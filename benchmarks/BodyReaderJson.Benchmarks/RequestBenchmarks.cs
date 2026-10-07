using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BenchmarkDotNet.Attributes;

namespace BodyReaderJson.Benchmarks;

public enum TargetPosition { First, Last, Missing }

[MemoryDiagnoser]
public class RequestBenchmarks
{
    private byte[] _body = [];

    [Params(TargetPosition.First, TargetPosition.Last, TargetPosition.Missing)]
    public TargetPosition Position { get; set; }

    [Params(64, 4096)]
    public int ChunkSize { get; set; }

    [Params(16, 16384)]
    public int PaddingLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        string padding = "\"padding\":\"" + new string('x', PaddingLength) + "\"";
        const string target = "\"propertyNameToSearchFor\":\"order-123\"";
        string json = Position switch
        {
            TargetPosition.First => "{" + target + "," + padding + "}",
            TargetPosition.Last => "{" + padding + "," + target + "}",
            _ => "{" + padding + "}"
        };
        _body = Encoding.UTF8.GetBytes(json);
        string? expected = Position == TargetPosition.Missing ? null : "order-123";
        foreach (string? actual in new[]
        {
            FullBodyReadAndDeserialize().GetAwaiter().GetResult(),
            JsonSerializerAsync().GetAwaiter().GetResult(),
            JsonSerializerDtoAsync().GetAwaiter().GetResult(),
            IncrementalWithFullValidation().GetAwaiter().GetResult(),
            IncrementalEarlyExtraction().GetAwaiter().GetResult()
        })
        {
            if (actual != expected)
            {
                throw new InvalidOperationException("Benchmark approaches returned different values.");
            }
        }
    }

    [Benchmark(Baseline = true)]
    public async Task<string?> FullBodyReadAndDeserialize()
    {
        await using var stream = new ChunkedReadStream(_body, ChunkSize);
        return await Baselines.FullReadAsync(stream);
    }

    [Benchmark]
    public async Task<string?> JsonSerializerAsync()
    {
        await using var stream = new ChunkedReadStream(_body, ChunkSize);
        return await Baselines.DeserializeAsync(stream);
    }

    [Benchmark]
    public async Task<string?> JsonSerializerDtoAsync()
    {
        await using var stream = new ChunkedReadStream(_body, ChunkSize);
        SearchRequest? value = await JsonSerializer.DeserializeAsync<SearchRequest>(stream);
        return value?.Value;
    }

    [Benchmark]
    public ValueTask<string?> IncrementalWithFullValidation() => IncrementalAsync(true);

    [Benchmark]
    public ValueTask<string?> IncrementalEarlyExtraction() => IncrementalAsync(false);

    private async ValueTask<string?> IncrementalAsync(bool validateWholeDocument)
    {
        await using var stream = new ChunkedReadStream(_body, ChunkSize);
        PipeReader reader = PipeReader.Create(stream, BufferedRequestInspection.ReaderOptions);
        try
        {
            return await JsonPropertyReader.FindAsync(reader, validateWholeDocument: validateWholeDocument);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    public sealed class SearchRequest
    {
        [JsonPropertyName(JsonPropertyReader.PropertyName)]
        public string? Value { get; set; }
    }
}

[MemoryDiagnoser]
public class TokenRetryBenchmarks
{
    private byte[] _body = [];

    [Params(4096, 16384)]
    public int TokenLength { get; set; }

    [GlobalSetup]
    public void Setup() => _body = Encoding.UTF8.GetBytes("{\"padding\":\""
        + new string('x', TokenLength) + "\",\"propertyNameToSearchFor\":\"order-123\"}");

    [Benchmark(Baseline = true)]
    public ValueTask<string?> RetryEveryByte() => RunAsync(false);

    [Benchmark]
    public ValueTask<string?> RetryGeometrically() => RunAsync(true);

    private async ValueTask<string?> RunAsync(bool geometric)
    {
        await using var stream = new ChunkedReadStream(_body, 1);
        PipeReader reader = PipeReader.Create(stream, BufferedRequestInspection.ReaderOptions);
        try
        {
            return await JsonPropertyReader.ReadCoreAsync(reader, default, 256 * 1024,
                validateWholeDocument: false, useGeometricRetries: geometric);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }
}
