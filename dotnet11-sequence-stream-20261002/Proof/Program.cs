using System.Buffers;
using System.IO.Pipelines;
using System.Security.Cryptography;
using System.Text.Json;

var evidence = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("evidence");
Directory.CreateDirectory(evidence);
var checks = new List<object>();
var started = DateTimeOffset.UtcNow;
var payload = Enumerable.Range(0, 128).Select(i => (byte)i).ToArray();
var failed = false;

void Check(bool condition, string name, object? details = null)
{
    checks.Add(new { name, status = condition ? "PASS" : "FAIL", details });
    Console.WriteLine((condition ? "PASS: " : "FAIL: ") + name);
    if (!condition) throw new InvalidOperationException(name);
}

async Task<(Pipe Pipe, ReadOnlySequence<byte> Buffer)> CreateRead(PoisonOnReturnPool pool)
{
    var pipe = new Pipe(new PipeOptions(pool: pool, minimumSegmentSize: 64, pauseWriterThreshold: 1024, resumeWriterThreshold: 512, useSynchronizationContext: false));
    for (var offset = 0; offset < payload.Length; offset += 64)
    {
        // Each pool rental has exactly 64 bytes, forcing a second segment.
        payload.AsSpan(offset, 64).CopyTo(pipe.Writer.GetSpan(64));
        pipe.Writer.Advance(64);
    }
    await pipe.Writer.FlushAsync();
    await pipe.Writer.CompleteAsync();
    var read = await pipe.Reader.ReadAsync();
    Check(!read.Buffer.IsSingleSegment, "PipeReader provides a multi-segment payload");
    Check(read.Buffer.Length == payload.Length, "PipeReader payload length is correct");
    return (pipe, read.Buffer);
}

async Task<byte[]> DelayedConsume(Stream source, Task release)
{
    // A deterministic gate represents a downstream consumer that has not read yet.
    await release;
    using var destination = new MemoryStream();
    await source.CopyToAsync(destination);
    return destination.ToArray();
}

