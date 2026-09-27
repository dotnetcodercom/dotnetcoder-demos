using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Diagnostics;

// Run twice: dotnet run, then dotnet run -- --retain.
// No exporter, Azure resource, credential, or external NuGet package is needed.
var retain = args.Contains("--retain", StringComparer.Ordinal);
var measurements = new List<(string? Status, string? ErrorType)>();
var sync = new object();

using var listener = new MeterListener
{
    InstrumentPublished = (instrument, meterListener) =>
    {
        if (instrument.Meter.Name == "Microsoft.AspNetCore.Hosting" &&
            instrument.Name == "http.server.request.duration")
        {
            meterListener.EnableMeasurementEvents(instrument);
        }
    }
};
listener.SetMeasurementEventCallback<double>((_, _, tags, _) =>
{
    string? status = null;
    string? errorType = null;
    foreach (var tag in tags)
    {
        if (tag.Key == "http.response.status_code") status = tag.Value?.ToString();
        if (tag.Key == "error.type") errorType = tag.Value?.ToString();
    }
    lock (sync) measurements.Add((status, errorType));
});
listener.Start();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddExceptionHandler<HandledExceptionHandler>();
builder.Services.AddProblemDetails();
var app = builder.Build();
if (retain)
{
    app.UseExceptionHandler(new ExceptionHandlerOptions
    {
        SuppressDiagnosticsCallback = _ => false
    });
}
else
{
    // Leave the callback unset to exercise ASP.NET Core 10's default behavior.
    app.UseExceptionHandler();
}
app.MapGet("/boom", () => { throw new InvalidOperationException("proof"); });

try
{
    await app.StartAsync();
    var url = app.Urls.Single();
    using var client = new HttpClient();
    using var response = await client.GetAsync($"{url}/boom");

    // The measurement callback can arrive just after the response completes.
    for (var i = 0; i < 100; i++)
    {
        lock (sync)
        {
            if (measurements.Count > 0) break;
        }
        await Task.Delay(20);
    }

    (string? Status, string? ErrorType)[] captured;
    lock (sync) captured = measurements.ToArray();
    var expectedError = retain ? typeof(InvalidOperationException).FullName : null;
    var pass = (int)response.StatusCode == 500 &&
               captured.Length == 1 &&
               captured[0].Status == "500" &&
               captured[0].ErrorType == expectedError;

    Console.WriteLine($"mode={(retain ? "retain" : "default")}");
    Console.WriteLine($"response={(int)response.StatusCode}");
    Console.WriteLine($"measurements={captured.Length}");
    Console.WriteLine($"error.type={(captured.Length == 1 ? captured[0].ErrorType ?? "<none>" : "<unexpected count>")}");
    Console.WriteLine(pass ? "PROOF PASS" : "PROOF FAIL");
    if (!pass) Environment.ExitCode = 1;
}
finally
{
    await app.StopAsync();
}

sealed class HandledExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsync("handled", cancellationToken);
        return true;
    }
}
