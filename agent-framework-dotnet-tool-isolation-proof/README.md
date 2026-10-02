# Agent Framework .NET: test actual tool isolation

This reproduction compares **Microsoft.Agents.AI 1.21.0 and 1.22.0** on one shared function-invoking `IChatClient`. The fake client deliberately returns an unadvertised tool name, and harmless callback counters show whether the invocation pipeline executes it. The fixed version is a tested comparison point, not a claim about the latest release or complete authorization.

## Prerequisites

Python 3, .NET SDK **11.0.100-rc.1.26425.128** (`global.json`) and internet for the first NuGet restore. Tested runtime: **11.0.0-rc.1.26425.128**, Ubuntu. Both package runs resolved **Microsoft.Extensions.AI 10.10.0**. No live model, account, credential, cloud resource or consequential tool is used.

## Run

```sh
cd article-example
dotnet run -p:AgentVersion=1.21.0
dotnet run -p:AgentVersion=1.22.0
```

Both runs advertise only `public_read`. The old version reports `public=0; privileged=1`; the fixed version reports `public=0; privileged=0`. The assembly output must change from `1.21.0.0` to `1.22.0.0` so the comparison does not accidentally run the same package twice.

Add the normal-call and deliberately contaminated shared-tool controls:

```sh
dotnet run -p:AgentVersion=1.21.0 -- --normal
dotnet run -p:AgentVersion=1.22.0 -- --normal
dotnet run -p:AgentVersion=1.21.0 -- --inject-shared
dotnet run -p:AgentVersion=1.22.0 -- --inject-shared
python verify-example.py
```

The normal control invokes `public_read` once and never invokes `privileged_write` on either version. The injected control invokes the privileged callback once on both versions: it explicitly restores that tool to shared `AdditionalTools`. This intentionally unsafe mutation checks the detector; it must never enter application configuration.

Expected verifier result: six PASS conditions, with advertised names, actual counts, resolved `Microsoft.Extensions.AI` version and assembly checks. Set `DNC_DOTNET` if the pinned SDK executable is not on PATH. This minimal probe uses `RunAsync`; its streaming adapter only satisfies the client interface.

## Run the full independent matrix

```sh
cd ../agent-proof/1.21.0
dotnet run --project Proof.csproj --configuration Release --disable-build-servers
cd ../1.22.0
dotnet run --project Proof.csproj --configuration Release --disable-build-servers
```

Each version writes `receipt.json` with 24 conditions: three scenarios × streaming/non-streaming × sequential/overlapping requests × both construction orders. Every row invokes both agents. In overlapping cases a barrier holds the first requests until both agents have entered the fake client.

| Scenario | 1.21.0 | 1.22.0 |
| --- | --- | --- |
| Each agent requests its advertised tool | 8/8 isolated | 8/8 isolated |
| Each agent requests the other agent's tool | 8/8 cross-agent invocation detected | 8/8 attempts not invoked |
| Inject privileged tool into shared lookup | 8/8 detections | 8/8 detections |

An `isolated: false` result in the old cross-agent scenario or an injected control is expected evidence. It is not a build failure. Inspect `observations`, `sharedToolsAfterConstruction` and actual callback counts together; checking the outgoing schema alone misses the old registration problem. Reversing construction changes which shared tool remains available on the old package.

## Evidence and operational boundary

`evidence/` contains the saved 48-row matrix, six exact-example observations and resolved dependency hashes. The minimal receipt includes only relevant observed lines, excluding local build paths. New runs write their own runtime receipts beside each project, leaving the curated evidence intact.

This local fake-client comparison does not measure the frequency of unadvertised calls from a live model, prove a production exploit, or cover every provider, middleware configuration, hosted agent or later package. It verifies the invocation boundary in the tested construction. Application-owned shared registrations can still defeat scoping after an upgrade, as the injected control demonstrates.

For production, repeat harmless canary/normal/contaminated controls through the actual provider, streaming path and concurrent workload. Enforce user/resource authorization inside consequential operations, and handle approval independently. Separate wrappers are a mitigation to evaluate, not a topology proved by this matrix. If a rollback restores 1.21.0, the tested shared-wrapper construction remains unsafe for cross-agent calls.

Local runs create `bin/`, `obj/` and runtime receipts only. No paid service or external operation occurs. Never copy injected-control configuration into a deployed application.

Primary sources:

- https://github.com/microsoft/agent-framework/pull/8531
- https://github.com/microsoft/agent-framework/releases/tag/dotnet-1.22.0
- https://github.com/microsoft/agent-framework/blob/dotnet-1.22.0/dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientExtensions.cs

Sample source is MIT licensed; NuGet dependencies retain their own licenses.

## Article

Planned article URL (not published yet): https://dotnetcoder.com/agent-framework-tool-isolation-dotnet/
