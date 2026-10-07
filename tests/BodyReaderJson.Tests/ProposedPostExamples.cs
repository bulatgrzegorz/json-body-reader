using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BodyReaderJson.Tests;

public static class ProposedPostExamples
{
    private const string PropertyName = "propertyNameToSearchFor";
    private static readonly byte[] PropertyNameBytes = Encoding.UTF8.GetBytes(PropertyName);

    private static bool TryParseFromJson(
        ref ReadOnlySequence<byte> buffer,
        bool isFinalBlock,
        ref JsonReaderState readerState,
        ref bool awaitingValue,
        out string? value)
    {
        var jsonReader = new Utf8JsonReader(buffer, isFinalBlock, readerState);
        value = null;

        try
        {
            while (jsonReader.Read())
            {
                if (awaitingValue)
                {
                    awaitingValue = false;
                    if (jsonReader.TokenType == JsonTokenType.String)
                    {
                        value = jsonReader.HasValueSequence
                            ? GetSegmentedString(ref jsonReader)
                            : jsonReader.GetString();
                        return true;
                    }
                }

                awaitingValue = jsonReader.TokenType == JsonTokenType.PropertyName
                    && jsonReader.ValueTextEquals(PropertyNameBytes);
            }

            return false;
        }
        finally
        {
            buffer = buffer.Slice(jsonReader.Position);
            readerState = jsonReader.CurrentState;
        }
    }

    private static string GetSegmentedString(ref Utf8JsonReader reader)
    {
        int length = checked((int)reader.ValueSequence.Length);
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

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<string?> FindAsync(
        PipeReader pipeReader,
        CancellationToken cancellationToken = default,
        int maxPendingBytes = 256 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPendingBytes);

        JsonReaderState readerState = default;
        bool awaitingValue = false;
        long nextParseLength = 0;

        while (true)
        {
            var readResult = nextParseLength == 0
                ? await pipeReader.ReadAsync(cancellationToken)
                : await pipeReader.ReadAtLeastAsync((int)nextParseLength, cancellationToken);
            var buffer = readResult.Buffer;
            var consumed = buffer.Start;

            try
            {
                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                if (TryParseFromJson(ref buffer, readResult.IsCompleted,
                    ref readerState, ref awaitingValue, out string? value))
                {
                    consumed = buffer.Start;
                    return value;
                }

                consumed = buffer.Start;

                if (buffer.Length >= maxPendingBytes)
                {
                    throw new InvalidDataException("Unfinished JSON token reached the buffer limit.");
                }

                if (readResult.IsCompleted)
                {
                    return null;
                }

                nextParseLength = buffer.IsEmpty
                    ? 0
                    : Math.Min(maxPendingBytes, buffer.Length * 2);
            }
            finally
            {
                pipeReader.AdvanceTo(consumed, buffer.End);
            }
        }
    }
}
