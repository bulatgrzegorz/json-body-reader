using System.Buffers;
using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace BodyReaderJson.Tests;

public class ParserTests
{
    public static IEnumerable<(string Json, string? Expected)> ValidInputs() =>
    [
        ("{}", null), ("[]", null), ("null", null), ("12345", null), ("true", null), ("\"root\"", null),
        ("{\"propertyNameToSearchFor\":\"first\"}", "first"),
        ("{\"id\":42,\"propertyNameToSearchFor\":\"last\"}", "last"),
        ("{\"nested\":{\"propertyNameToSearchFor\":\"nested\"},\"propertyNameToSearchFor\":\"root\"}", "nested"),
        ("[{\"a\":1},{\"propertyNameToSearchFor\":\"array\"}]", "array"),
        ("{\"propertyNameToSearchFor\":null,\"propertyNameToSearchFor\":\"second\"}", "second"),
        ("{\"propertyNameToSearchFor\":{},\"propertyNameToSearchFor\":\"second\"}", "second"),
        ("{\"propertyNameToSearchFor\":{\"propertyNameToSearchFor\":\"inside\"}}", "inside"),
        ("{\"propertyNameToSearchFor\":\"one\",\"propertyNameToSearchFor\":\"two\"}", "one"),
        ("{\"propertyNameToSearchFor\":\"\"}", ""),
        ("{\"propertyNameToSearchFor\":\"Zażółć 🫡 \\n \\u263a \\\" \\\\\"}", "Zażółć 🫡 \n ☺ \" \\"),
        ("{\"propertyNameToSearchFo\\u0072\":\"escaped name\"}", "escaped name"),
        ("{\"propertyNameToSearchFor\":17,\"array\":[1.2e-34,false,null],\"tail\":[]}", null),
        (" \r\n {\"other\":\"value\"} \t ", null)
    ];

    public static IEnumerable<string> InvalidInputs() =>
    [
        "", " ", "{", "[", "{\"x\":", "{\"x\":\"unfinished", "{\"x\":123", "{\"x\":1,",
        "{\"x\":1", "[1,2", "{\"x\":true", "{} trailing", "{}{}",
        "{\"propertyNameToSearchFor\":\"unfinished",
        "{\"propertyNameToSearchFor\":\"ok\"",
        "{\"propertyNameToSearchFor\":\"ok\"} trailing"
    ];

