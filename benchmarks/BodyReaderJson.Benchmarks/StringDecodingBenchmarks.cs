using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BenchmarkDotNet.Attributes;

namespace BodyReaderJson.Benchmarks;

[MemoryDiagnoser]
public class StringDecodingBenchmarks
{
    private ReadOnlySequence<byte> _token;
    private ReadOnlySequence<byte> _document;
    private string _expected = "";

    [Params(16, 16384)]
    public int Length { get; set; }

    [Params(false, true)]
    public bool Segmented { get; set; }

    [Params(false, true)]
    public bool Escaped { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _expected = new string('x', Length);
        string raw = Escaped ? string.Concat(Enumerable.Repeat("\\u0078", Length)) : _expected;
        byte[] token = Encoding.UTF8.GetBytes("\"" + raw + "\"");
        byte[] document = Encoding.UTF8.GetBytes("{\"propertyNameToSearchFor\":\"" + raw + "\"}");
        _token = MakeSequence(token);
        _document = MakeSequence(document);
        if (GetString() != _expected || CurrentParser() != _expected)
        {
            throw new InvalidOperationException("String fixtures returned different values.");
        }
    }

    [Benchmark(Baseline = true)]
    public string GetString()
    {
        var reader = new Utf8JsonReader(_token, true, default);
        reader.Read();
        return reader.GetString()!;
    }

    [Benchmark]
    public string CopyChars()
    {
        var reader = new Utf8JsonReader(_token, true, default);
        reader.Read();
        int length = checked((int)(reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length));
        char[]? rented = null;
        Span<char> scratch = length <= 256 ? stackalloc char[256] : (rented = ArrayPool<char>.Shared.Rent(length));
        try
        {
            int written = reader.CopyString(scratch);
            return new string(scratch[..written]);
        }
        finally
        {
            if (rented is not null) ArrayPool<char>.Shared.Return(rented);
        }
    }

    [Benchmark]
    public string CopyBytes()
    {
        var reader = new Utf8JsonReader(_token, true, default);
        reader.Read();
        int length = checked((int)(reader.HasValueSequence ? reader.ValueSequence.Length : reader.ValueSpan.Length));
        byte[]? rented = null;
        Span<byte> scratch = length <= 256 ? stackalloc byte[256] : (rented = ArrayPool<byte>.Shared.Rent(length));
        try
        {
            int written = reader.CopyString(scratch);
            return Encoding.UTF8.GetString(scratch[..written]);
        }
        finally
        {
            if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    internal string ParseValueDocument()
    {
        var reader = new Utf8JsonReader(_token, true, default);
        reader.Read();
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        return document.RootElement.GetString()!;
    }

    internal string CurrentParser()
    {
        ReadOnlySequence<byte> buffer = _document;
        JsonReaderState state = default;
        bool awaitingValue = false;
        if (!JsonPropertyReader.TryParseFromJson(ref buffer, true, ref state, ref awaitingValue, out string? value))
        {
            throw new InvalidOperationException("Fixture target missing.");
        }
        return value!;
    }

    internal string AllocateResultOnly() => new('x', Length);

    private ReadOnlySequence<byte> MakeSequence(byte[] bytes)
    {
        if (!Segmented) return new ReadOnlySequence<byte>(bytes);
        int segmentSize = Math.Min(4096, bytes.Length / 4);
        var first = new StringSegment(bytes.AsMemory(0, segmentSize));
        StringSegment tail = first;
        for (int offset = segmentSize; offset < bytes.Length; offset += segmentSize)
        {
            tail = tail.Append(bytes.AsMemory(offset, Math.Min(segmentSize, bytes.Length - offset)));
        }
        return new ReadOnlySequence<byte>(first, 0, tail, tail.Memory.Length);
    }

    private sealed class StringSegment : ReadOnlySequenceSegment<byte>
    {
        internal StringSegment(ReadOnlyMemory<byte> memory) => Memory = memory;

        internal StringSegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new StringSegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }

    }
}

internal static class StringDecodingProbe
{
    internal static int Run()
    {
        bool passes = true;
        Console.WriteLine("Length Segmented Escaped GetString Parser ResultOnly (allocated bytes)");
        foreach (int length in new[] { 16, 16384 })
        foreach (bool segmented in new[] { false, true })
        foreach (bool escaped in new[] { false, true })
        {
            var fixture = new StringDecodingBenchmarks { Length = length, Segmented = segmented, Escaped = escaped };
            fixture.Setup();
            long getString = MeasureBytes(fixture.GetString);
            long parser = MeasureBytes(fixture.CurrentParser);
            long resultOnly = MeasureBytes(fixture.AllocateResultOnly);
            passes &= parser <= resultOnly;
            Console.WriteLine($"{length,6} {segmented,9} {escaped,7} {getString,9} {parser,6} {resultOnly,10}");
            if (fixture.CopyChars() != fixture.GetString() || fixture.CopyBytes() != fixture.GetString())
            {
                throw new InvalidOperationException("CopyString candidates returned different values.");
            }
            Console.WriteLine($"  GetString {MeasureMicroseconds(fixture.GetString):F3} us; CopyChars {MeasureBytes(fixture.CopyChars)} B / {MeasureMicroseconds(fixture.CopyChars):F3} us; CopyBytes {MeasureBytes(fixture.CopyBytes)} B / {MeasureMicroseconds(fixture.CopyBytes):F3} us");
            if (segmented && escaped && length == 16384)
            {
                Console.WriteLine($"  ParseValueDocument: {MeasureBytes(fixture.ParseValueDocument)} B / {MeasureMicroseconds(fixture.ParseValueDocument):F3} us");
            }
        }
        Console.WriteLine(passes ? "PASS: parser allocates only its result string." : "FAIL: parser allocates temporary string-decoding buffers.");
        return passes ? 0 : 1;
    }

    internal static long MeasureBytes(Func<string> operation)
    {
        const int count = 128;
        for (int i = 0; i < count; i++) GC.KeepAlive(operation());
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < count; i++) GC.KeepAlive(operation());
        return (GC.GetAllocatedBytesForCurrentThread() - start) / count;
    }

    internal static double MeasureMicroseconds(Func<string> operation)
    {
        const int count = 1024;
        double[] samples = new double[5];
        for (int i = 0; i < count; i++) GC.KeepAlive(operation());
        for (int sample = 0; sample < samples.Length; sample++)
        {
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < count; i++) GC.KeepAlive(operation());
            samples[sample] = Stopwatch.GetElapsedTime(start).TotalMicroseconds / count;
        }
        Array.Sort(samples);
        return samples[2];
    }
}
