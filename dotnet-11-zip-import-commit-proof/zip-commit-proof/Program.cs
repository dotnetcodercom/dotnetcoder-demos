using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var root = Path.Combine(Directory.GetCurrentDirectory(), "work");
Directory.CreateDirectory(root);
var cases = new List<object>();
foreach (var item in new[] {
    ("valid", "valid.zip", false, false, false, true),
    ("corrupt-first", "corrupt-first.zip", false, false, false, false),
    ("corrupt-last", "corrupt-last.zip", false, false, false, false),
    ("cancel-between-entries", "valid.zip", true, false, false, false),
    ("fail-before-replace", "valid.zip", false, true, false, false),
    ("invalid-record-last", "invalid-record.zip", false, false, false, false),
    ("unsafe-corrupt-last", "corrupt-last.zip", false, false, true, false)
}) {
    var (name, archiveName, cancel, failCommit, unsafeMode, expectedCommit) = item;
    var dir = Path.Combine(root, name);
    Directory.CreateDirectory(dir);
    var statePath = Path.Combine(dir, "records.json");
    File.WriteAllText(statePath, "{\"seed\":\"keep\"}");
    string before = Hash(statePath);
    string outcome = "committed";
    int validated = 0;
    using var cancellation = new CancellationTokenSource();
    try {
        using var zip = ZipFile.OpenRead(archiveName);
        var staged = new Dictionary<string, string> { ["seed"] = "keep" };
        foreach (var entry in zip.Entries) {
            using var input = entry.Open();
            using var buffer = new MemoryStream();
            input.CopyTo(buffer, 3); // Read to EOF before interpreting or committing any record.
            string text = Encoding.UTF8.GetString(buffer.ToArray());
            var parts = text.Split('=', 2);
            if (parts.Length != 2 || parts[0].Length == 0) throw new FormatException("Invalid record");
            staged.Add(parts[0], parts[1]);
            validated++;
            if (unsafeMode) File.WriteAllText(statePath, JsonSerializer.Serialize(staged));
            if (cancel && validated == 1) cancellation.Cancel();
            cancellation.Token.ThrowIfCancellationRequested();
        }
        if (unsafeMode) throw new InvalidOperationException("Unsafe control must fail at bad second entry");
        var temp = statePath + ".pending";
        try {
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                JsonSerializer.Serialize(output, staged);
                output.Flush(flushToDisk: true);
            }
            if (failCommit) throw new IOException("Injected failure before replacing state");
            File.Move(temp, statePath, overwrite: true);
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    } catch (Exception ex) { outcome = ex.GetType().Name; }
    string after = Hash(statePath);
    bool changed = before != after;
    var records = JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(statePath))!;
    bool pass = unsafeMode
        ? outcome == "InvalidDataException" && changed && records.ContainsKey("first") && !records.ContainsKey("second")
        : expectedCommit
            ? outcome == "committed" && changed && records.Count == 3
            : outcome != "committed" && !changed && records.Count == 1;
    bool tempClean = !File.Exists(statePath + ".pending");
    pass &= tempClean;
    cases.Add(new { name, outcome, validated_entries=validated, state_changed=changed, record_count=records.Count, temp_clean=tempClean, pass });
}
var report = new { runtime=System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    os=System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    scope="Synchronous ZIP_STORED reads; single local JSON state file replaced on the same filesystem. No database, HTTP endpoint, encryption, crash/power-loss guarantee or concurrent importer verified.", cases };
var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented=true });
Console.WriteLine(json);
File.WriteAllText("receipt.json", json);
return cases.All(c => (bool)c.GetType().GetProperty("pass")!.GetValue(c)!) ? 0 : 1;

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
