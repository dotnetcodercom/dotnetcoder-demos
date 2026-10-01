# Azure Blob SDK 12.30 local proof

Run `powershell -NoProfile -ExecutionPolicy Bypass -File .\Verify-Blob.ps1` on Windows with Node.js >=22 and .NET SDK >=10 (including SDK 11 preview). On Linux/macOS use `bash Verify-Blob.sh`.

Pinned comparison: Azure.Storage.Blobs 12.29.2 vs 12.30.0; Azurite 3.37.0. No Azure credentials or cloud resources. Local-only endpoint: 127.0.0.1:10080; both clients explicitly request service API 2023-11-03 so the SDK comparison does not depend on support for the newest service APIs. No skipApiVersionCheck switch is used.

Reports are in artifacts/latest/report.html; immutable per-run receipts are in the timestamped artifacts folder. A FAIL is a FAIL: do not publish an unverified claim or use stale receipts.

Tests observe real partitioned upload block IDs, SHA256 preservation, persisted custom resume order, changed-source rejection, conditional ETag commits, 404, pre-canceled download, and six fresh-process DownloadToAsync profiles against a 64 MiB blob. Memory observations are local process measurements, not SDK-exclusive allocation counts or production memory caps. C × range size is a range-window estimate only. No performance ordering is a pass criterion.

The manifest example is deliberately small: equal-sized blocks, in-memory source, same-process reload of persisted state, and trusted local manifest. It is not a full resumable-upload product. The ETag test stages two writers and serializes conditional commits; it is not a concurrency load test. New random block IDs do not provide write coordination.

Azurite is started with its official --disableTelemetry switch; this prevents Application Insights initialization and unnecessary metadata probes. .NET CLI telemetry is disabled during builds. No WordPress write, publication, or scheduling happens in this package.

Verified Windows run: 84 checks and two cross-version gates passed. Original receipts are in evidence/windows and the three selected report screenshots are in screenshots. The source-built Before and After projects are two SDK versions in this single demo.
