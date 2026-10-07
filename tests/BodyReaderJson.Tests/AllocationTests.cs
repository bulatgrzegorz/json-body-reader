using System.IO.Pipelines;
using System.Text;
using System.Text.Json;

namespace BodyReaderJson.Tests;

public class AllocationTests
{
    [Test]
    public async Task ConcurrentPendingReadsKeepTheirParserStateSeparate()
    {
        for (int attempt = 0; attempt < 20; attempt++)
        {
            var first = new Pipe();
            var second = new Pipe();
            try
            {
                Task<string?> firstRead = JsonPropertyReader.FindAsync(first.Reader, validateWholeDocument: true).AsTask();
                Task<string?> secondRead = JsonPropertyReader.FindAsync(second.Reader, validateWholeDocument: true).AsTask();
                await first.Writer.WriteAsync("{\"outer\":{\"propertyNameToSearchFor\":"u8.ToArray());
                await second.Writer.WriteAsync("[{}, {\"propertyNameToSearchFor\":"u8.ToArray());
                await second.Writer.WriteAsync("\"second\"}]"u8.ToArray());
                await second.Writer.CompleteAsync();
                await Assert.That(await secondRead.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo("second");
                await first.Writer.WriteAsync("\"first\"}}"u8.ToArray());
                await first.Writer.CompleteAsync();
                await Assert.That(await firstRead.WaitAsync(TimeSpan.FromSeconds(5))).IsEqualTo("first");
            }
            finally
            {
                await first.Reader.CompleteAsync();
                await second.Reader.CompleteAsync();
                await first.Writer.CompleteAsync();
                await second.Writer.CompleteAsync();
            }
        }
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task FullValidationKeepsTheFirstValueAndChecksTheSuffix(bool malformed)
    {
        string laterValue = new('x', 8192);
        byte[] bytes = Encoding.UTF8.GetBytes("{\"propertyNameToSearchFor\":\"\",\"nested\":{"
            + "\"propertyNameToSearchFor\":\"" + laterValue + "\"},\"tail\":"
            + (malformed ? "[1,}" : "[1,2]}"));
        var reader = new RecordingReader(PipeReader.Create(new ChunkedReadStream(bytes, 64)));
        try
        {
            if (malformed)
            {
                await Assert.That(() => JsonPropertyReader.FindAsync(reader, validateWholeDocument: true).AsTask())
                    .Throws<JsonException>();
            }
            else
            {
                await Assert.That(await JsonPropertyReader.FindAsync(reader, validateWholeDocument: true))
                    .IsEqualTo("");
            }
            await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
        }
        finally
        {
            await reader.CompleteAsync();
        }
    }
}