try
{
    Console.WriteLine("=== DNC .NET 11 ReadOnlySequenceStream proof ===");
    Console.WriteLine("Runtime: " + Environment.Version);
    Console.WriteLine("API assembly: " + typeof(ReadOnlySequenceStream).Assembly.FullName);
    Console.WriteLine("No server, Azure, Docker or external packages are used.");

    Console.WriteLine("\n--- Safe: consume before AdvanceTo / CompleteAsync ---");
    using (var pool = new PoisonOnReturnPool())
    {
        var (pipe, buffer) = await CreateRead(pool);
        try
        {
            using var source = new ReadOnlySequenceStream(buffer);
            Check(!source.CanSeek && !source.CanWrite, "Adapter is read-only and non-seekable");
            try { _ = source.Length; throw new Exception("Length unexpectedly succeeded"); }
            catch (NotSupportedException) { Check(true, "Length throws NotSupportedException"); }
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var reading = DelayedConsume(source, gate.Task);
            Check(pool.ReturnedCount == 0, "Buffers remain owned while consumption is pending");
            gate.SetResult();
            var actual = await reading;
            Check(actual.AsSpan().SequenceEqual(payload), "Awaited consumption preserves all payload bytes", new { sha256 = Convert.ToHexString(SHA256.HashData(actual)) });
            pipe.Reader.AdvanceTo(buffer.End);
        }
        finally { await pipe.Reader.CompleteAsync(); }
        Check(pool.ReturnedCount >= 2, "Reader completion returns the rented segments", new { pool.RentedCount, pool.ReturnedCount });
    }

    Console.WriteLine("\n--- Unsafe negative control: return buffers before async consumption ---");
    using (var pool = new PoisonOnReturnPool())
    {
        var (pipe, buffer) = await CreateRead(pool);
        using var source = new ReadOnlySequenceStream(buffer);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = DelayedConsume(source, gate.Task);
        // Deliberately broken lifetime ordering, restricted to this negative test.
        pipe.Reader.AdvanceTo(buffer.End);
        await pipe.Reader.CompleteAsync();
        Check(pool.ReturnedCount >= 2, "Negative control returns buffers while consumer is pending");
        gate.SetResult();
        var unsafeDetected = false;
        try
        {
            var corrupted = await reading;
            unsafeDetected = !corrupted.AsSpan().SequenceEqual(payload);
            Console.WriteLine("EXPECTED UNSAFE RESULT: payload changed after the pipe returned its memory");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            // Pipe completion may invalidate segment links as well as release memory.
            unsafeDetected = true;
            Console.WriteLine("EXPECTED UNSAFE RESULT: " + ex.GetType().Name + " - " + ex.Message);
        }
        Check(unsafeDetected, "Negative control detects unsafe access to a completed pipe's sequence");
    }

    Console.WriteLine("\n--- Direct pooled-memory negative control (no pipe segment reset) ---");
    using (var pool = new PoisonOnReturnPool())
    {
        using var owner = pool.Rent(64);
        payload.AsSpan(0, 64).CopyTo(owner.Memory.Span);
        using var source = new ReadOnlySequenceStream(new ReadOnlySequence<byte>(owner.Memory));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reading = DelayedConsume(source, gate.Task);
        owner.Dispose();
        gate.SetResult();
        var corrupted = await reading;
        Check(corrupted.Length == 64 && corrupted.All(x => x == PoisonOnReturnPool.Poison), "Returned pooled memory is observed as 0xDD by a delayed adapter consumer");
        Console.WriteLine("EXPECTED UNSAFE RESULT: source ownership was released before consumption");
    }

    Console.WriteLine("\n--- Owning copy: independent of the pipe lifetime ---");
    using (var pool = new PoisonOnReturnPool())
    {
        var (pipe, buffer) = await CreateRead(pool);
        var owned = buffer.ToArray();
        pipe.Reader.AdvanceTo(buffer.End);
        await pipe.Reader.CompleteAsync();
        using var source = new MemoryStream(owned, writable: false);
        var actual = await DelayedConsume(source, Task.CompletedTask);
        Check(actual.AsSpan().SequenceEqual(payload), "An intentional owning copy survives pipe completion");
    }

    Console.WriteLine("\n--- Adapter disposal does not own the source memory ---");
    using (var pool = new PoisonOnReturnPool())
    {
        var (pipe, buffer) = await CreateRead(pool);
        var source = new ReadOnlySequenceStream(buffer);
        source.Dispose();
        Check(pool.ReturnedCount == 0, "Disposing the adapter does not return pipe buffers");
        try { source.ReadByte(); throw new Exception("Disposed read unexpectedly succeeded"); }
        catch (ObjectDisposedException) { Check(true, "Read after adapter disposal throws ObjectDisposedException"); }
        pipe.Reader.AdvanceTo(buffer.End);
        await pipe.Reader.CompleteAsync();
    }

    Console.WriteLine("\n--- Pre-cancelled read ---");
    using (var source = new ReadOnlySequenceStream(new ReadOnlySequence<byte>(payload)))
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        try { var read = await source.ReadAsync(new byte[8].AsMemory(), cts.Token); throw new Exception($"Cancelled read unexpectedly returned {read} bytes"); }
        catch (OperationCanceledException) { Check(true, "Pre-cancelled ReadAsync observes cancellation"); }
    }
    Console.WriteLine("\nPASS: ReadOnlySequenceStream proof completed");
    Console.WriteLine("INTERPRETATION: no contiguous source copy is required; normal reads still copy into their destination.");
    Console.WriteLine("This test does not measure throughput or claim end-to-end zero-copy HTTP.");
}
catch (Exception ex)
{
    failed = true;
    checks.Add(new { name = "unhandled_error", status = "FAIL", details = ex.ToString() });
    Console.Error.WriteLine("FAIL: " + ex);
}
finally
{
    var receipt = new { demo = "DNC-NET11-SequenceStream-Proof", candidate_id = "DNC-MANUAL-20261002-1016-C05", started_at = started, finished_at = DateTimeOffset.UtcNow, runtime = Environment.Version.ToString(), outcome = failed ? "FAIL" : "PASS", controlled_negative_test = "custom memory pool poisons returned buffers with 0xDD; this is not a prediction of every production pool", checks };
    await File.WriteAllTextAsync(Path.Combine(evidence, "receipt.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("RECEIPT: " + Path.Combine(evidence, "receipt.json"));
}
return failed ? 1 : 0;

sealed class PoisonOnReturnPool : MemoryPool<byte>
{
    public const byte Poison = 0xDD;
    public int RentedCount { get; private set; }
    public int ReturnedCount { get; private set; }
    public override int MaxBufferSize => 64;
    public override IMemoryOwner<byte> Rent(int minBufferSize = -1)
    {
        if (minBufferSize > 64) throw new ArgumentOutOfRangeException(nameof(minBufferSize));
        RentedCount++;
        return new Owner(this);
    }
    protected override void Dispose(bool disposing) { }
    sealed class Owner(PoisonOnReturnPool pool) : IMemoryOwner<byte>
    {
        private readonly byte[] bytes = new byte[64];
        private bool returned;
        public Memory<byte> Memory => bytes;
        public void Dispose()
        {
            if (returned) return;
            returned = true;
            Array.Fill(bytes, Poison);
            pool.ReturnedCount++;
        }
    }
}
