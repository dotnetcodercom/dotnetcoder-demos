using System.Diagnostics;
using System.IO.Pipelines;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

var results = new List<object>();
int failures = 0;
Directory.CreateDirectory("evidence");
var assembly = typeof(JsonSerializer).Assembly;
var sdkProcess = Process.Start(new ProcessStartInfo("dotnet", "--version") { RedirectStandardOutput = true, UseShellExecute = false });
string? sdk = sdkProcess is null ? null : (await sdkProcess.StandardOutput.ReadToEndAsync()).Trim();
if (sdkProcess is not null) await sdkProcess.WaitForExitAsync();
async Task Check(string name, Func<Task<object?>> action)
{
    try
    {
        object? evidence = await action();
        results.Add(new { Name = name, Status = "PASS", Evidence = evidence });
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures++;
        results.Add(new { Name = name, Status = "FAIL", Error = ex.ToString() });
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}
await Check("RC1 public API available", () =>
{
    var methods = typeof(JsonSerializer).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(m => m.Name == "SerializeAsyncEnumerable").Select(m => m.ToString()).ToArray();
    Require(methods.Length == 4, "Expected four public overloads on the actual runtime.");
    return Task.FromResult<object?>(methods);
});
var rows = new[] { new ExportRow(1, "line one\nline two"), new ExportRow(2, "quote: \" and emoji: 🧪") };
var options = new JsonSerializerOptions { WriteIndented = true, NewLine = "\r\n" };
foreach (bool pipe in new[] { false, true })
    await Check($"{(pipe ? "PipeWriter" : "Stream")} canonical JSONL framing", async () =>
    {
        using var memory = new MemoryStream();
        if (pipe)
        {
            var writer = PipeWriter.Create(memory, new StreamPipeWriterOptions(leaveOpen: true));
            await JsonSerializer.SerializeAsyncEnumerable(writer, Sequence(rows), topLevelValues: true, options: options);
            await writer.CompleteAsync();
        }
        else await JsonSerializer.SerializeAsyncEnumerable(memory, Sequence(rows), topLevelValues: true, options: options);
        string body = Encoding.UTF8.GetString(memory.ToArray());
        Require(body.EndsWith('\n') && !body.Contains('\r'), "Expected LF including the last item, no CR.");
        string[] lines = body.Split('\n');
        Require(lines.Length == 3 && lines[^1] == "", "Each record must occupy one line.");
        Require(JsonSerializer.Deserialize<ExportRow>(lines[0]) == rows[0], "Embedded newline did not round trip.");
        Require(JsonSerializer.Deserialize<ExportRow>(lines[1]) == rows[1], "Quoted/Unicode string did not round trip.");
        return new { Utf8Bytes = memory.Length, Lines = lines, WriteIndentedIgnored = true, ConfiguredCrLfIgnored = true };
    });
await Check("Default output remains a JSON array", async () =>
{
    using var memory = new MemoryStream();
    await JsonSerializer.SerializeAsyncEnumerable(memory, Sequence(rows));
    string body = Encoding.UTF8.GetString(memory.ToArray());
    Require(JsonSerializer.Deserialize<ExportRow[]>(body)!.SequenceEqual(rows), "Array round trip failed.");
    return new { Body = body };
});
await Check("Empty sequence: zero JSONL bytes versus empty array", async () =>
{
    using var jsonl = new MemoryStream(); using var array = new MemoryStream();
    await JsonSerializer.SerializeAsyncEnumerable(jsonl, Sequence(Array.Empty<ExportRow>()), topLevelValues: true);
    await JsonSerializer.SerializeAsyncEnumerable(array, Sequence(Array.Empty<ExportRow>()));
    Require(jsonl.Length == 0, "Empty JSONL should contain no records or terminator.");
    Require(Encoding.UTF8.GetString(array.ToArray()) == "[]", "Empty array must be [].");
    return new { JsonlBytes = jsonl.Length, Array = "[]" };
});
var states = new System.Collections.Concurrent.ConcurrentDictionary<string, ExportState>();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production", Args = Array.Empty<string>() });
builder.Logging.ClearProviders(); builder.WebHost.UseUrls("http://127.0.0.1:0");
await using var app = builder.Build();
app.MapGet("/export/{id}", async (string id, HttpContext context) =>
{
    var state = states[id]; context.Response.ContentType = "application/x-ndjson; charset=utf-8";
    await context.Response.StartAsync(context.RequestAborted);
    try
    {
        await JsonSerializer.SerializeAsyncEnumerable(context.Response.Body, Controlled(state, context.RequestAborted),
            topLevelValues: true, options: new JsonSerializerOptions { DefaultBufferSize = state.BufferSize },
            cancellationToken: context.RequestAborted);
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { state.Cancelled.TrySetResult(); }
    catch (InvalidOperationException ex) when (ex.Message == "Injected producer failure") { state.ProducerFailed = true; context.Abort(); }
    finally { state.Finished.TrySetResult(); }
});
await app.StartAsync();
var baseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
using var client = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(10) };
foreach (int buffer in new[] { 16 * 1024, 128 })
    await Check($"HTTP first JSONL record before producer completion (buffer {buffer})", async () =>
    {
        var state = new ExportState(buffer, fail: false); string id = Guid.NewGuid().ToString("N"); states[id] = state;
        try
        {
            using var response = await client.GetAsync($"/export/{id}", HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode(); using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
            string first = (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!;
            Require(JsonSerializer.Deserialize<ExportRow>(first)!.Id == 1, "Expected first complete record.");
            Require(!state.Finished.Task.IsCompleted && !state.Release.Task.IsCompleted, "Producer had already completed.");
            state.Release.TrySetResult(); string rest = await reader.ReadToEndAsync();
            Require(JsonSerializer.Deserialize<ExportRow>(rest.TrimEnd('\n'))!.Id == 2, "Expected second record.");
            await state.Finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
            return new { FirstRecordBeforeProducerRelease = true, BufferSize = buffer, PayloadCharacters = 200, FirstRecord = first, RemainingBody = rest };
        }
        finally { state.Release.TrySetResult(); }
    });
await Check("HTTP consumer closes stream: RequestAborted stops producer", async () =>
{
    var state = new ExportState(128, fail: false); string id = Guid.NewGuid().ToString("N"); states[id] = state;
    try
    {
        var response = await client.GetAsync($"/export/{id}", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode(); var stream = await response.Content.ReadAsStreamAsync();
        using (var reader = new StreamReader(stream))
        {
            string first = (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!;
            Require(JsonSerializer.Deserialize<ExportRow>(first)!.Id == 1, "No first record before cancellation.");
            response.Dispose();
        }
        await state.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Require(!state.Release.Task.IsCompleted, "Test released the producer instead of cancelling it.");
        return new { RequestAbortedObserved = true, ProducerReleaseNotUsed = true };
    }
    finally { state.Release.TrySetResult(); }
});
await Check("HTTP producer failure leaves a valid prefix and a failed transfer", async () =>
{
    var state = new ExportState(128, fail: true); string id = Guid.NewGuid().ToString("N"); states[id] = state;
    try
    {
        using var response = await client.GetAsync($"/export/{id}", HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode(); using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        string first = (await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(3)))!;
        Require(JsonSerializer.Deserialize<ExportRow>(first)!.Id == 1, "First prefix record invalid.");
        state.Release.TrySetResult(); string? error = null;
        try { await reader.ReadToEndAsync(); }
        catch (IOException ex) { error = ex.GetType().Name + ": " + ex.Message; }
        await state.Finished.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Require(state.ProducerFailed && error is not null, "Expected injected failure plus client transfer error.");
        return new { FirstRecord = first, ValidPrefixIsNotWholeExport = true, InitialStatusCode = (int)response.StatusCode, TransferError = error };
    }
    finally { state.Release.TrySetResult(); }
});
await app.StopAsync();
var receipt = new
{
    Status = failures == 0 ? "PASS" : "FAIL", AtUtc = DateTimeOffset.UtcNow, Sdk = sdk,
    Framework = RuntimeInformation.FrameworkDescription, Assembly = assembly.FullName,
    InformationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
    FailedChecks = failures, CheckCount = results.Count, Results = results,
    Scope = "Pinned .NET 11 RC1, local Kestrel HTTP/1.1, Stream/PipeWriter framing, two buffer sizes and 200-character payload, consumer stream closure and injected producer failure. No proxy/compression/CDN, throughput, per-record universal flush or production network claim."
};
await File.WriteAllTextAsync("evidence/receipt.json", JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"RESULT {receipt.Status}: {results.Count - failures}/{results.Count} checks");
return failures == 0 ? 0 : 1;
static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static async IAsyncEnumerable<ExportRow> Sequence(IEnumerable<ExportRow> source)
{ foreach (var row in source) { yield return row; await Task.Yield(); } }
static async IAsyncEnumerable<ExportRow> Controlled(ExportState state, [EnumeratorCancellation] CancellationToken token)
{
    yield return new ExportRow(1, new string('a', 200)); await state.Release.Task.WaitAsync(token);
    if (state.Fail) throw new InvalidOperationException("Injected producer failure");
    yield return new ExportRow(2, "last row");
}
sealed record ExportRow(int Id, string Text);
sealed class ExportState(int bufferSize, bool fail)
{
    public int BufferSize { get; } = bufferSize; public bool Fail { get; } = fail; public bool ProducerFailed { get; set; }
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
