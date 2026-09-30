# Native C# union + ASP.NET Core 11 proof

A small, single-project Minimal API for an existing discriminator-free JSON contract. It uses native `public union Pet(Dog, Cat)`, preview C#, System.Text.Json's structural classifier, and the built-in OpenAPI generator. No database or cloud account.

Verification passed 23 checks locally on Ubuntu and on the user's Windows laptop with the pinned .NET 11 RC1 SDK. The Windows result is confirmed by seven inspected user screenshots, including `ALL CHECKS PASSED (23)` and `VERIFICATION COMPLETE`. Browser screenshots show Dog and Cat returning HTTP 200, the mixed object returning HTTP 400, and the generated OpenAPI `anyOf` alongside each exchange. See `evidence/laptop-verification.json` and `evidence/laptop-screenshots/`. The raw laptop artifact files were not supplied; `evidence/local-*` remains the separate Linux run.

## Reproduce

Prerequisite: .NET SDK `11.0.100-rc.1.26425.128` (or a later .NET 11 SDK selected by `global.json`). OpenAPI is pinned to `11.0.0-rc.1.26425.128`. Preview behavior may change.

Extract the ZIP, open PowerShell in the folder containing the project and script, then:

```powershell
Unblock-File .\Verify-Union.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-Union.ps1
```

Portable verification from that same folder:

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet bin/Release/net11.0/UnionProof.dll --verify
```

The verifier starts Kestrel on an ephemeral loopback port, calls actual HTTP endpoints, and stops the app. A failed run exits nonzero. A successful run writes `artifacts/result.json`, full OpenAPI, and HTTP exchanges. The PowerShell wrapper also captures `artifacts/verification.txt`.

Run the browser demo after building:

```powershell
dotnet run --project .\UnionProof.csproj -c Release --no-build --no-launch-profile -- --urls http://127.0.0.1:5111
```

Open http://127.0.0.1:5111 and use the three buttons. Each sends a real HTTP request. Stop with Ctrl+C.

## Contract and scope

| Case | JSON body | Selected case |
| --- | --- | --- |
| Dog | `{"name":"Rex","breed":"Husky"}` | Dog |
| Cat | `{"name":"Milo","lives":9}` | Cat |
| Cat with numeric string | `{"name":"Milo","lives":"9"}` | Cat; response normalizes lives to `9` |
| Ambiguous | `{"name":"Shared","breed":"Husky","lives":9}` | HTTP 400 |

`GET /pets/dog` and `/pets/cat` return the active case directly. `POST /pets/echo` uses `[FromBody] Pet` and an exhaustive case switch to expose the actual selected C# case in `X-Union-Case`. All case properties have `[JsonRequired]`. No union envelope or additional `$type` is written.

The verifier checks both cases, invalid and ambiguous bodies, and generated request/response `anyOf` references plus required properties. **OpenAPI `anyOf` is not the structural classifier:** a mixed object can satisfy both alternatives while runtime classification rejects a tie. The test proves this limitation, not full schema/runtime equivalence. It does not test generated clients, performance, MVC, SignalR, query binding, or production suitability.

ASP.NET Core's web JSON defaults allow reading numbers from strings. Consequently the generated `lives` property admits `integer` or `string`, while the response writes a number. The verifier checks the numeric-string input and this generated schema explicitly; request and response schemas are not claimed to be equally narrow.

For new contracts where you control all case types, Microsoft's guidance favors a closed hierarchy with a discriminator. This example is appropriate for learning how native unions preserve an established discriminator-free shape.

## Official references

- [C# union tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/tutorials/unions)
- [ASP.NET Core 11 release notes](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-11?view=aspnetcore-11.0)
- [Use C# unions and closed hierarchies in ASP.NET Core](https://devblogs.microsoft.com/dotnet/unions-and-closed-hierarchies-in-aspnetcore/)

See `README-AR.md` for Arabic instructions and the screenshots to capture after a successful laptop run.
