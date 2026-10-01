using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;

if (args.Length != 2) throw new ArgumentException("HostProbe <before|after> <output.json>");
var variant = args[0];
if (variant is not ("before" or "after")) throw new ArgumentException("Unknown variant");
// The real Functions Host configures its own allowlist. This probe only adds a
// memory exporter; it never adds the Service Bus sources to the provider.
var assembly = typeof(ScriptHost).Assembly;
var configureType = assembly.GetType("Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry.OpenTelemetryConfigurationExtensions", true)!;
var modeType = assembly.GetType("Microsoft.Azure.WebJobs.Script.Diagnostics.OpenTelemetry.TelemetryMode", true)!;
var configure = configureType.GetMethod("ConfigureOpenTelemetry", BindingFlags.Static | BindingFlags.NonPublic)!;
var exporter = new MemoryExporter();
using var host = new HostBuilder()
    .ConfigureLogging((context, builder) => configure.Invoke(null, [builder, context, Enum.Parse(modeType, "Placeholder")]))
    .ConfigureServices(services =>
    {
        services.AddSingleton<IEnvironment>(SystemEnvironment.Instance);
        services.AddOpenTelemetry().WithTracing(b => b.AddProcessor(new SimpleActivityExportProcessor(exporter)));
    }).Build();
var provider = host.Services.GetRequiredService<TracerProvider>();
var checks = new List<object>();
var cases = new List<object>();
void Check(string name, bool pass) => checks.Add(new { name, pass });
Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;
const string sessionSource = "Azure.Messaging.ServiceBus.ServiceBusSessionProcessor";
const string regularSource = "Azure.Messaging.ServiceBus.ServiceBusProcessor";
Check("Actual Microsoft.Azure.WebJobs.Script assembly loaded", assembly.GetName().Name == "Microsoft.Azure.WebJobs.Script");
foreach (var sourceName in new[] { regularSource, sessionSource, "DNC.UnknownSource" })
{
    using var source = new ActivitySource(sourceName);
    for (var index = 0; index < 3; index++)
    {
        var parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded, isRemote: true);
        var expected = sourceName == regularSource || sourceName == sessionSource && variant == "after";
        var count = exporter.Spans.Count;
        string? traceId = null, parentSpanId = null;
        using (var activity = source.StartActivity(sourceName == sessionSource ? "ServiceBusSessionProcessor.ProcessSessionMessage" : "Control.ProcessMessage", ActivityKind.Consumer, parent))
        {
            Check($"{sourceName}[{index}] listener matches release", (activity != null) == expected);
            if (activity != null)
            {
                traceId = activity.TraceId.ToHexString();
                parentSpanId = activity.ParentSpanId.ToHexString();
                Check($"{sourceName}[{index}] W3C trace ID preserved", traceId == parent.TraceId.ToHexString());
                Check($"{sourceName}[{index}] remote parent span preserved", parentSpanId == parent.SpanId.ToHexString());
                Check($"{sourceName}[{index}] consumer span sampled", activity.Recorded && activity.Kind == ActivityKind.Consumer);
            }
        }
        provider.ForceFlush();
        Check($"{sourceName}[{index}] actual OTel export count", exporter.Spans.Count - count == (expected ? 1 : 0));
        cases.Add(new { source = sourceName, index, expectedListener = expected, expectedTraceId = parent.TraceId.ToHexString(), expectedParentSpanId = parent.SpanId.ToHexString(), traceId, parentSpanId });
    }
}
var result = new
{
    schemaVersion = 1, variant, scope = "ACTUAL_HOST_CONFIGURATION_COMPONENT_ONLY",
    messageTransport = "NOT_RUN_NO_BROKER", hostMode = "Placeholder (no external exporters)",
    hostAssemblyVersion = assembly.GetName().Version?.ToString(),
    timestampUtc = DateTimeOffset.UtcNow, pass = checks.All(c => (bool)c.GetType().GetProperty("pass")!.GetValue(c)!),
    checks, cases, spans = exporter.Spans
};
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
File.WriteAllText(args[1], JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"{variant}: {checks.Count} checks; {exporter.Spans.Count} spans; {result.pass}");
return result.pass ? 0 : 1;

sealed class MemoryExporter : BaseExporter<Activity>
{
    public List<object> Spans { get; } = [];
    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var a in batch) Spans.Add(new { source = a.Source.Name, name = a.DisplayName, traceId = a.TraceId.ToHexString(), spanId = a.SpanId.ToHexString(), parentSpanId = a.ParentSpanId.ToHexString(), kind = a.Kind.ToString() });
        return ExportResult.Success;
    }
}
