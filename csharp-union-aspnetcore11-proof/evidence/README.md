# Verification evidence

These files come from an actual agent-side Linux HTTP/OpenAPI run using .NET 11 RC1. They are not Windows laptop evidence or screenshots.

- `local-verification.txt`: console output and assertion results.
- `local-result.json`: runtime, OS, timestamp, assertion count, and scope.
- `local-openapi.json`: the full document returned by the running API.
- `local-http-cases.txt`: actual HTTP inputs, statuses, and response bodies.

## Windows laptop evidence

Seven user screenshots were inspected after the Linux run. They confirm the pinned SDK, successful restore/build, 23 passing HTTP/OpenAPI assertions, successful PowerShell completion, and a working browser UI. Dog and Cat return HTTP 200; the mixed object returns HTTP 400. Full original screenshots are preserved in `laptop-screenshots/`, with provenance and limits in `laptop-verification.json`.

This Windows verification is based on screenshots, not an agent-run Windows session. Raw Windows `artifacts` files were not supplied. No screenshot text or results were edited. The Linux evidence above remains separately labeled.

Selected article evidence: `03-verification-complete.png`, `05-dog-http-200.png`, and `06-ambiguous-http-400.png`. Additional screenshots support SDK/build details, numeric-string behavior, and the Cat response.
