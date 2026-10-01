using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;

internal static class Program
{
    private const int MiB = 1024 * 1024;
    // Public local-emulator key, not an Azure credential. No external endpoint is accepted.
    private const string LocalKey = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly List<object> Checks = [];
    private static readonly Dictionary<string, object?> Evidence = [];
    private static string Output = "result.json";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Parse(args);
            Output = options["out"];
            string run = options["run"];
            if (run.Length != 32 || run.Any(c => !Uri.IsHexDigit(c)))
                throw new ArgumentException("run must be a 32-character hexadecimal run ID.");
            string label = options["label"];
            if (label is not ("before" or "after")) throw new ArgumentException("Unknown label.");
            string expectedVersion = label == "before" ? "12.29.2" : "12.30.0";
            string actualVersion = typeof(BlobClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";
            Evidence["sdkVersion"] = actualVersion;
            Evidence["expectedSdkVersion"] = expectedVersion;
            Evidence["runId"] = run;
            Evidence["runtime"] = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
            Evidence["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription;
            Evidence["architecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString();
            Evidence["mode"] = options["mode"];
            Evidence["label"] = label;
            Evidence["localEndpoint"] = "http://127.0.0.1:10080/dncproof";
            Evidence["serviceVersion"] = "2023-11-03";
            Check("Exact requested SDK loaded", actualVersion.Split('+')[0] == expectedVersion);
            var requests = new RequestProbe();
            var clientOptions = new BlobClientOptions(BlobClientOptions.ServiceVersion.V2023_11_03);
            clientOptions.Retry.MaxRetries = 0;
            clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(30);
            clientOptions.AddPolicy(requests, HttpPipelinePosition.PerRetry);
            var service = new BlobServiceClient($"DefaultEndpointsProtocol=http;AccountName=dncproof;AccountKey={LocalKey};BlobEndpoint=http://127.0.0.1:10080/dncproof;", clientOptions);
            var container = service.GetBlobContainerClient($"dnc-proof-{run.ToLowerInvariant()}-{label}");
            Evidence["container"] = container.Name;
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
            switch (options["mode"])
            {
                case "blocks":
                    await container.CreateIfNotExistsAsync(cancellationToken: timeout.Token);
                    await VerifyBlocks(container, label, Path.GetDirectoryName(Path.GetFullPath(Output))!, timeout.Token);
                    break;
                case "seed":
                    await container.CreateIfNotExistsAsync(cancellationToken: timeout.Token);
                    await Seed(container, options["fixture"], timeout.Token);
                    break;
                case "download":
                    await Download(container, options["profile"], options["fixture"], requests, timeout.Token);
                    break;
                default: throw new ArgumentException("Unknown mode.");
            }
            Evidence["status"] = "PASS";
            return 0;
        }
        catch (Exception ex)
        {
            Evidence["status"] = "FAIL";
            Evidence["error"] = ex.ToString();
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            Evidence["utc"] = DateTimeOffset.UtcNow;
            Evidence["checks"] = Checks;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(Output))!);
            await File.WriteAllTextAsync(Output, JsonSerializer.Serialize(Evidence, Json));
            Console.WriteLine($"Result: {Evidence.GetValueOrDefault("status")} | {Output}");
        }
    }

    private static Dictionary<string, string> Parse(string[] args)
    {
        if (args.Length % 2 != 0) throw new ArgumentException("Use --name value pairs.");
        var result = new Dictionary<string, string>();
        for (int i = 0; i < args.Length; i += 2) result.Add(args[i].TrimStart('-'), args[i + 1]);
        return result;
    }

    private static void Check(string name, bool pass)
    {
        Checks.Add(new { name, pass });
        Console.WriteLine($"[{(pass ? "PASS" : "FAIL")}] {name}");
        if (!pass) throw new InvalidOperationException(name);
    }

    private static byte[] Payload(int size)
    {
        byte[] bytes = new byte[size];
        for (int i = 0; i < size; i++) bytes[i] = (byte)((i % 251 + i / MiB * 17) % 251);
        return bytes;
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static async Task<string> FileHash(string path)
    {
        await using var file = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(file));
    }

    private static StorageTransferOptions Transfer(int concurrency, int size) => new()
    {
        MaximumConcurrency = concurrency,
        MaximumTransferSize = size,
        InitialTransferSize = size
    };

    private static async Task VerifyBlocks(BlobContainerClient container, string label, string directory, CancellationToken token)
    {
        byte[] payload = Payload(8 * MiB);
        var blob = container.GetBlockBlobClient("sdk-partitioned.bin");
        var upload = new BlobUploadOptions { TransferOptions = Transfer(4, MiB) };
        using (var source = new MemoryStream(payload)) await blob.UploadAsync(source, upload, token);
        string[] first = (await blob.GetBlockListAsync(BlockListTypes.Committed, cancellationToken: token)).Value.CommittedBlocks.Select(b => b.Name).ToArray();
        Check("Partitioned upload has eight committed blocks", first.Length == 8);
        Check("First upload SHA256 matches", Hash((await blob.DownloadContentAsync(token)).Value.Content.ToArray()) == Hash(payload));
        using (var source = new MemoryStream(payload)) await blob.UploadAsync(source, upload, token);
        string[] second = (await blob.GetBlockListAsync(BlockListTypes.Committed, cancellationToken: token)).Value.CommittedBlocks.Select(b => b.Name).ToArray();
        Check("Second partitioned upload has eight committed blocks", second.Length == 8);
        Check("Second upload SHA256 matches", Hash((await blob.DownloadContentAsync(token)).Value.Content.ToArray()) == Hash(payload));
        int overlap = first.Intersect(second, StringComparer.Ordinal).Count();
        Check(label == "after" ? "New SDK IDs differ across repeated uploads" : "Previous SDK reuses its sequential IDs", label == "after" ? overlap == 0 : first.SequenceEqual(second));
        Evidence["sdkBlockIds"] = new { first, second, overlap, idsAreSortedInThisSample = second.SequenceEqual(second.OrderBy(x => x, StringComparer.Ordinal)), firstDecodedHex = first.Select(x => Convert.ToHexString(Convert.FromBase64String(x))).ToArray() };

        // Custom resumable uploads own the IDs and persist the exact commit order.
        var resume = container.GetBlockBlobClient("application-resume.bin");
        byte[] resumable = Payload(6 * MiB);
        string session = Guid.NewGuid().ToString("N");
        var manifest = new UploadManifest(session, Hash(resumable), MiB,
            Enumerable.Range(0, 6).Select(i => Convert.ToBase64String(Encoding.ASCII.GetBytes($"{session}:{i:D8}"))).ToArray());
        string manifestPath = Path.Combine(directory, $"{label}-resume-manifest.json");
        await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest, Json), token);
        for (int i = 0; i < 2; i++)
        {
            using var part = new MemoryStream(resumable, i * MiB, MiB);
            await resume.StageBlockAsync(manifest.OrderedBlockIds[i], part, cancellationToken: token);
        }
        Check("Interrupted upload has two uncommitted blocks", (await resume.GetBlockListAsync(BlockListTypes.Uncommitted, cancellationToken: token)).Value.UncommittedBlocks.Count() == 2);
        var loaded = JsonSerializer.Deserialize<UploadManifest>(await File.ReadAllTextAsync(manifestPath, token))!;
        Check("Persisted manifest reload preserves order", loaded.OrderedBlockIds.SequenceEqual(manifest.OrderedBlockIds));
        bool changedSourceRejected = false;
        try { await ResumeUpload(resume, loaded, Payload(5 * MiB), token); }
        catch (InvalidDataException) { changedSourceRejected = true; }
        Check("Resume rejects changed source by SHA256", changedSourceRejected);
        Check("Rejected resume leaves staged blocks unchanged", (await resume.GetBlockListAsync(BlockListTypes.Uncommitted, cancellationToken: token)).Value.UncommittedBlocks.Count() == 2);
        Check("Resume source SHA256 matches saved manifest", loaded.Sha256 == Hash(resumable));
        int reused = await ResumeUpload(resume, loaded, resumable, token);
        Check("Resume reuses the two staged blocks", reused == 2);
        Check("All six manifest blocks are staged", (await resume.GetBlockListAsync(BlockListTypes.Uncommitted, cancellationToken: token)).Value.UncommittedBlocks.Count() == 6);
        await resume.CommitBlockListAsync(loaded.OrderedBlockIds, cancellationToken: token);
        Check("Manifest-order commit restores complete file", Hash((await resume.DownloadContentAsync(token)).Value.Content.ToArray()) == loaded.Sha256);
        await resume.CommitBlockListAsync(loaded.OrderedBlockIds.Reverse(), cancellationToken: token);
        Check("Wrong commit order changes file bytes", Hash((await resume.DownloadContentAsync(token)).Value.Content.ToArray()) != loaded.Sha256);
        await resume.CommitBlockListAsync(loaded.OrderedBlockIds, cancellationToken: token);
        Check("Original manifest order restores SHA256", Hash((await resume.DownloadContentAsync(token)).Value.Content.ToArray()) == loaded.Sha256);
        Evidence["resume"] = new { session, reused, blocks = 6, sha256 = loaded.Sha256, manifestFile = Path.GetFileName(manifestPath) };

        // Distinct IDs do not grant two writers permission to replace the same blob.
        var guarded = container.GetBlockBlobClient("etag-guard.bin");
        using (var source = new MemoryStream(Payload(MiB))) await guarded.UploadAsync(source, cancellationToken: token);
        var originalEtag = (await guarded.GetPropertiesAsync(cancellationToken: token)).Value.ETag;
        string writerA = Convert.ToBase64String(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N")));
        string writerB = Convert.ToBase64String(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N")));
        byte[] contentA = Payload(2 * MiB);
        byte[] contentB = Payload(3 * MiB);
        using (var source = new MemoryStream(contentA)) await guarded.StageBlockAsync(writerA, source, cancellationToken: token);
        using (var source = new MemoryStream(contentB)) await guarded.StageBlockAsync(writerB, source, cancellationToken: token);
        var conditional = new CommitBlockListOptions { Conditions = new BlobRequestConditions { IfMatch = originalEtag } };
        await guarded.CommitBlockListAsync([writerA], conditional, token);
        Check("Writer A commits with current ETag", Hash((await guarded.DownloadContentAsync(token)).Value.Content.ToArray()) == Hash(contentA));
        int rejectedStatus = 0;
        try { await guarded.CommitBlockListAsync([writerB], conditional, token); }
        catch (RequestFailedException ex) { rejectedStatus = ex.Status; }
        Check("Writer B stale ETag rejected with HTTP 412", rejectedStatus == 412);
        Check("Rejected writer leaves winner content intact", Hash((await guarded.DownloadContentAsync(token)).Value.Content.ToArray()) == Hash(contentA));
        Evidence["etagGuard"] = new { staleWriterHttpStatus = rejectedStatus, winnerSha256 = Hash(contentA), scope = "two staged writers, serialized conditional commits; not a load test" };

        int missingStatus = 0;
        try { await container.GetBlobClient("does-not-exist.bin").DownloadContentAsync(token); }
        catch (RequestFailedException ex) { missingStatus = ex.Status; }
        Check("Missing blob returns HTTP 404", missingStatus == 404);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        bool canceledCorrectly = false;
        try { await blob.DownloadToAsync(Stream.Null, canceled.Token); }
        catch (OperationCanceledException) { canceledCorrectly = true; }
        Check("Pre-canceled download throws cancellation", canceledCorrectly);
    }

    private static async Task<int> ResumeUpload(BlockBlobClient blob, UploadManifest manifest, byte[] source, CancellationToken token)
    {
        // Validate before any service call; never reuse blocks for a changed source.
        if (Hash(source) != manifest.Sha256) throw new InvalidDataException("Source changed since the manifest was written.");
        if (manifest.BlockSize <= 0 || source.Length != manifest.BlockSize * (long)manifest.OrderedBlockIds.Length)
            throw new InvalidDataException("This demo requires complete, equal-sized manifest blocks.");
        var existing = (await blob.GetBlockListAsync(BlockListTypes.Uncommitted, cancellationToken: token)).Value.UncommittedBlocks.ToDictionary(x => x.Name);
        int reused = 0;
        for (int i = 0; i < manifest.OrderedBlockIds.Length; i++)
        {
            if (existing.TryGetValue(manifest.OrderedBlockIds[i], out var staged) && staged.Size == manifest.BlockSize) { reused++; continue; }
            using var part = new MemoryStream(source, i * manifest.BlockSize, manifest.BlockSize);
            await blob.StageBlockAsync(manifest.OrderedBlockIds[i], part, cancellationToken: token);
        }
        return reused;
    }

    private static async Task Seed(BlobContainerClient container, string fixture, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(fixture))!);
        if (!File.Exists(fixture))
        {
            await using var file = File.Create(fixture);
            for (int block = 0; block < 64; block++)
            {
                byte[] bytes = Payload(MiB);
                for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)((bytes[i] + block * 17) % 251);
                await file.WriteAsync(bytes, token);
            }
        }
        Check("Download fixture is 64 MiB", new FileInfo(fixture).Length == 64L * MiB);
        await using var source = File.OpenRead(fixture);
        var blob = container.GetBlobClient("memory-fixture.bin");
        await blob.UploadAsync(source, new BlobUploadOptions { TransferOptions = Transfer(4, 4 * MiB) }, token);
        Check("Seeded blob length matches fixture", (await blob.GetPropertiesAsync(cancellationToken: token)).Value.ContentLength == source.Length);
        Evidence["fixtureSha256"] = await FileHash(fixture);
        Evidence["fixtureBytes"] = source.Length;
    }

    private static async Task Download(BlobContainerClient container, string profile, string fixture, RequestProbe requests, CancellationToken token)
    {
        (int concurrency, int chunkMiB) = profile switch
        {
            "low" => (1, 1), "balanced" => (4, 4), "wide" => (8, 8),
            _ => throw new ArgumentException("Unknown profile")
        };
        Evidence["profile"] = profile;
        Evidence["maximumConcurrency"] = concurrency;
        Evidence["maximumTransferMiB"] = chunkMiB;
        Evidence["initialTransferMiB"] = chunkMiB;
        Evidence["rangeWindowEstimateMiB"] = concurrency * chunkMiB;
        Evidence["estimateIsProcessMemoryCap"] = false;
        var blob = container.GetBlobClient("memory-fixture.bin");
        Check("Seeded blob exists", (await blob.GetPropertiesAsync(cancellationToken: token)).Value.ContentLength == 64L * MiB);
        string path = Path.ChangeExtension(Output, ".download.bin");
        long baselineManaged = GC.GetTotalMemory(true);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        long baselineWorkingSet = process.WorkingSet64;
        long peakManaged = baselineManaged, peakWorkingSet = baselineWorkingSet;
        using var sampleCancel = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            try
            {
                while (!sampleCancel.IsCancellationRequested)
                {
                    peakManaged = Math.Max(peakManaged, GC.GetTotalMemory(false));
                    process.Refresh();
                    peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                    await Task.Delay(10, sampleCancel.Token);
                }
            }
            catch (OperationCanceledException) { }
        });
        requests.Reset();
        var watch = Stopwatch.StartNew();
        try
        {
            await using var destination = File.Create(path);
            await blob.DownloadToAsync(destination, new BlobDownloadToOptions { TransferOptions = Transfer(concurrency, chunkMiB * MiB) }, token);
            await destination.FlushAsync(token);
        }
        finally
        {
            watch.Stop();
            sampleCancel.Cancel();
            await sampler;
        }
        Check("Downloaded file length is 64 MiB", new FileInfo(path).Length == 64L * MiB);
        Check("Downloaded SHA256 matches source", await FileHash(path) == await FileHash(fixture));
        var rangeRequests = requests.Snapshot();
        Check("Transfer used multiple HTTP ranges", rangeRequests.Length > 1);
        Check("Requested ranges respect configured range size", rangeRequests.All(r => RangeLength(r) <= chunkMiB * (long)MiB));
        Evidence["elapsedMilliseconds"] = watch.Elapsed.TotalMilliseconds;
        Evidence["baselineManagedBytes"] = baselineManaged;
        Evidence["sampledPeakManagedBytes"] = peakManaged;
        Evidence["baselineWorkingSetBytes"] = baselineWorkingSet;
        Evidence["sampledPeakWorkingSetBytes"] = peakWorkingSet;
        Evidence["sampleIntervalMilliseconds"] = 10;
        Evidence["rangeRequests"] = rangeRequests;
        Evidence["downloadSha256"] = await FileHash(path);
        Evidence["memoryMeasurement"] = "Observed process/managed memory on local Azurite. Peaks may be missed; not an SDK-only allocation count, hard cap, or Azure throughput benchmark.";
        File.Delete(path); // Only the temporary file created in this invocation.
    }

    private static long RangeLength(string range)
    {
        var values = range.Split('=')[1].Split('-');
        return long.Parse(values[1]) - long.Parse(values[0]) + 1;
    }

    private sealed record UploadManifest(string Session, string Sha256, int BlockSize, string[] OrderedBlockIds);

    private sealed class RequestProbe : HttpPipelineSynchronousPolicy
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> ranges = new();
        public override void OnSendingRequest(HttpMessage message)
        {
            if (message.Request.Method == RequestMethod.Get && message.Request.Headers.TryGetValue("x-ms-range", out string? range)) ranges.Enqueue(range);
        }
        public void Reset() { while (ranges.TryDequeue(out _)) { } }
        public string[] Snapshot() => ranges.ToArray();
    }
}
