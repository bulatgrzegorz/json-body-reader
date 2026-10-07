using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace BodyReaderJson.Tests;

public class StringDecodingTests
{
    [Test]
    [Arguments(1, false)]
    [Arguments(1, true)]
    [Arguments(512, false)]
    [Arguments(512, true)]
    [Arguments(8192, false)]
    [Arguments(8192, true)]
    public async Task MatchingStringsDecodeAcrossUtf8AndEscapeBoundaries(int repetitions, bool escapeUnicode)
    {
        string expected = string.Concat(Enumerable.Repeat("Zażółć 🫡\n\"\\☺", repetitions));
        var options = new JsonSerializerOptions
        {
            Encoder = escapeUnicode ? JavaScriptEncoder.Default : JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        byte[] bytes = Encoding.UTF8.GetBytes("{\"propertyNameToSearchFor\":"
            + JsonSerializer.Serialize(expected, options) + "}");

        using var full = new ChunkedReadStream(bytes, 64);
        using var serializer = new ChunkedReadStream(bytes, 64);
        await Assert.That(await Baselines.FullReadAsync(full)).IsEqualTo(expected);
        await Assert.That(await Baselines.DeserializeAsync(serializer)).IsEqualTo(expected);

        foreach (bool segmented in new[] { false, true })
        {
            ReadOnlySequence<byte> buffer = segmented ? TestBuffers.Segmented(bytes) : new(bytes);
            JsonReaderState state = default;
            bool awaitingValue = false;
            bool found = JsonPropertyReader.TryParseFromJson(ref buffer, true, ref state,
                ref awaitingValue, out string? actual);
            await Assert.That(found).IsTrue();
            await Assert.That(actual).IsEqualTo(expected);
            await Assert.That(buffer.Length).IsEqualTo(1);
            await Assert.That(JsonPropertyReader.TryParseFromJson(ref buffer, true, ref state,
                ref awaitingValue, out _)).IsFalse();
            await Assert.That(buffer.IsEmpty).IsTrue();
        }

        RecordingReader reader = TestBuffers.CreateReader(bytes, 4096);
        try
        {
            await Assert.That(await ProposedPostExamples.FindAsync(reader,
                maxPendingBytes: bytes.Length + 1)).IsEqualTo(expected);
            await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }

    public static IEnumerable<(int Padding, string InvalidHex)> InvalidStrings()
    {
        foreach (int padding in new[] { 0, 512 })
        foreach (byte[] invalid in new byte[][]
        {
            [0xc3, 0x28], [0xed, 0xa0, 0x80], "\\uD800"u8.ToArray(), "\\uDC00"u8.ToArray()
        })
        {
            yield return (padding, Convert.ToHexString(invalid));
        }
    }

    [Test]
    [MethodDataSource(nameof(InvalidStrings))]
    public async Task MatchingStringsRejectInvalidEncodingLikeGetString(int padding, string invalidHex)
    {
        byte[] invalid = Convert.FromHexString(invalidHex);
        byte[] bytes = [.. "{\"propertyNameToSearchFor\":\""u8, .. Encoding.UTF8.GetBytes(new string('x', padding)),
            .. invalid, .. "\"}"u8];
        foreach (bool segmented in new[] { false, true })
        {
            ReadOnlySequence<byte> buffer = segmented ? TestBuffers.Segmented(bytes) : new(bytes);
            await Assert.That(() => DecodeWithGetString(buffer)).Throws<InvalidOperationException>();
            JsonReaderState state = default;
            bool awaitingValue = false;
            await Assert.That(() => JsonPropertyReader.TryParseFromJson(ref buffer, true,
                ref state, ref awaitingValue, out _)).Throws<InvalidOperationException>();
        }
    }

    private static string? DecodeWithGetString(ReadOnlySequence<byte> buffer)
    {
        var reader = new Utf8JsonReader(buffer, true, default);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.String) return reader.GetString();
        }
        throw new InvalidOperationException("Fixture did not contain a string.");
    }
}
