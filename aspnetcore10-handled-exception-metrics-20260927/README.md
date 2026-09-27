# ASP.NET Core 10 handled exception metric proof

This tiny .NET 10 application calls an endpoint that throws, handles the exception with `IExceptionHandler`, and observes the built-in `http.server.request.duration` metric with `MeterListener`. It compares the `error.type` tag with the default diagnostic suppression and with diagnostics retained.

Article: https://dotnetcoder.com/aspnetcore10-handled-exception-metrics/

## Prerequisites

Install a .NET 10 SDK. No Azure resource, credential, or external package is needed.

## Run

In this directory, run:

```powershell
dotnet run --project .\DncExceptionMetricsProof.csproj
dotnet run --project .\DncExceptionMetricsProof.csproj -- --retain
```

Both commands should print `response=500`, `measurements=1`, and `PROOF PASS`. In the first, `error.type=<none>`; in the second, `error.type=System.InvalidOperationException`. A nonzero exit code means the assertion failed. The app binds to an available loopback port and stops automatically. No cloud account is needed.

The proof shows this one handled-exception metric path only. It does not test an OpenTelemetry exporter, Application Insights, other exception handlers, or a production telemetry pipeline. Do not infer that HTTP 500 responses disappear: the response code remains 500 in both modes.

## References

- [Microsoft: Exception handler diagnostics suppressed by default in ASP.NET Core 10](https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/10/exception-handler-diagnostics-suppressed?view=aspnetcore-10.0)
- [Microsoft: ASP.NET Core metrics](https://learn.microsoft.com/en-us/aspnet/core/metrics/overview?view=aspnetcore-10.0)
