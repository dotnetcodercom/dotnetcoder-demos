# .NET 11 ZIP imports: validate before committing

This example stages every ZIP record, reads entries to EOF and saves one local JSON file only after the whole batch succeeds. A separate proof demonstrates why catching a checksum exception around per-entry writes can leave an earlier record saved.

Prerequisites: Python 3 and .NET SDK **11.0.100-rc.1.26425.128**, pinned in `global.json`. Tested runtime: **11.0.0-rc.1.26425.128**, on Ubuntu. .NET 11 is prerelease. These projects have no third-party package, cloud resource, credential or model requirement.

## Run the exact article example

From this demo directory:

```sh
cd article-example
dotnet build --configuration Release --disable-build-servers
python make-fixtures.py
dotnet run -- corrupt-last.zip records.json
```

The deliberately corrupt import exits **1** with `InvalidDataException`; `records.json` must still contain only `{"seed":"keep"}`. Confirm the saved state, then run the valid import:

```sh
python -c "import json; from pathlib import Path; assert json.loads(Path('records.json').read_text()) == {'seed':'keep'}"
dotnet run -- valid.zip records.json
```

The valid import prints `Committed` and saves `seed`, `first` and `second`. Reset fixtures before repeating: the importer rejects existing keys rather than silently overwriting them.

```sh
python verify-example.py
```

Expected: **PASS, 11 checks**. The verifier runs the Release DLL and checks valid input, early/late corruption, malformed records, an existing key, invalid UTF-8, entry/aggregate/count limits, an empty archive and an empty directory entry. Failure cases assert the saved bytes are unchanged and no pending file remains. Set `DOTNET_EXE` if the pinned executable is not on PATH.

## Run the independent commit-boundary proof

```sh
cd ../zip-commit-proof
dotnet run --project Proof.csproj --configuration Release --disable-build-servers
```

The printed JSON and newly written `receipt.json` contain seven passing conditions. The valid batch commits both records; early corruption, late corruption, a malformed last record, cancellation between entries and an injected I/O fault before replacement preserve the seed. The deliberately unsafe per-entry-save control is expected to leave the first record saved after late corruption. It is a detector control, not code to use in an importer.

This companion program differs from the exact article example: it supplies cancellation and fault-injection controls and starts each case from its own seed. The article console has neither control option.

## Check the ZIP read boundary

```sh
cd ../zip-proof
dotnet run --project Proof.csproj --configuration Release --disable-build-servers
```

Six printed observations compare valid/corrupt stored entries with full read, first byte and open-only modes. Corrupt full read throws `InvalidDataException` after 18 bytes have already been returned; its open-only and first-byte modes succeed. Opening a stream or consuming a prefix therefore does not establish full-entry validity.

## Evidence and limits

`evidence/` contains recorded observations from the verified local executions. Rerunning the code creates new work files and receipts; it does not silently replace these curated evidence files. Both corrupt archive sets are intentional tiny data fixtures, read without extraction.

The exact article example rejects more than 16 entries, a declared record length over 64 KiB and aggregate declared entry data over 256 KiB. Those are demonstration limits, not a complete resource policy for an untrusted upload service. The synchronous stored-ZIP, single-file result does not prove database transactions, HTTP integration, encrypted/compressed archive coverage, concurrent-writer safety, prompt cancellation during `CopyTo`, filesystem crash durability or Windows behavior. Unique temporary names do not prevent lost updates between writers. CRC32 detects accidental corruption; it does not authenticate an archive.

Verification creates/reset only local `work/` and `verification-work/` data, plus generated fixture/state files in `article-example/`. The `.gitignore` excludes runtime output. Keep the intentional fixture ZIP files in `zip-proof/` and `zip-commit-proof/`; they are needed to reproduce the comparison.

Primary sources:

- https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/11/ziparchive-entry-crc32-validation
- https://learn.microsoft.com/en-us/dotnet/standard/io/zip-tar-best-practices

Sample source is MIT licensed; framework and runtime dependencies retain their own licenses.