    [Test]
    [MethodDataSource(nameof(ValidInputs))]
    public async Task AllApproachesReturnTheSameFirstString(string json, string? expected)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        foreach (var chunkSize in new[] { 1, 2, 3, 7, 31, 4096 })
        {
            await using var fullStream = new ChunkedReadStream(bytes, chunkSize);
            await using var serializerStream = new ChunkedReadStream(bytes, chunkSize);
            await Assert.That(await Baselines.FullReadAsync(fullStream)).IsEqualTo(expected);
            await Assert.That(await Baselines.DeserializeAsync(serializerStream)).IsEqualTo(expected);

            foreach (bool validateWholeDocument in new[] { false, true })
            {
                RecordingReader reader = TestBuffers.CreateReader(bytes, chunkSize);
                try
                {
                    string? actual = await JsonPropertyReader.FindAsync(reader,
                        validateWholeDocument: validateWholeDocument);
                    await Assert.That(actual).IsEqualTo(expected)
                        .Because($"chunk size {chunkSize}, validation {validateWholeDocument}");
                    await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
                }
                finally
                {
                    await reader.CompleteAsync();
                }
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(ValidInputs))]
    public async Task TheExactProposedPostCodeMatchesTheBaselines(string json, string? expected)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        foreach (int chunkSize in new[] { 1, 2, 3, 7, 31, 4096 })
        {
            RecordingReader reader = TestBuffers.CreateReader(bytes, chunkSize);
            try
            {
                await Assert.That(await ProposedPostExamples.FindAsync(reader)).IsEqualTo(expected);
                await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
            }
            finally
            {
                await reader.CompleteAsync();
            }
        }
    }

    [Test]
    [MethodDataSource(nameof(ValidInputs))]
    public async Task EveryByteSplitWorksWithSegmentedRemainders(string json, string? expected)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        for (int split = 1; split < bytes.Length; split++)
        {
            JsonReaderState state = default;
            bool awaitingValue = false;
            ReadOnlySequence<byte> prefix = new(bytes.AsMemory(0, split));
            bool found = JsonPropertyReader.TryParseFromJson(ref prefix, false,
                ref state, ref awaitingValue, out string? value);

            if (!found)
            {
                int consumed = split - (int)prefix.Length;
                ReadOnlySequence<byte> remainder = TestBuffers.Segmented(bytes.AsMemory(consumed));
                found = JsonPropertyReader.TryParseFromJson(ref remainder, true,
                    ref state, ref awaitingValue, out value);
            }

            await Assert.That(found ? value : null).IsEqualTo(expected).Because($"split {split}");
        }
    }

    [Test]
    [MethodDataSource(nameof(InvalidInputs))]
    public async Task WholeDocumentReadersRejectMalformedInput(string json)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        foreach (int chunkSize in new[] { 1, 2, 7, 4096 })
        {
            using var fullStream = new ChunkedReadStream(bytes, chunkSize);
            using var serializerStream = new ChunkedReadStream(bytes, chunkSize);
            await Assert.That(() => Baselines.FullReadAsync(fullStream)).Throws<JsonException>();
            await Assert.That(() => Baselines.DeserializeAsync(serializerStream)).Throws<JsonException>();
            RecordingReader reader = TestBuffers.CreateReader(bytes, chunkSize);
            try
            {
                await Assert.That(() => JsonPropertyReader.FindAsync(reader,
                    validateWholeDocument: true).AsTask()).Throws<JsonException>();
                await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
            }
            finally
            {
                await reader.CompleteAsync();
            }
        }
    }

    [Test]
    public async Task AnEmptyFinalBufferStillValidatesSavedNesting()
    {
        ReadOnlySequence<byte> prefix = new("{\"x\":\"complete token\""u8.ToArray());
        JsonReaderState state = default;
        bool awaitingValue = false;
        JsonPropertyReader.TryParseFromJson(ref prefix, false, ref state, ref awaitingValue, out _);
        await Assert.That(prefix.IsEmpty).IsTrue();

        ReadOnlySequence<byte> finalBuffer = ReadOnlySequence<byte>.Empty;
        await Assert.That(() => JsonPropertyReader.TryParseFromJson(ref finalBuffer,
            true, ref state, ref awaitingValue, out _)).Throws<JsonException>();
    }

    [Test]
    public async Task APropertyAtTheStartOfANewBufferPreservesNestingAndOptions()
    {
        var state = new JsonReaderState(new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 5
        });
        bool awaitingValue = false;
        ReadOnlySequence<byte> prefix = new("{\"outer\":{"u8.ToArray());
        JsonPropertyReader.TryParseFromJson(ref prefix, false, ref state, ref awaitingValue, out _);

        ReadOnlySequence<byte> property = new(Encoding.UTF8.GetBytes("\"propertyNameToSearchFor\":"));
        await Assert.That(JsonPropertyReader.TryParseFromJson(ref property, false,
            ref state, ref awaitingValue, out _)).IsFalse();
        await Assert.That(awaitingValue).IsTrue();
        await Assert.That(property.IsEmpty).IsTrue();
        await Assert.That(state.Options.MaxDepth).IsEqualTo(5);
        await Assert.That(state.Options.AllowTrailingCommas).IsTrue();
        await Assert.That(state.Options.CommentHandling).IsEqualTo(JsonCommentHandling.Skip);

        ReadOnlySequence<byte> valueBuffer = new(Encoding.UTF8.GetBytes("\"ok\"},}"));
        bool found = JsonPropertyReader.TryParseFromJson(ref valueBuffer, true,
            ref state, ref awaitingValue, out string? value);
        await Assert.That(found).IsTrue();
        await Assert.That(value).IsEqualTo("ok");
        await Assert.That(JsonPropertyReader.TryParseFromJson(ref valueBuffer, true,
            ref state, ref awaitingValue, out _)).IsFalse();
    }

    [Test]
    public async Task EarlyExtractionLeavesSuffixValidationToTheCaller()
    {
        byte[] bytes = "{\"propertyNameToSearchFor\":\"ok\", trailing"u8.ToArray();
        RecordingReader reader = TestBuffers.CreateReader(bytes, 4096);
        try
        {
            await Assert.That(await JsonPropertyReader.FindAsync(reader)).IsEqualTo("ok");
            await Assert.That(reader.LastExamined).IsGreaterThan(reader.LastConsumed);
            await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    [Test]
    public async Task ACompletedResultCanContainTheMatchingProperty()
    {
        var pipe = new Pipe();
        await pipe.Writer.WriteAsync("{\"propertyNameToSearchFor\":\"final buffer\"}"u8.ToArray());
        await pipe.Writer.CompleteAsync();
        var reader = new RecordingReader(pipe.Reader);
        try
        {
            await Assert.That(await JsonPropertyReader.FindAsync(reader)).IsEqualTo("final buffer");
            await Assert.That(reader.Reads).IsEqualTo(1);
            await Assert.That(reader.Advances).IsEqualTo(1);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    [Test]
    public async Task CanceledReadResultsAreAdvanced()
    {
        var pipe = new Pipe();
        var reader = new RecordingReader(pipe.Reader);
        reader.CancelPendingRead();
        try
        {
            await Assert.That(() => JsonPropertyReader.FindAsync(reader).AsTask()).Throws<OperationCanceledException>();
            await Assert.That(reader.Reads).IsEqualTo(1);
            await Assert.That(reader.Advances).IsEqualTo(1);
        }
        finally
        {
            await reader.CompleteAsync();
            await pipe.Writer.CompleteAsync();
        }
    }

    [Test]
    public async Task TokenCancellationDoesNotInventAReadToAdvance()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        RecordingReader reader = TestBuffers.CreateReader([1], 1);
        try
        {
            await Assert.That(() => JsonPropertyReader.FindAsync(reader, cancellation.Token).AsTask())
                .Throws<OperationCanceledException>();
            await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    [Test]
    [Arguments(4096)]
    [Arguments(8192)]
    [Arguments(16384)]
    public async Task GeometricRetriesBoundTheInputSuppliedToTheParser(int tokenLength)
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"padding\":\"" + new string('x', tokenLength)
            + "\",\"propertyNameToSearchFor\":\"ok\"}");
        long naiveWork = 0;
        long geometricWork = 0;

        foreach (bool geometric in new[] { false, true })
        {
            RecordingReader reader = TestBuffers.CreateReader(bytes, 1);
            long work = 0;
            try
            {
                string? value = await JsonPropertyReader.ReadCoreAsync(reader, CancellationToken.None,
                    256 * 1024, false, geometric, length => work += length);
                await Assert.That(value).IsEqualTo("ok");
                await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
            }
            finally
            {
                await reader.CompleteAsync();
            }

            if (geometric) geometricWork = work; else naiveWork = work;
        }

        await Assert.That(geometricWork).IsLessThan(bytes.Length * 5L);
        await Assert.That(naiveWork).IsGreaterThan(geometricWork * 100);
        Console.WriteLine($"Token {tokenLength}: naive parser input {naiveWork}, geometric {geometricWork}");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task LargeNamesAndStringValuesWorkAcrossReads(bool validateWholeDocument)
    {
        string expected = new('ą', 8192);
        string json = "{\"" + new string('n', 16384) + "\":null,\"propertyNameToSearchFor\":\""
            + expected + "\"}";
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        RecordingReader reader = TestBuffers.CreateReader(bytes, 7);
        try
        {
            await Assert.That(await JsonPropertyReader.FindAsync(reader,
                validateWholeDocument: validateWholeDocument)).IsEqualTo(expected);
            await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    [Test]
    public async Task ThePendingBufferLimitDoesNotRejectAlreadyCompleteTokens()
    {
        string expected = new('x', 4096);
        byte[] bytes = Encoding.UTF8.GetBytes("{\"propertyNameToSearchFor\":\"" + expected + "\"}");
        var reader = new RecordingReader(PipeReader.Create(new ChunkedReadStream(bytes, bytes.Length),
            new StreamPipeReaderOptions(bufferSize: bytes.Length + 1024, minimumReadSize: 1)));
        try
        {
            await Assert.That(await JsonPropertyReader.FindAsync(reader,
                maxPendingBytes: 32, validateWholeDocument: true)).IsEqualTo(expected);
            await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    [Test]
    public async Task UnfinishedTokensHaveABufferLimit()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"padding\":\"" + new string('x', 16384));
        RecordingReader reader = TestBuffers.CreateReader(bytes, 1);
        try
        {
            await Assert.That(() => JsonPropertyReader.FindAsync(reader, maxPendingBytes: 1024).AsTask())
                .Throws<InvalidDataException>();
            await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }
}
