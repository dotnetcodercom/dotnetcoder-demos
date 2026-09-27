using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();
var baseUri = new Uri($"http://127.0.0.1:{port}/");

using var build = Process.Start(new ProcessStartInfo("dotnet")
{
    WorkingDirectory = Directory.GetCurrentDirectory(),
    ArgumentList = { "build", "MvcToastProof.csproj", "--nologo" },
    UseShellExecute = false
}) ?? throw new Exception("Could not build the MVC application.");
await build.WaitForExitAsync();
if (build.ExitCode != 0) throw new Exception($"MVC build failed: {build.ExitCode}");

using var server = Process.Start(new ProcessStartInfo("dotnet")
{
    WorkingDirectory = Directory.GetCurrentDirectory(),
    ArgumentList =
    {
        "bin/Debug/net10.0/MvcToastProof.dll", "--urls", baseUri.ToString().TrimEnd('/')
    },
    UseShellExecute = false
}) ?? throw new Exception("Could not start the MVC application.");

try
{
    using var probe = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
    var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
    while (true)
    {
        if (server.HasExited) throw new Exception($"MVC application exited: {server.ExitCode}");
        try
        {
            using var ready = await probe.GetAsync(baseUri);
            if (ready.IsSuccessStatusCode) break;
        }
        catch (HttpRequestException) { }
        catch (TaskCanceledException) { }
        if (DateTimeOffset.UtcNow >= deadline) throw new Exception("MVC application did not become ready.");
        await Task.Delay(250);
    }

using var handler = new HttpClientHandler
{
    AllowAutoRedirect = false,
    CookieContainer = new CookieContainer()
};
using var client = new HttpClient(handler) { BaseAddress = baseUri };

var initial = await client.GetStringAsync("/");
Check(!initial.Contains("data-proof-toast", StringComparison.Ordinal), "Initial GET has no toast");

var input = Regex.Matches(initial, "<input[^>]*>", RegexOptions.IgnoreCase)
    .Select(m => m.Value)
    .FirstOrDefault(tag => tag.Contains("name=\"__RequestVerificationToken\"", StringComparison.Ordinal));
Check(input is not null, "Antiforgery input exists");
var tokenMatch = Regex.Match(input!, "value=\"([^\"]+)\"");
Check(tokenMatch.Success, "Antiforgery input has a value");
var token = WebUtility.HtmlDecode(tokenMatch.Groups[1].Value);

const string name = "<script>alert(1)</script>";
using var content = new FormUrlEncodedContent(new Dictionary<string, string>
{
    ["name"] = name,
    ["__RequestVerificationToken"] = token
});
using var response = await client.PostAsync("/Home/Save", content);
Check(response.StatusCode == HttpStatusCode.Redirect, "POST returns 302");
Check(response.Headers.Location is not null, "Redirect has a Location");
var redirectedUri = new Uri(baseUri, response.Headers.Location!);

var afterRedirect = await client.GetStringAsync(redirectedUri);
Check(Count(afterRedirect, "data-proof-toast") == 1, "Redirected GET has one toast");
Check(afterRedirect.Contains("&lt;script&gt;", StringComparison.Ordinal), "Untrusted name is HTML encoded");
Check(!afterRedirect.Contains(name, StringComparison.Ordinal), "No raw script in output");

var afterRefresh = await client.GetStringAsync(redirectedUri);
Check(!afterRefresh.Contains("data-proof-toast", StringComparison.Ordinal), "Refresh has no toast");

Console.WriteLine("PASS: initial GET, antiforgery, POST 302, one redirected toast, encoded input, no toast after refresh");
}
finally
{
    if (!server.HasExited) server.Kill(entireProcessTree: true);
    await server.WaitForExitAsync();
}

static int Count(string source, string value) =>
    source.Split(value, StringSplitOptions.None).Length - 1;

static void Check(bool condition, string description)
{
    if (!condition) throw new Exception($"FAIL: {description}");
    Console.WriteLine($"PASS: {description}");
}
