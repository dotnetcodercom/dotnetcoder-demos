using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);
new HostBuilder().ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(s => s.AddOpenTelemetry().UseFunctionsWorkerDefaults()
        .WithTracing(b => b.AddSource("DNC.Consumer"))
        .UseOtlpExporter())
    .Build().Run();
