using System.Buffers;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace BodyReaderJson;

public static class JsonPropertyReader
{
    public const string PropertyName = "propertyNameToSearchFor";
    private static readonly byte[] PropertyNameBytes = Encoding.UTF8.GetBytes(PropertyName);

    public static bool TryParseFromJson(
        ref ReadOnlySequence<byte> buffer,
        bool isFinalBlock,
        ref JsonReaderState readerState,
        ref bool awaitingValue,
        out string? value,
        bool findValue = true)
    {
        var jsonReader = new Utf8JsonReader(buffer, isFinalBlock, readerState);
        value = null;

        try
        {
            while (jsonReader.Read())
            {
                if (!findValue) continue;

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

    public static ValueTask<string?> FindAsync(
        PipeReader pipeReader,
        CancellationToken cancellationToken = default,
        int maxPendingBytes = 256 * 1024,
        bool validateWholeDocument = false)
        => ReadCoreAsync(pipeReader, cancellationToken, maxPendingBytes,
            validateWholeDocument, useGeometricRetries: true);

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    internal static async ValueTask<string?> ReadCoreAsync(
        PipeReader pipeReader,
        CancellationToken cancellationToken,
        int maxPendingBytes,
        bool validateWholeDocument,
        bool useGeometricRetries,
        Action<long>? recordParseLength = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPendingBytes);

        JsonReaderState readerState = default;
        bool awaitingValue = false;
        string? firstValue = null;
        long nextParseLength = 0;

        while (true)
        {
            ReadResult readResult = nextParseLength == 0
                ? await pipeReader.ReadAsync(cancellationToken)
                : await pipeReader.ReadAtLeastAsync((int)nextParseLength, cancellationToken);
            ReadOnlySequence<byte> buffer = readResult.Buffer;
            SequencePosition consumed = buffer.Start;

            try
            {
                if (readResult.IsCanceled)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                recordParseLength?.Invoke(buffer.Length);

                while (TryParseFromJson(ref buffer, readResult.IsCompleted,
                    ref readerState, ref awaitingValue, out string? value, findValue: firstValue is null))
                {
                    firstValue ??= value;
                    if (validateWholeDocument) continue;

                    consumed = buffer.Start;
                    return firstValue;
                }

                consumed = buffer.Start;

                if (buffer.Length >= maxPendingBytes)
                {
                    throw new InvalidDataException("Unfinished JSON token reached the buffer limit.");
                }

                if (readResult.IsCompleted)
                {
                    return firstValue;
                }

                nextParseLength = useGeometricRetries && !buffer.IsEmpty
                    ? Math.Min(maxPendingBytes, buffer.Length * 2)
                    : 0;
            }
            finally
            {
                pipeReader.AdvanceTo(consumed, buffer.End);
            }
        }
    }
}
