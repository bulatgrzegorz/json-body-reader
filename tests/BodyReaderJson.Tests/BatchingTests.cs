using System.IO.Pipelines;
using System.Text;

namespace BodyReaderJson.Tests;

public class BatchingTests
{
    [Test]
    public async Task SmallChunksUseBatchedReadsAfterAnIncompleteToken()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"padding\":\"" + new string('x', 16384)
            + "\",\"propertyNameToSearchFor\":\"ok\"}");
        foreach (bool usePostCode in new[] { false, true })
        {
            var reader = new RecordingReader(PipeReader.Create(new ChunkedReadStream(bytes, 64)));
            try
            {
                string? value = usePostCode
                    ? await ProposedPostExamples.FindAsync(reader)
                    : await JsonPropertyReader.FindAsync(reader);
                await Assert.That(value).IsEqualTo("ok");
                await Assert.That(reader.Reads).IsLessThan(32);
                await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
                Console.WriteLine($"Returned reads for 64-byte chunks: {reader.Reads}; proposed post: {usePostCode}");
            }
            finally
            {
                await reader.CompleteAsync();
            }
        }
    }

    [Test]
    public async Task CancellationDuringABatchedReadStillAdvancesTheCanceledResult()
    {
        var pipe = new Pipe();
        var reader = new RecordingReader(pipe.Reader);
        var parsed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            Task<string?> extraction = JsonPropertyReader.ReadCoreAsync(reader, default,
                256 * 1024, false, true, _ => parsed.TrySetResult()).AsTask();
            await pipe.Writer.WriteAsync(Encoding.UTF8.GetBytes("{\"padding\":\"" + new string('x', 64)));
            await parsed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            reader.CancelPendingRead();
            await Assert.That(() => extraction.WaitAsync(TimeSpan.FromSeconds(5)))
                .Throws<OperationCanceledException>();
            await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
            await Assert.That(reader.Reads).IsGreaterThan(1);
        }
        finally
        {
            await reader.CompleteAsync();
            await pipe.Writer.CompleteAsync();
        }
    }

    [Test]
    public async Task BatchedReadsMakeProgressWithSmallPipeBackpressureThresholds()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("{\"padding\":\"" + new string('x', 8192)
            + "\",\"propertyNameToSearchFor\":\"ok\"}");
        var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 512, resumeWriterThreshold: 256));
        var reader = new RecordingReader(pipe.Reader);
        try
        {
            Task<string?> extraction = JsonPropertyReader.FindAsync(reader, validateWholeDocument: true).AsTask();
            Task production = ProduceAsync();
            await Task.WhenAll(extraction, production).WaitAsync(TimeSpan.FromSeconds(10));
            await Assert.That(await extraction).IsEqualTo("ok");
            await Assert.That(reader.Advances).IsEqualTo(reader.Reads);
        }
        finally
        {
            await reader.CompleteAsync();
            await pipe.Writer.CompleteAsync();
        }

        async Task ProduceAsync()
        {
            for (int offset = 0; offset < bytes.Length; offset += 64)
            {
                await pipe.Writer.WriteAsync(bytes.AsMemory(offset, Math.Min(64, bytes.Length - offset)));
            }

            await pipe.Writer.CompleteAsync();
        }
    }

}
