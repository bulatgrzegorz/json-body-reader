using System.Buffers;
using System.IO.Pipelines;

namespace BodyReaderJson.Tests;

internal static class TestBuffers
{
    internal static RecordingReader CreateReader(byte[] bytes, int chunkSize)
        => new(PipeReader.Create(new ChunkedReadStream(bytes, chunkSize),
            new StreamPipeReaderOptions(bufferSize: 32, minimumReadSize: 16)));

    internal static ReadOnlySequence<byte> Segmented(ReadOnlyMemory<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return ReadOnlySequence<byte>.Empty;
        }

        var first = new ByteSegment(bytes[..1]);
        ByteSegment last = first;
        for (int i = 1; i < bytes.Length; i++)
        {
            last = last.Append(bytes.Slice(i, 1));
        }

        return new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class ByteSegment : ReadOnlySequenceSegment<byte>
    {
        internal ByteSegment(ReadOnlyMemory<byte> memory) => Memory = memory;

        internal ByteSegment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new ByteSegment(memory) { RunningIndex = RunningIndex + Memory.Length };
            Next = next;
            return next;
        }
    }
}

internal sealed class RecordingReader(PipeReader inner) : PipeReader
{
    private ReadOnlySequence<byte> current;
    private bool outstanding;

    internal int Reads { get; private set; }
    internal int Advances { get; private set; }
    internal long LastConsumed { get; private set; }
    internal long LastExamined { get; private set; }

    public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (outstanding)
        {
            throw new InvalidOperationException("Read before AdvanceTo.");
        }

        return RecordRead(await inner.ReadAsync(cancellationToken));
    }

    protected override async ValueTask<ReadResult> ReadAtLeastAsyncCore(int minimumSize,
        CancellationToken cancellationToken)
    {
        if (outstanding)
        {
            throw new InvalidOperationException("Read before AdvanceTo.");
        }

        return RecordRead(await inner.ReadAtLeastAsync(minimumSize, cancellationToken));
    }

    private ReadResult RecordRead(ReadResult result)
    {
        current = result.Buffer;
        outstanding = true;
        Reads++;
        return result;
    }

    public override void AdvanceTo(SequencePosition consumed) => AdvanceTo(consumed, consumed);

    public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
    {
        if (!outstanding)
        {
            throw new InvalidOperationException("AdvanceTo without a read.");
        }

        LastConsumed = current.Slice(current.Start, consumed).Length;
        LastExamined = current.Slice(current.Start, examined).Length;
        inner.AdvanceTo(consumed, examined);
        outstanding = false;
        Advances++;
    }

    public override void CancelPendingRead() => inner.CancelPendingRead();
    public override void Complete(Exception? exception = null) => inner.Complete(exception);
    public override ValueTask CompleteAsync(Exception? exception = null) => inner.CompleteAsync(exception);
    public override bool TryRead(out ReadResult result) => throw new NotSupportedException();
}
