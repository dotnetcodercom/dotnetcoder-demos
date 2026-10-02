using System.IO.Compression;
using System.Text;
using System.Text.Json;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: ZipImport <archive.zip> <records.json>");
    return 2;
}

try
{
    string statePath = Path.GetFullPath(args[1]);
    var staged = JsonSerializer.Deserialize<Dictionary<string, string>>(
        File.ReadAllText(statePath))
        ?? throw new FormatException("Expected a JSON object.");
    using var archive = ZipFile.OpenRead(args[0]);
    if (archive.Entries.Count is 0 or > 16)
        throw new InvalidDataException("Expected 1–16 record entries.");

    long totalBytes = 0;
    foreach (var entry in archive.Entries)
    {
        if (entry.Length is 0 or > 65_536)
            throw new InvalidDataException("Record size exceeds this sample's policy.");
        totalBytes = checked(totalBytes + entry.Length);
        if (totalBytes > 262_144)
            throw new InvalidDataException("Archive exceeds this sample's byte policy.");

        using var input = entry.Open();
        using var buffer = new MemoryStream();
        input.CopyTo(buffer); // Reach EOF before parsing or saving this record.
        string text = new UTF8Encoding(false, true).GetString(buffer.ToArray());
        var record = text.Split('=', 2);
        if (record.Length != 2 || record[0].Length == 0)
            throw new FormatException("Expected key=value.");
        staged.Add(record[0], record[1]); // Reject duplicate and existing keys.
    }

    string pending = statePath + "." + Guid.NewGuid().ToString("N") + ".pending";
    try
    {
        using (var output = new FileStream(
            pending, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(output, staged);
            output.Flush(flushToDisk: true);
        }
        File.Move(pending, statePath, overwrite: true);
    }
    finally
    {
        if (File.Exists(pending)) File.Delete(pending);
    }
    Console.WriteLine("Committed");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message}");
    return 1;
}
