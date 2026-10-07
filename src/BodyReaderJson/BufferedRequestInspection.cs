using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;

namespace BodyReaderJson;

public static class BufferedRequestInspection
{
    internal static readonly StreamPipeReaderOptions ReaderOptions = new(leaveOpen: true);

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public static async ValueTask<string?> FindAsync(HttpRequest request)
    {
        request.EnableBuffering();
        PipeReader reader = PipeReader.Create(request.Body, ReaderOptions);

        try
        {
            return await JsonPropertyReader.FindAsync(reader, request.HttpContext.RequestAborted);
        }
        finally
        {
            try
            {
                await reader.CompleteAsync();
            }
            finally
            {
                request.Body.Position = 0;
            }
        }
    }
}
