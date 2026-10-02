# .NET 11 ReadOnlySequenceStream lifetime proof

This console demo uses the actual framework adapter with a segmented PipeReader payload. It checks borrowed-memory lifetime, deterministic negative controls, an owning copy, disposal, and pre-cancellation. No server or browser UI is started.

## Prerequisites

- .NET SDK 11.0.100-rc.1.26425.128 and its .NET 11 framework/reference packs. global.json allows latestPatch within the pinned feature band; another preview or final SDK needs separate revalidation.
- Windows PowerShell 5.1 or PowerShell 7 for the wrapper. A direct dotnet command is also available below.
- FrameworkReference Microsoft.AspNetCore.App supplies System.IO.Pipelines; no web hosting is involved. NuGet.Config clears package feeds, so packs must already be installed.

## Run

Open PowerShell in this folder:

```powershell
Get-ChildItem -Recurse -Filter *.ps1 | Unblock-File
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-SequenceStream.ps1
```

Direct console execution:

```text
dotnet run --project Proof/Proof.csproj --configuration Release -- evidence/manual
```

Success includes PASS: ReadOnlySequenceStream proof completed. EXPECTED UNSAFE RESULT belongs to an intentional negative control; the final outcome must still be PASS. Evidence is saved under evidence/.

## Recorded verification

Executed on Windows on October 2, 2026 with SDK 11.0.100-rc.1.26425.128 and runtime 11.0.0. Restore, build, and execution succeeded; the receipt contains 20 PASS assertions. This uses the actual framework API, not the separate .NET 10 authoring compatibility harness. The executable source here is byte-for-byte unchanged from the tested download.

The custom pool poisons returned memory with 0xDD only as a controlled diagnostic. Production symptoms can differ. Await consumption before AdvanceTo/completion, or copy into independent storage. No throughput, allocation improvement, or end-to-end zero-copy HTTP benchmark is claimed.

## Cleanup

After preserving wanted evidence, delete the extracted folder. No credentials, Azure resources, persistent server, or public deployment is created by this demo.

## Article

Planned article URL (publication pending): https://dotnetcoder.com/dotnet-11-readonlysequencestream-pipe-lifetime/

## Primary sources

- https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/libraries
- https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines
- https://github.com/dotnet/runtime/blob/v11.0.0-rc.1.26425.128/src/libraries/System.Memory/src/System/Buffers/ReadOnlySequenceStream.cs
