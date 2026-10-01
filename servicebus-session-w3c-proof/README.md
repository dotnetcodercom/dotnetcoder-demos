# Azure Functions Service Bus session tracing proof

Compare the actual OpenTelemetry configuration of Functions Host v4.1054.200 and v4.1055.100. The fixed revision registers the Service Bus session processor ActivitySource. Ordinary processor and unknown-source controls separate this regression from exporter failure.

## Requirements and run

Node.js 22+, .NET 10 SDK and runtime (a compatible newer SDK can build the net10.0 projects), and PowerShell on Windows. Component mode does not need Docker. Integration mode needs Docker Desktop running Linux containers. No Azure account or cloud resources are used.

```powershell
Unblock-File .\Verify-ServiceBus.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-ServiceBus.ps1 -Mode Component
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-ServiceBus.ps1 -Mode Integration
```

Read the displayed emulator and SQL Server licenses. Enter uppercase `Y` only if you accept them; lowercase `y` stops the script. Use `-Mode All` to run both stages together. Open `artifacts/latest/report.html` afterward. Preserve each evidence folder when running modes separately because `latest` is replaced.

## Versions and evidence

Host source archives and SHA-256 values are pinned in `vendor/sources.json`; official license files are included. NuGet restore uses each extracted Host's official NuGet.config explicitly and locked dependencies. Integration uses source-built proof images, the Microsoft Service Bus emulator, SQL Server, Azurite and an OTLP collector. These are proof images, not Microsoft's released Host images.

The Windows component run passed 65 checks and four comparison gates. The independent Windows Docker run passed 61 checks: 12 messages sent and received, ordinary processor spans present in both revisions, and three session processor spans present only in the fixed revision with matching producer Trace IDs and parent Span IDs. Raw synthetic-message receipts and OTLP evidence are under `evidence/`; selected unmodified report screenshots are under `screenshots/`.

The observed receipts do not establish exactly-once delivery. This local emulator proof does not establish Azure rollout, production sampling, scaling or recovery behavior. Verify your deployed Host version and an exported session trace separately.

The runner removes its demo containers and network when finished. Downloading dependencies and Docker images requires network access and local disk space.

## Source

Fix: https://github.com/Azure/azure-functions-host/pull/11946

Release: https://github.com/Azure/azure-functions-host/releases/tag/v4.1055.100
