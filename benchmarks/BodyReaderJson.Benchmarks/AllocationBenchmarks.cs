using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks.Sources;
using BenchmarkDotNet.Attributes;

namespace BodyReaderJson.Benchmarks;

[MemoryDiagnoser]
public class AllocationBenchmarks
{
    private byte[] _body = [];

    [Params(TargetPosition.First, TargetPosition.Last, TargetPosition.Missing)]
    public TargetPosition Position { get; set; }

    [Params(64, 4096)]
    public int ChunkSize { get; set; }

    [Params(16, 16384)]
    public int PaddingLength { get; set; }

    [Params(false, true)]
    public bool ValidateWholeDocument { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        string padding = "\"padding\":\"" + new string('x', PaddingLength) + "\"";
        const string target = "\"propertyNameToSearchFor\":\"order-123\"";
        _body = Encoding.UTF8.GetBytes(Position switch
        {
            TargetPosition.First => "{" + target + "," + padding + "}",
            TargetPosition.Last => "{" + padding + "," + target + "}",
            _ => "{" + padding + "}"
        });

        string? expected = Position == TargetPosition.Missing ? null : "order-123";
        if (Before().GetAwaiter().GetResult() != expected
            || After().GetAwaiter().GetResult() != expected)
        {
            throw new InvalidOperationException("Allocation benchmark results differ.");
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<string?> Before()
    {
        await using var stream = new ChunkedReadStream(_body, ChunkSize);
        PipeReader reader = PipeReader.Create(stream, new StreamPipeReaderOptions(leaveOpen: true));
        try
        {
            return await BeforeAllocationJsonPropertyReader.FindAsync(reader, validateWholeDocument: ValidateWholeDocument);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    [Benchmark]
    public ValueTask<string?> After() => ReadWithOptionsAsync(BufferedRequestInspection.ReaderOptions);

    internal async ValueTask<string?> ReadWithOptionsAsync(StreamPipeReaderOptions options)
    {
        await using var stream = new ChunkedReadStream(_body, ChunkSize);
        PipeReader reader = PipeReader.Create(stream, options);
        try
        {
            return await JsonPropertyReader.FindAsync(reader, validateWholeDocument: ValidateWholeDocument);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    internal void ParseOnly()
    {
        ReadOnlySequence<byte> buffer = new(_body);
        JsonReaderState state = default;
        bool awaitingValue = false;
        JsonPropertyReader.TryParseFromJson(ref buffer, true, ref state, ref awaitingValue, out _);
    }
}

[MemoryDiagnoser]
public class PendingReadBenchmarks
{
    private DeferredReader _reader = null!;

    [Params("missing", "last", "duplicates")]
    public string Scenario { get; set; } = "missing";

    [GlobalSetup]
    public void Setup()
    {
        string target = "\"propertyNameToSearchFor\":\"first\"";
        string json = Scenario switch
        {
            "missing" => "{}",
            "last" => "{" + target + "}",
            _ => "{" + target + "," + string.Join(',', Enumerable.Repeat(
                "\"propertyNameToSearchFor\":\"" + new string('x', 1024) + "\"", 128)) + "}"
        };
        _reader = new DeferredReader(Encoding.UTF8.GetBytes(json));
        string? expected = Scenario == "missing" ? null : "first";
        if (Before().GetAwaiter().GetResult() != expected || After().GetAwaiter().GetResult() != expected)
        {
            throw new InvalidOperationException("Pending-read benchmark results differ.");
        }
    }

    [Benchmark(Baseline = true)]
    public Task<string?> Before()
    {
        Task<string?> operation = BeforeAllocationJsonPropertyReader.FindAsync(_reader, validateWholeDocument: true);
        _reader.Release();
        return operation;
    }

    [Benchmark]
    public ValueTask<string?> After()
    {
        ValueTask<string?> operation = JsonPropertyReader.FindAsync(_reader, validateWholeDocument: true);
        _reader.Release();
        return operation;
    }
}

internal static class AllocationProbe
{
    internal static int Run()
    {
        Console.WriteLine("Position Chunk Padding Before After Saved (bytes per operation)");
        bool improved = true;
        foreach (TargetPosition position in Enum.GetValues<TargetPosition>())
        foreach (int chunkSize in new[] { 64, 4096 })
        foreach (int paddingLength in new[] { 16, 16384 })
        {
            var benchmark = new AllocationBenchmarks
            {
                Position = position, ChunkSize = chunkSize, PaddingLength = paddingLength
            };
            benchmark.Setup();
            long before = Measure(() => CompleteSynchronously(benchmark.Before()));
            long after = Measure(() => CompleteSynchronously(benchmark.After()));
            improved &= after < before;
            Console.WriteLine($"{position,-7} {chunkSize,5} {paddingLength,7} {before,6} {after,5} {before - after,5}");
        }

        var parser = new AllocationBenchmarks { Position = TargetPosition.Last, ChunkSize = 64, PaddingLength = 16384 };
        parser.Setup();
        Console.WriteLine($"Parser only, match: {Measure(parser.ParseOnly)} B");
        parser.Position = TargetPosition.Missing;
        parser.Setup();
        Console.WriteLine($"Parser only, missing: {Measure(parser.ParseOnly)} B");
        Console.WriteLine($"Stream only: {Measure(() => GC.KeepAlive(new ChunkedReadStream([], 64)))} B");
        Console.WriteLine($"Reader options only: {Measure(() => GC.KeepAlive(new StreamPipeReaderOptions(leaveOpen: true)))} B");
        Console.WriteLine($"Reader creation and completion (existing stream/options, no reads): {MeasureReaderSetup()} B");
        foreach (var options in new[]
        {
            new StreamPipeReaderOptions(minimumReadSize: 1, leaveOpen: true),
            new StreamPipeReaderOptions(minimumReadSize: 64, leaveOpen: true),
            new StreamPipeReaderOptions(minimumReadSize: 512, leaveOpen: true),
            BufferedRequestInspection.ReaderOptions,
            new StreamPipeReaderOptions(bufferSize: 32768, leaveOpen: true)
        })
        {
            Action operation = () => CompleteSynchronously(parser.ReadWithOptionsAsync(options));
            Console.WriteLine($"Missing, 64-byte chunks, 16 KiB padding, buffer {options.BufferSize}, minimum read {options.MinimumReadSize}: {Measure(operation)} B, {MedianMicroseconds(operation):F2} us");
        }
        improved &= MeasurePendingRead();
        MeasureDuplicates();
        Console.WriteLine(improved ? "PASS: every fixture allocates less than the frozen baseline." : "FAIL: allocation reduction is still needed.");
        return improved ? 0 : 1;
    }

    private static long MeasureReaderSetup()
    {
        using var stream = new ChunkedReadStream([], 64);
        var options = new StreamPipeReaderOptions(leaveOpen: true);
        return Measure(() => PipeReader.Create(stream, options).Complete());
    }

    private static bool MeasurePendingRead()
    {
        var reader = new DeferredReader("{}"u8.ToArray());
        long before = Measure(() =>
        {
            Task<string?> operation = BeforeAllocationJsonPropertyReader.FindAsync(reader);
            reader.Release();
            CompleteSynchronously(operation);
        });
        long after = Measure(() =>
        {
            ValueTask<string?> operation = JsonPropertyReader.FindAsync(reader);
            reader.Release();
            CompleteSynchronously(operation);
        });
        Console.WriteLine($"Borrowed reader, missing, one genuinely pending read (same-thread continuation): {before} B before, {after} B after");
        return after < before;
    }

    private static void MeasureDuplicates()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"propertyNameToSearchFor\":\"first\","
            + string.Join(',', Enumerable.Repeat("\"propertyNameToSearchFor\":\"" + new string('x', 1024) + "\"", 128)) + "}");
        var reader = new DeferredReader(bytes);
        long before = Measure(() =>
        {
            Task<string?> operation = BeforeAllocationJsonPropertyReader.FindAsync(reader, validateWholeDocument: true);
            reader.Release();
            CompleteSynchronously(operation);
        });
        long after = Measure(() =>
        {
            ValueTask<string?> operation = JsonPropertyReader.FindAsync(reader, validateWholeDocument: true);
            reader.Release();
            CompleteSynchronously(operation);
        });
        Console.WriteLine($"Full validation, borrowed reader, first match plus 128 discarded 1 KiB matches: {before} B before, {after} B after");
    }

    private static double MedianMicroseconds(Action operation)
    {
        const int count = 4096;
        double[] samples = new double[5];
        for (int sample = 0; sample < samples.Length; sample++)
        {
            long started = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++) operation();
            samples[sample] = Stopwatch.GetElapsedTime(started).TotalMicroseconds / count;
        }
        Array.Sort(samples);
        return samples[2];
    }

    private static void CompleteSynchronously(Task<string?> operation)
    {
        if (!operation.IsCompleted)
        {
            throw new InvalidOperationException("This allocation probe requires synchronously completing fixture reads.");
        }
        operation.GetAwaiter().GetResult();
    }

    private static void CompleteSynchronously(ValueTask<string?> operation)
    {
        if (!operation.IsCompleted)
        {
            throw new InvalidOperationException("This allocation probe requires synchronously completing fixture reads.");
        }
        operation.GetAwaiter().GetResult();
    }

    private static long Measure(Action operation)
    {
        const int count = 4096;
        for (int i = 0; i < count; i++) operation();
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++) operation();
        return (GC.GetAllocatedBytesForCurrentThread() - before) / count;
    }
}

internal sealed class DeferredReader(byte[] body) : PipeReader, IValueTaskSource<ReadResult>
{
    private ManualResetValueTaskSourceCore<ReadResult> _source;

    internal void Release() => _source.SetResult(new ReadResult(new ReadOnlySequence<byte>(body), false, true));

    public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        _source.Reset();
        return new ValueTask<ReadResult>(this, _source.Version);
    }

    public override void AdvanceTo(SequencePosition consumed) { }
    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) { }
    public override void CancelPendingRead() => throw new NotSupportedException();
    public override void Complete(Exception? exception = null) { }
    public override bool TryRead(out ReadResult result) => throw new NotSupportedException();
    public ReadResult GetResult(short token) => _source.GetResult(token);
    public ValueTaskSourceStatus GetStatus(short token) => _source.GetStatus(token);
    public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _source.OnCompleted(continuation, state, token, flags);
}
