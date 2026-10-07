using System.Buffers;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace BodyReaderJson.Benchmarks;

[MemoryDiagnoser]
public class LookupBenchmarks
{
    private JsonElement _document;
    private ReadOnlySequence<byte> _bytes;
    private ReadOnlySequence<byte> _remaining;
    private JsonReaderState _state;

    [Params(1, 64)]
    public int PropertyCount { get; set; }

    [Params(false, true)]
    public bool Missing { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        string properties = string.Join(',', Enumerable.Range(0, PropertyCount).Select(i => $"\"field{i}\":{i}"));
        byte[] bytes = Encoding.UTF8.GetBytes("{" + properties
            + (Missing ? "}" : ",\"propertyNameToSearchFor\":\"order-123\"}"));
        _document = JsonSerializer.Deserialize<JsonElement>(bytes);
        _bytes = new ReadOnlySequence<byte>(bytes);
    }

    [Benchmark(Baseline = true)]
    public string? GetStringNames() => Find(_document, nameEquals: false);

    [Benchmark]
    public string? NameEquals() => Find(_document, nameEquals: true);

    [Benchmark]
    public string? SequenceReader() => Scan(useSpan: false);

    [Benchmark]
    public string? SpanReader() => Scan(useSpan: true);

    private static string? Find(JsonElement element, bool nameEquals)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                bool matches = nameEquals ? property.NameEquals("propertyNameToSearchFor"u8)
                    : property.Name == JsonPropertyReader.PropertyName;
                if (matches && property.Value.ValueKind == JsonValueKind.String) return property.Value.GetString();
                string? nested = Find(property.Value, nameEquals);
                if (nested is not null) return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                string? nested = Find(child, nameEquals);
                if (nested is not null) return nested;
            }
        }
        return null;
    }

    private string? Scan(bool useSpan)
    {
        ReadOnlySequence<byte> buffer = _bytes;
        JsonReaderState state = default;
        var reader = useSpan ? new Utf8JsonReader(buffer.FirstSpan, true, state)
            : new Utf8JsonReader(buffer, true, state);
        bool awaitingValue = false;
        try
        {
            while (reader.Read())
            {
                if (awaitingValue)
                {
                    awaitingValue = false;
                    if (reader.TokenType == JsonTokenType.String) return reader.GetString();
                }
                awaitingValue = reader.TokenType == JsonTokenType.PropertyName
                    && reader.ValueTextEquals("propertyNameToSearchFor"u8);
            }
            return null;
        }
        finally
        {
            buffer = useSpan ? buffer.Slice(reader.BytesConsumed) : buffer.Slice(reader.Position);
            state = reader.CurrentState;
            _remaining = buffer;
            _state = state;
        }
    }

    internal static void Probe()
    {
        foreach (int count in new[] { 1, 64 })
        foreach (bool missing in new[] { false, true })
        {
            var fixture = new LookupBenchmarks { PropertyCount = count, Missing = missing };
            fixture.Setup();
            string expected = missing ? "<missing>" : "order-123";
            foreach (var (label, method) in new (string, Func<string?>)[]
            {
                ("GetStringNames", fixture.GetStringNames), ("NameEquals", fixture.NameEquals),
                ("SequenceReader", fixture.SequenceReader), ("SpanReader", fixture.SpanReader)
            })
            {
                if ((method() ?? "<missing>") != expected) throw new InvalidOperationException("Lookup results differ.");
                if (label is "SequenceReader" or "SpanReader")
                {
                    if (fixture._remaining.Length != (missing ? 0 : 1)) throw new InvalidOperationException("Reader positions differ.");
                    var remainderReader = new Utf8JsonReader(fixture._remaining, true, fixture._state);
                    while (remainderReader.Read()) { }
                }
                Func<string> operation = () => method() ?? "<missing>";
                Console.WriteLine($"Lookup {count} properties, missing {missing}, {label}: {StringDecodingProbe.MeasureBytes(operation)} B / {StringDecodingProbe.MeasureMicroseconds(operation):F3} us");
            }
        }
    }
}
