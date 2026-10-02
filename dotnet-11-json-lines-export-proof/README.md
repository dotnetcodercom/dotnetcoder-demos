# .NET 11 RC1 JSON Lines — executable export proof
Related article (planned canonical URL; the draft may not yet be public): https://dotnetcoder.com/dotnet-11-json-lines-exports/

## Prerequisites

Pinned .NET 11 RC1 SDK specified in `global.json`; a supported .NET host OS. No external service or credentials.


The project uses SDK `11.0.100-rc.1.26425.128` and runtime `11.0.0-rc.1.26425.128`, pinned in `global.json`. It has no external NuGet packages. The actual `JsonSerializer.SerializeAsyncEnumerable` call compiles and runs on this version.

## Verified result

All **nine checks passed** on Linux x64:

1. Four public SerializeAsyncEnumerable overloads exist on the actual runtime.
2. Stream JSONL writes compact records followed by LF, including the final record, despite WriteIndented=true and NewLine=CRLF; embedded newlines, quotes and Unicode round trip.
3. PipeWriter JSONL has the same framing; the caller completes the writer.
4. Default topLevelValues=false produces a JSON array.
5. An empty sequence emits zero JSONL bytes and [] in array mode.
6. Local HTTP delivers the first complete JSONL record while the producer is held at a gate, with a 16 KiB buffer.
7. The same gated delivery works with a 128-byte buffer.
8. Closing the unfinished HTTP response stream propagates RequestAborted to the waiting producer without releasing its gate.
9. A producer exception after the first delivered record leaves a valid prefix but causes a client transfer error; an initial HTTP 200 does not certify a complete export.

The test intentionally uses a controlled producer, not elapsed-time performance assertions. ReadLineAsync handles transport chunk boundaries; raw HTTP chunks must not be treated as record boundaries. The HTTP payload in the gated tests contains 200 text characters.

## Run

With the pinned SDK installed, open PowerShell in this directory:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify.ps1
```

Linux/macOS:

```bash
dotnet build -c Release --disable-build-servers -p:UseSharedCompilation=false
dotnet run -c Release --no-build
```

The program starts Kestrel at a temporary 127.0.0.1 port, runs its own HTTP client, then stops the server. It requires no external service, Docker, credentials or public deployment.

A nonzero exit code or FAIL receipt blocks article readiness. Results are written to evidence/receipt.json. Verification on Windows is optional; the packaged receipts were executed on Linux.

## Boundaries

This is a local HTTP/1.1 proof. It does not establish behavior through proxies, compression, CDNs, cloud hosting or every payload shape, and it is not a throughput benchmark or universal guarantee of one network flush per record. It tests options-based Stream and PipeWriter overloads; source-generated JsonTypeInfo overload behavior is not separately exercised. Cancellation evidence specifically covers closing an unfinished client response stream.

Rolling documentation and XML documentation files are not a substitute for compilation and runtime execution. The initial XML search missed this API; the actual runtime exposes it, and the direct calls pass.

## Primary sources

- [.NET 11 libraries: JSON Lines output](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-11/libraries#json-lines-output)
- [System.Text.Json serializer source](https://source.dot.net/System.Text.Json/System/Text/Json/Serialization/JsonSerializer.Write.Stream.cs.html) — rolling source, not an independent execution result.
