# Recorded measurements

The string and lookup reports were recorded before extraction, using the same parser and benchmark source. The request report was recorded directly from this standalone repository. They are local diagnostics from Apple M2 / macOS 26.5.2, .NET SDK 10.0.103, .NET 10.0.3, and BenchmarkDotNet 0.15.8.

## Current request comparison

The [60-case request report](request-benchmark.md) and [CSV](request-benchmark.csv) use the latest parser and the allocation-free property-name comparisons in the JsonElement baseline. The main README summarizes both timing and allocations for First, Last, and Missing with 16 KiB padding and 64- or 4096-byte chunks. The full report also includes small payloads.

Early extraction and whole-document validation perform different work when the value comes first. A DTO uses a different general search contract. Adapter setup and cleanup are included; real HTTP and middleware replay costs are excluded.

## Matched-string decoding

The [24-case decoding report](string-decoding-benchmark.md) and [CSV](string-decoding-benchmark.csv) compare `GetString`, `CopyString` into characters, and `CopyString` into UTF-8 bytes. The [actual-parser allocation probe](string-decoding-probe.txt) verifies the production path after warmup:

| Decoded characters | Segmented | Escaped | GetString allocation | Current parser allocation |
|---:|---|---|---:|---:|
| 16 | No | Either | 56 B | 56 B |
| 16 | Yes | No | 96 B | 56 B |
| 16 | Yes | Yes | 176 B | 56 B |
| 16384 | No | Either | 32792 B | 32792 B |
| 16384 | Yes | No | 49200 B | 32792 B |
| 16384 | Yes | Yes | 131120 B | 32792 B |

The escaped fixture encodes every character as `\u0078`, so 16384 decoded characters occupy 98304 encoded value bytes. Segments are at most 4096 bytes. This stresses a particular decoding cost rather than representing a typical request distribution.

For large segmented values, byte-buffer decoding measured comparable or lower time: 3.181 to 3.142 µs for plain text and 178.497 to 164.893 µs for escaped text. Tiny segmented values added about 6–8 ns. Contiguous values already allocate only their result string, and the large plain contiguous byte-buffer candidate was slower; production retains `GetString` there.

The character-buffer candidate was faster for the large plain split value but needs more scratch capacity. The byte-buffer implementation favors lower scratch memory. The output string still allocates, and warm allocation totals do not measure retained pool capacity, cold starts, or concurrency. BenchmarkDotNet's large escaped allocation totals differ slightly from the deterministic warmed probe.

## Property-name lookup

The [16-case lookup report](lookup-benchmark.md) and [CSV](lookup-benchmark.csv) compare names in prebuilt `JsonElement` documents and explore reader constructors. Looking through 64 names with `NameEquals` removes 2560 B of name-string allocation and roughly halves lookup time. Deserialization is excluded, so this is not a claim about whole-request speed.

A span constructor helped the smallest missing fixture by about 8 ns, but other constructor comparisons were effectively equal. The production parser keeps the sequence constructor.

## Existing allocation improvements

The [end-to-end allocation check](low-hanging-allocation-check.txt) compares the frozen task-based batched implementation with the current parser. It includes stream/reader setup and cleanup. The current small matching fixture allocates 408 B versus 592 B; the small missing fixture allocates 368 B versus 408 B.

The ready parser itself allocates only its result string, or 0 B for a missing match. A warmed borrowed reader with one pending read also avoids parser state-machine allocation. These isolated totals exclude request, reader, and transport costs supplied by a real server.

## Reproduce

```console
dotnet run --project benchmarks/BodyReaderJson.Benchmarks -c Release -f net10.0 -- --strings
dotnet run --project benchmarks/BodyReaderJson.Benchmarks -c Release -f net10.0 -- --allocations
dotnet run --project benchmarks/BodyReaderJson.Benchmarks -c Release -f net10.0 -- --filter '*StringDecodingBenchmarks*' '*LookupBenchmarks*' --job Short
```

All 100 checked-in benchmark cases completed with `InProcessEmitToolchain`, one launch, three warmup iterations, and three measured iterations. Some error ranges are wide. The reports are summaries, rather than raw iteration samples. Use an isolated run on the deployment environment for publication measurements.

The recorded local commands used `-p:UseAppHost=false -p:NuGetAudit=false` before `--`, and `--inProcess` for benchmarks. These worked around an app-host signing error and an unavailable NuGet audit endpoint with already-cached dependencies. They are command-line workarounds, not project settings.
