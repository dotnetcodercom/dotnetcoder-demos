# EF Core 11 JsonPathExists proof

Local SQL Server proof for missing JSON properties, explicit JSON null, nested paths and SQL NULL. See README-AR.md for Windows instructions and screenshot requirements.

## Verified laptop result — 30 September 2026

The user completed the Windows PowerShell script successfully against SQL Server 2025 LocalDB `17.0.1000.7`, instance `(localdb)\DNC2025`, with SDK `11.0.100-rc.1.26425.128`. Six EF assertions, raw SQL null comparisons and rollback passed. Evidence is inspected user screenshots, not an agent-side SQL run. See `evidence/laptop-verification.json` and three cropped screenshots in `evidence/screenshots/`. SQL Server 2022 was not independently executed.

## Prerequisites

.NET 11 SDK, Windows PowerShell, SQL Server 2022 or later, local login permissions and NuGet access. EF Core SqlServer is pinned to 11.0.0-rc.1.26425.128. These are reproduction versions, not production update advice.

## Run

Use the same local server name and authentication mode that work in SSMS:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-JsonPathExists.ps1 -Server '.\SQLEXPRESS'
```

For a SQL login, add `-SqlUser 'YOUR_SQL_LOGIN'`; the script prompts for a hidden password. Do not put credentials in command arguments, logs, screenshots or shared files. The app never prints its connection string.

Only a local temporary table in tempdb is used, within a transaction that rolls back. The same open connection is shared with EF; no database creation/deletion or persistent application-table writes occur. The sample forces the catalog to tempdb, disables connection pooling, and uses compatibility level 160 for EF query translation. The application queries the actual engine version before executing the proof. TrustServerCertificate is for this local development connection only.

## Verification scope

The live command runs six EF queries and asserts their exact result IDs; it also compares raw JSON_PATH_EXISTS and JSON_VALUE results. It uses text JSON (nvarchar(max)), not SQL Server 2025's native json type. It does not establish performance, indexing, Azure SQL or complex-type mapping behavior.

```powershell
dotnet run --project .\JsonPathProof.csproj --configuration Release -- --offline
```

Offline mode only validates SQL translation and explicitly does not connect to SQL Server. Live results must be verified separately. Output is recorded in artifacts/verification.txt by the Windows script.

## Sources

- https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-11.0/whatsnew
- https://learn.microsoft.com/en-us/sql/t-sql/functions/json-path-exists-transact-sql

## Saved-evidence audit

`node Verify-Saved-Proof.mjs` verifies hashes of the tested source and saved screenshot/verification records. It performs no restore, build, or SQL execution. Run the PowerShell script above for fresh engine-backed results.

Related article (expected canonical URL after publication): https://dotnetcoder.com/ef-core-11-jsonpathexists/
