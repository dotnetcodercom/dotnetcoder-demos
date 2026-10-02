using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

var root = args.Length > 0 ? Path.GetFullPath(args[0]) : throw new ArgumentException("Pass the demo root directory.");
var mode = args.Length > 1 ? args[1].ToLowerInvariant() : "isolated";
if (mode is not ("isolated" or "shared")) throw new ArgumentException("Mode must be isolated or shared.");
var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
var evidence = Path.Combine(root, "evidence", stamp + "-" + mode);
Directory.CreateDirectory(evidence);
var checks = new List<object>();
var dotnet = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
var started = DateTimeOffset.UtcNow;
var failed = false;
var outcome = "FAIL";
var selectedSdk = "UNKNOWN";

async Task<(int ExitCode, string Stdout, string Stderr)> Execute(string label, params string[] arguments)
{
    Console.WriteLine("COMMAND: dotnet " + string.Join(" ", arguments.Select(x => x.Contains(' ') ? "\"" + x + "\"" : x)));
    var info = new ProcessStartInfo(dotnet) { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
    foreach (var argument in arguments) info.ArgumentList.Add(argument);
    using var process = Process.Start(info) ?? throw new InvalidOperationException("Cannot start dotnet.");
    var stdout = process.StandardOutput.ReadToEndAsync();
    var stderr = process.StandardError.ReadToEndAsync();
    try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(5)); }
    catch { if (!process.HasExited) process.Kill(entireProcessTree: true); throw; }
    var result = (process.ExitCode, await stdout, await stderr);
    await File.WriteAllTextAsync(Path.Combine(evidence, label + ".stdout.txt"), result.Item2);
    await File.WriteAllTextAsync(Path.Combine(evidence, label + ".stderr.txt"), result.Item3);
    Console.Write(result.Item2);
    if (result.Item3.Length > 0) Console.Write(result.Item3);
    lock (checks) checks.Add(new { name = label, exit_code = result.ExitCode, command = arguments });
    return result;
}

try
{
    Console.WriteLine("=== DNC parallel .NET publish proof ===");
    Console.WriteLine("MODE: " + mode);
    Console.WriteLine("SCOPE: local .NET SDK component proof; no Azure resource or azd deployment");
    var sdk = await Execute("sdk", "--version");
    if (sdk.ExitCode != 0) throw new Exception("SDK probe failed.");
    selectedSdk = sdk.Stdout.Trim();
    var outputs = new[] { Path.Combine(evidence, "publish-a"), Path.Combine(evidence, "publish-b") };
    var artifactRoots = new[] { Path.Combine(evidence, "artifacts-a"), Path.Combine(evidence, "artifacts-b") };
    var commands = new List<string[]>();
    for (var i = 0; i < 2; i++)
    {
        var service = i == 0 ? "ServiceA" : "ServiceB";
        var flavor = i == 0 ? "A" : "B";
        var command = new List<string> { "publish", Path.Combine(root, "src", service, service + ".csproj"), "--configuration", "Release", "--output", outputs[i], "--self-contained", "false", "-p:DncFlavor=" + flavor, "--verbosity", "minimal", "--nologo" };
        if (mode == "isolated") command.AddRange(["--artifacts-path", artifactRoots[i]]);
        commands.Add(command.ToArray());
    }
    // Start both processes before awaiting either: this is actual concurrent publishing.
    var builds = await Task.WhenAll(Execute("publish-a", commands[0]), Execute("publish-b", commands[1]));
    var anyBad = builds.Any(x => x.ExitCode != 0);
    for (var i = 0; i < 2 && !anyBad; i++)
    {
        var service = i == 0 ? "ServiceA" : "ServiceB";
        var run = await Execute("run-" + service, Path.Combine(outputs[i], service + ".dll"));
        if (run.ExitCode != 0 || !run.Stdout.Contains("APP PASS:")) anyBad = true;
    }
    if (mode == "shared")
    {
        outcome = anyBad ? "SHARED_FAILURE_OBSERVED_NEEDS_LOG_REVIEW" : "NO_RACE_OBSERVED";
        Console.WriteLine("DIAGNOSTIC: " + outcome);
        Console.WriteLine("A shared-mode failure is not automatically proof of a race; inspect its logs.");
        Console.WriteLine("A passing shared run does not establish safety; scheduling is nondeterministic.");
    }
    else
    {
        if (anyBad) throw new Exception("Isolated publishing or runtime marker validation failed.");
        for (var i = 0; i < 2; i++)
        {
            if (!Directory.Exists(Path.Combine(artifactRoots[i], "obj", "Shared"))) throw new Exception("Missing service-specific Shared intermediate output.");
        }
        var hashA = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(outputs[0], "Shared.dll"))));
        var hashB = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(outputs[1], "Shared.dll"))));
        if (hashA == hashB) throw new Exception("Shared assembly hashes must differ because their embedded flavors differ.");
        checks.Add(new { name = "independent_shared_assembly_hashes", status = "PASS", sha256_a = hashA, sha256_b = hashB });
        checks.Add(new { name = "service_specific_intermediate_roots", status = "PASS", paths = artifactRoots });
        outcome = "SDK_COMPONENT_PASS";
        Console.WriteLine("PASS: both parallel publishes use separate intermediate roots");
        Console.WriteLine("PASS: service A and B execute with their own embedded shared-library markers");
        Console.WriteLine("PASS: shared assembly bytes differ as expected");
        Console.WriteLine("PASS: SDK artifact isolation proof completed");
        Console.WriteLine("AZD END-TO-END: NOT_EXECUTED (do not promote this component result to an azd deployment result)");
    }
}
catch (Exception ex)
{
    failed = true;
    Console.Error.WriteLine("FAIL: " + ex.Message);
}
finally
{
    var receipt = new { demo = "DNC-AZD-Parallel-Proof", candidate_id = "DNC-MANUAL-20261002-1016-C01", mode, sdk = selectedSdk, runner_runtime = Environment.Version.ToString(), started_at = started, finished_at = DateTimeOffset.UtcNow, outcome, azd_end_to_end = "NOT_EXECUTED", checks };
    await File.WriteAllTextAsync(Path.Combine(evidence, "receipt.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine("EVIDENCE DIRECTORY: " + evidence);
}
return failed ? 1 : 0;
