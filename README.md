# JSON body reader

Incremental JSON request inspection using `PipeReader` and `Utf8JsonReader`, with correctness tests and BenchmarkDotNet comparisons against full-body reading and `JsonSerializer`.

This is the companion experiment for [HttpContext BodyReader and incremental JSON parser](https://bulatgrzegorz.github.io/httpcontext-bodyreader-and-incremental-json-parser/). It explores the performance and allocation costs of finding one property while preserving parser state, pipeline advancement, cancellation, and middleware body replay.

## Getting started

Install the **.NET 10 SDK**. The projects target .NET 9 and .NET 10; running both test targets also requires the corresponding ASP.NET Core runtimes. `global.json` selects the .NET 10 SDK family.

```console
git clone https://github.com/bulatgrzegorz/json-body-reader.git
cd json-body-reader
dotnet restore BodyReaderJson.slnx
dotnet run --project tests/BodyReaderJson.Tests -c Release -f net10.0 -- --output Minimal --report-trx
dotnet run --project tests/BodyReaderJson.Tests -c Release -f net9.0 -- --output Minimal --report-trx
```

Dependencies are pinned to TUnit 1.66.27 and BenchmarkDotNet 0.15.8. The parser project uses the ASP.NET Core shared framework.

## What it finds

The example searches for the **first string-valued property named `propertyNameToSearchFor`**, in document order at any depth. The property name is fixed in this experiment.

- Nested objects and arrays are searched.
- Matching names with non-string values are ignored, but their children are still searched.
- Duplicate names return the first string-valued match.
- Escaped names and values are decoded correctly.
- An empty string is a match; a missing match returns `null`.

By default, extraction returns as soon as the value is found, so it validates only the parsed prefix. Set `validateWholeDocument: true` to retain the first value while reading and validating the remaining JSON through EOF.

## Terminal endpoint

Reference `src/BodyReaderJson/BodyReaderJson.csproj` from an ASP.NET Core application. An endpoint that finishes processing the request can borrow the existing body reader:

```csharp
using BodyReaderJson;

app.MapPost("/inspect", async (HttpContext context) =>
{
    string? value = await JsonPropertyReader.FindAsync(
        context.Request.BodyReader,
        context.RequestAborted,
        validateWholeDocument: true);

    return Results.Ok(new { value });
});
```

The helper advances every returned read result in `finally`. It leaves completion to the reader's owner. Use the default early-extraction mode when obtaining the property is enough; use full validation when the entire request must be valid JSON.

The early extractor marks the buffer end examined. Its contract is terminal extraction, rather than a reusable one-message cursor for subsequent consumers of the same reader.

## Middleware with body replay

When a later component needs to read the body again, inspect a private reader over the buffered stream:

```csharp
using BodyReaderJson;

app.Use(async (context, next) =>
{
    string? value = await BufferedRequestInspection.FindAsync(context.Request);
    context.Items["routing-value"] = value;
    await next(context);
});
```

`BufferedRequestInspection` enables buffering before reading, creates its own reader with `leaveOpen: true`, completes that reader, and rewinds the body in nested `finally` blocks. Register this middleware before other body consumers. The helper uses early extraction; downstream processing is responsible for any required whole-document validation.

Rewinding `Request.Body` does not reset a shared `Request.BodyReader` that has already buffered bytes or observed EOF. Buffering also cannot recover data consumed before it was enabled. ASP.NET Core keeps ownership of the request stream.

## Parser and allocation choices

`JsonReaderState` preserves completed-token progress and grammar state. A separate flag remembers a matching property whose value has not arrived yet. Even an empty final buffer is parsed so that EOF can reject an unfinished object or array.

An unfinished token must be supplied again. Retrying after every tiny read can repeatedly scan a growing string prefix. The read loop instead asks `ReadAtLeastAsync` for approximately twice the retained suffix, and always retries at EOF. This reduces repeated work while trading off prompt detection between thresholds.

The default `maxPendingBytes` is 256 KiB. It limits an unfinished suffix **after parsing**, so it is not a strict token-length or HTTP request-size limit: a read can overshoot it and already-complete larger tokens can be accepted. Configure server request limits separately.

The parser and inspection helpers return `ValueTask<string?>` and use .NET's `PoolingAsyncValueTaskMethodBuilder<>`. Await a returned value task once. For task composition or repeated awaits, call `AsTask()` once and keep the resulting task. Cold calls and concurrent operations can still allocate.

Other allocation choices are deliberately small:

- Decode only the first matching string; full validation continues without decoding discarded later matches.
- Keep `GetString` for contiguous values. For segmented values, use `CopyString` with a small stack buffer or rented byte array, then create the returned string.
- Share immutable private-reader options while creating an independent reader per inspection.
- Use `JsonProperty.NameEquals` in the serializer baselines so visited property names do not need string allocations.

The returned string still allocates. Pooling reduces repeated scratch allocations after warmup, while retained pool capacity still contributes to memory usage. The default private-reader buffer remains 4 KiB with a 1 KiB minimum read.

## Tests

The extracted implementation passes **115 TUnit cases on .NET 9 and .NET 10**. Coverage includes comparisons against both serializer baselines, every byte split for sample inputs, segmented UTF-8 and escapes, nesting, duplicates, wrong types, empty strings, large values, invalid encoding, truncated JSON, saved state at EOF, cancellation, advancement, retry limits, pending concurrent reads, and backpressure.

Real Kestrel tests verify downstream reads through both `Body` and `BodyReader`, including a 96 KiB payload that makes buffering spill to disk. `ProposedPostExamples.cs` also compiles and exercises the proposed article excerpts.

## Benchmarks

```console
dotnet run --project benchmarks/BodyReaderJson.Benchmarks -c Release -f net10.0 -- --filter '*RequestBenchmarks*' --job Short
dotnet run --project benchmarks/BodyReaderJson.Benchmarks -c Release -f net10.0 -- --filter '*TokenRetryBenchmarks*' --job Short
dotnet run --project benchmarks/BodyReaderJson.Benchmarks -c Release -f net10.0 -- --filter '*AllocationBenchmarks*' '*PendingReadBenchmarks*' --job Short
dotnet run --project benchmarks/BodyReaderJson.Benchmarks -c Release -f net10.0 -- --filter '*StringDecodingBenchmarks*' '*LookupBenchmarks*' --job Short
```

| Benchmark group | Comparison |
|---|---|
| `RequestBenchmarks` | Full-body read, `DeserializeAsync<JsonElement>`, DTO deserialization, full incremental validation, and early extraction; varies position, chunk size, and padding length |
| `TokenRetryBenchmarks` | Retrying every byte versus geometric retries |
| `AllocationBenchmarks` | Frozen task-based batched implementation versus the current allocation improvements |
| `PendingReadBenchmarks` | Missing, matching, and duplicate-heavy documents with a genuinely pending read |
| `StringDecodingBenchmarks` | Small and large matching values, contiguous or segmented, plain or escaped; compares `GetString` and both `CopyString` overloads |
| `LookupBenchmarks` | Property-name lookup over prebuilt elements and reader-constructor experiments over ready bytes |

Quick diagnostics are also available:

```console
dotnet run --project benchmarks/BodyReaderJson.Benchmarks -c Release -f net10.0 -- --allocations
dotnet run --project benchmarks/BodyReaderJson.Benchmarks -c Release -f net10.0 -- --strings
dotnet run --project benchmarks/BodyReaderJson.Benchmarks -c Release -f net10.0 -- --lookup
```

The diagnostics warm caches and report same-thread allocations. Their timings are exploratory; use BenchmarkDotNet for comparisons. The checked-in [measurement notes and reports](docs/benchmarks/README.md) record the environment, fixtures, measured gains, and tradeoffs.

Compare equivalent work. Early extraction may finish before reading or validating the suffix. Complete-document incremental validation is the corresponding serializer comparison. The DTO baseline assumes a known schema with an unambiguous root property; it has different semantics for nested or duplicate names.

The controlled streams return bytes immediately. Request benchmarks include adapter setup and cleanup, while decoding and lookup benchmarks isolate ready data. These timings do not measure network latency, Kestrel throughput, or middleware buffering costs. Run the benchmarks on the deployment environment before drawing production conclusions.

## Results

These are **local .NET 10.0.3 measurements** on Apple M2 / macOS 26.5.2 with SDK 10.0.103 and BenchmarkDotNet 0.15.8. The current request comparison completed 60 cases using `InProcessEmitToolchain`, three warmup iterations, and three measured iterations. Error ranges are available in the [full request report](docs/benchmarks/request-benchmark.md) and [CSV](docs/benchmarks/request-benchmark.csv); some are broad.

### Request timing

Each row below has a **16 KiB padding string** and one small root target, or no target for Missing. Values are mean microseconds; lower is better. All methods in a row receive the same controlled stream.

| Position / chunk bytes | Full body + deserialize | JsonElement async | DTO async | Incremental full validation | Incremental early |
|---|---:|---:|---:|---:|---:|
| First / 64 | 8.482 | 4.948 | 3.341 | 5.036 | 0.187 |
| First / 4096 | 5.605 | 3.930 | 2.332 | 2.686 | 0.262 |
| Last / 64 | 8.463 | 4.920 | 3.380 | 5.117 | 5.022 |
| Last / 4096 | 6.676 | 4.723 | 2.849 | 3.078 | 2.983 |
| Missing / 64 | 8.270 | 4.834 | 3.291 | 4.973 | 4.985 |
| Missing / 4096 | 5.213 | 3.761 | 2.242 | 2.582 | 2.568 |

### Request allocations

The same fixtures allocate the following managed bytes per operation. These include stream adapter setup and cleanup; they exclude middleware buffering and real HTTP transport costs.

| Position / chunk bytes | Full body + deserialize | JsonElement async | DTO async | Incremental full validation | Incremental early |
|---|---:|---:|---:|---:|---:|
| First / 64 | 98856 B | 16856 B | 176 B | 696 B | 408 B |
| First / 4096 | 94920 B | 16856 B | 176 B | 600 B | 408 B |
| Last / 64 | 98856 B | 16856 B | 176 B | 696 B | 696 B |
| Last / 4096 | 94920 B | 16856 B | 176 B | 600 B | 600 B |
| Missing / 64 | 98568 B | 16608 B | 64 B | 656 B | 656 B |
| Missing / 4096 | 94632 B | 16608 B | 64 B | 560 B | 560 B |

### How to read these results

- **Early extraction helps most when the property comes first.** It can return before scanning the large suffix, so its time measures obtaining the value rather than validating the whole document.
- **Last and Missing require most or all of the body.** With 64-byte chunks, full incremental validation is close to, or somewhat slower than, the JsonElement serializer. With 4096-byte chunks, it is faster than that baseline in these large-padding fixtures.
- **A known-schema DTO is still a strong default.** In the large-padding rows it finishes sooner than full incremental validation and allocates much less. Its search contract differs from the general any-depth parser on nested or duplicate names.
- **Incremental inspection greatly reduces allocation compared with building a JsonElement document or copying the full body.** The remaining per-operation bytes largely come from the private stream reader. A borrowed request reader has a different setup cost and needs its own HTTP measurements.
- **These are warmed allocation totals, not peak or retained memory.** Pooled byte buffers still occupy memory. Controlled synchronous streams and short in-process jobs do not establish production HTTP throughput.

For small payloads, setup costs matter more. For example, Last / 64-byte chunks / 16-character padding measured **0.236 µs and 176 B** for the DTO versus **0.320 µs and 408 B** for full incremental validation.

### Large matching values

Skipping padding and returning a large string are different workloads. In the [segmented-string probe](docs/benchmarks/string-decoding-probe.txt), decoding a 16384-character matching value after warmup changed:

| Segmented value | GetString allocation | Current parser allocation | Reduction |
|---|---:|---:|---:|
| Plain text | 49200 B | 32792 B | 33.3% |
| Every character escaped as `\u0078` | 131120 B | 32792 B | 75.0% |

The returned string accounts for the remaining bytes. The [decoding benchmark](docs/benchmarks/string-decoding-benchmark.md) measured comparable or lower time for large split values, while tiny split values cost about 6–8 ns more. Contiguous values keep `GetString`, since they already allocate only the result string. The escaped fixture deliberately stresses decoding; it does not represent a typical request distribution.

## Repository layout

- `src/BodyReaderJson`: parser, buffered inspection helper, serializer baselines, and controlled chunked stream.
- `tests/BodyReaderJson.Tests`: TUnit correctness tests, Kestrel replay tests, and compiled article examples.
- `benchmarks/BodyReaderJson.Benchmarks`: BenchmarkDotNet groups, allocation probes, and the frozen comparison implementation.
- `docs/benchmarks`: recorded local measurements and reproduction notes.

## References

- [Microsoft pipeline examples](https://learn.microsoft.com/en-us/dotnet/standard/io/pipelines#read-streaming-data-scenarios)
- [Partial-token guidance](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-utf8jsonreader#read-from-a-stream-using-utf8jsonreader)
- [Utf8JsonReader.CopyString](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.utf8jsonreader.copystring?view=net-10.0)
- [JsonProperty.NameEquals](https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonproperty.nameequals?view=net-10.0)
- [ValueTask consumption rules](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1?view=net-10.0)
- [ASP.NET Core body reader caching](https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Http/Http/src/Features/RequestBodyPipeFeature.cs)
