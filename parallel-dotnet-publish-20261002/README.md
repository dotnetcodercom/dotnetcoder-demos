# Parallel .NET publish artifact-isolation proof

Two console services reference Shared. The runner starts concurrent dotnet publish processes with independent --artifacts-path roots and final output folders, then executes both packages and verifies their embedded Shared-library A/B markers, different Shared.dll hashes, and intermediate roots. This is a local SDK component proof.

## Prerequisites

- A .NET 10 or newer SDK, installed .NET 10 reference packs, and a compatible runtime. Apps target net10.0 with runtime roll-forward enabled; global.json permits latestMajor SDK roll-forward and previews.
- Windows PowerShell 5.1 or PowerShell 7 for the wrapper; direct dotnet execution is available below.
- NuGet.Config clears package feeds; there are no external PackageReferences. Missing targeting packs need installation from Microsoft, not removal of assertions.

## Run

From this folder:

```powershell
Get-ChildItem -Recurse -Filter *.ps1 | Unblock-File
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-ParallelPublish.ps1
```

Direct execution:

```text
dotnet run --project Runner/Runner.csproj --configuration Release -- . isolated
```

Both services must print APP PASS with markers A and B, followed by PASS: SDK artifact isolation proof completed. The receipt must record SDK_COMPONENT_PASS. The wrapper console and runner receipt are in separate timestamped evidence folders; preserve both.

## Recorded verification

Executed on Windows on October 2, 2026 with SDK 11.0.100-rc.1.26425.128 and runner runtime 10.0.12. Five process exit checks returned zero; the two assembly-hash/intermediate-root checks PASS. The MSBuild server fell back successfully to an in-process build. Executable source is byte-for-byte unchanged from the tested download.

## Optional diagnostic

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-ParallelPublish.ps1 -Mode Shared
```

This unexecuted diagnostic omits per-service artifacts paths. NO_RACE_OBSERVED is a valid outcome; a shared run is not guaranteed to fail, and a failure needs log diagnosis.

## Boundaries and cleanup

AZD END-TO-END: NOT_EXECUTED. No Azure authentication, provisioning, deployment, or azd package result is proved. Optional azd scripts from the earlier authoring download are deliberately excluded from this SDK-scoped publication copy. The A/B settings are diagnostic markers, not recommended production library design. Inspect custom hard-coded generator paths separately; serialize conflicting builds when isolation cannot be verified.

Delete the folder after preserving evidence. The demo starts no web server, creates no cloud resources, and installs no tooling.

## Article

Planned article URL (publication pending): https://dotnetcoder.com/parallel-dotnet-publish-artifact-isolation/

## Primary sources

- https://learn.microsoft.com/en-us/dotnet/core/sdk/artifacts-output
- https://github.com/Azure/azure-dev/blob/azure-dev-cli_1.34.2/cli/azd/pkg/project/build_gate.go
- https://github.com/Azure/azure-dev/blob/azure-dev-cli_1.34.2/cli/azd/pkg/tools/dotnet/dotnet.go
