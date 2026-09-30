# NU1510 in .NET 11: a small verified reproduction

Reproduce a redundant Microsoft.Extensions.Logging.Abstractions reference, remove it for net11.0, and keep it for net10.0 in a two-target project.

Article: https://dotnetcoder.com/nu1510-in-dotnet-11-microsoft-extensions/

## Prerequisites

- Windows PowerShell and the .NET 11 SDK. The recorded Windows test selected **11.0.100-rc.1.26425.128**.
- NuGet access. The Before project uses Logging.Abstractions **11.0.0-rc.1.26425.128**; MultiTargetSafe uses **10.0.0** only for net10.0. These are reproduction versions, not a current security recommendation.
- Select SDK 11 in this directory; check any parent global.json if dotnet --version selects another SDK.

## Run

```powershell
Unblock-File .\Verify-NU1510.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-NU1510.ps1
```

The script records artifacts/verification.txt. Expected outcomes: Before emits NU1510; After restores, builds, and runs without that warning; MultiTargetSafe restores and builds net10.0 and net11.0 without it. The multi-target executable is built, not run by this script.

## Evidence and limits

evidence/windows-proof-excerpts.txt contains selected lines supplied by the developer after a successful Windows run on 2026-09-30. It is an excerpt, not a replacement for the original PowerShell transcript. Local paths have been omitted. evidence/proof.json binds these excerpts to the tested project/script hashes.

`node Verify-Saved-Proof.mjs` checks the recorded proof and source binding only. It performs **no .NET restore, build, or execution** and does not establish fresh runtime behavior. This audit is useful for the repository handoff; run the PowerShell command for a fresh reproduction.

The example exercises ILogger/NullLogger in a console app. It does not test all nine Microsoft.Extensions libraries, existing third-party binaries, self-contained deployment, Native AOT, or production workload behavior. Keep package dependencies needed by other target frameworks. Recompile and test actual dependent libraries before a production migration.
