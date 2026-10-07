using System.IO.Pipelines;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BodyReaderJson.Tests;

public class MiddlewareTests
{
    [Test]
    [Arguments(1)]
    [Arguments(7)]
    [Arguments(4096)]
    public async Task PrivateInspectionPreservesTheBodyForAnUnusedSharedReader(int chunkSize)
    {
        var bytes = Encoding.UTF8.GetBytes(
            """
                {
                    "propertyNameToSearchFor": "ok",
                    "tail":"
            """
            + new string('z', 8192) + "\"}");
        var context = new DefaultHttpContext
        {
            Request =
            {
                Body = new ChunkedReadStream(bytes, chunkSize)
            }
        };

        context.Request.EnableBuffering();
        PipeReader sharedReader = context.Request.BodyReader;

        try
        {
            await Assert.That(await BufferedRequestInspection.FindAsync(context.Request)).IsEqualTo("ok");
            using var replay = new MemoryStream();
            await sharedReader.CopyToAsync(replay);
            await Assert.That(replay.ToArray().SequenceEqual(bytes)).IsTrue();
        }
        finally
        {
            await sharedReader.CompleteAsync();
            await context.Request.Body.DisposeAsync();
        }
    }

    [Test]
    public async Task RewindingTheBodyDoesNotResetAUsedSharedReader()
    {
        var bytes = """
                    {
                        "propertyNameToSearchFor": "ok",
                        "tail": 123
                    }
                    """u8.ToArray();
        var context = new DefaultHttpContext
        {
            Request =
            {
                Body = new ChunkedReadStream(bytes, 4096)
            }
        };

        context.Request.EnableBuffering();
        PipeReader reader = context.Request.BodyReader;

        try
        {
            await Assert.That(await JsonPropertyReader.FindAsync(reader)).IsEqualTo("ok");
            context.Request.Body.Position = 0;
            using var replay = new MemoryStream();
            await reader.CopyToAsync(replay);
            await Assert.That(replay.ToArray().SequenceEqual(bytes)).IsFalse();
        }
        finally
        {
            await reader.CompleteAsync();
            await context.Request.Body.DisposeAsync();
        }
    }

    [Test]
    public async Task FailedInspectionAlsoRewindsTheBody()
    {
        var bytes = "{\"x\":\"unfinished"u8.ToArray();

        var context = new DefaultHttpContext
        {
            Request =
            {
                Body = new ChunkedReadStream(bytes, 1)
            }
        };

        try
        {
            await Assert.That(() => BufferedRequestInspection.FindAsync(context.Request).AsTask())
                .Throws<System.Text.Json.JsonException>();
            await Assert.That(context.Request.Body.Position).IsEqualTo(0);
            using var replay = new MemoryStream();
            await context.Request.Body.CopyToAsync(replay);
            await Assert.That(replay.ToArray().SequenceEqual(bytes)).IsTrue();
        }
        finally
        {
            await context.Request.Body.DisposeAsync();
        }
    }

    [Test]
    [Arguments(false, false)]
    [Arguments(true, false)]
    [Arguments(false, true)]
    [Arguments(true, true)]
    public async Task KestrelEndpointCanReadTheWholeBodyAfterMiddlewareInspection(
        bool useBodyReader, bool targetAtEnd)
    {
        WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseKestrelCore();
        builder.Services.AddRouting();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        await using WebApplication app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items["found"] = await BufferedRequestInspection.FindAsync(context.Request);
            await next(context);
        });
        app.MapPost("/inspect", async context =>
        {
            if (!Equals(context.Items["found"], "ok"))
            {
                context.Response.StatusCode = 500;
                return;
            }

            if (useBodyReader)
            {
                await context.Request.BodyReader.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
            else
            {
                await context.Request.Body.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
        });

        await app.StartAsync();
        try
        {
            using var client = new HttpClient();
            client.BaseAddress = new Uri(app.Urls.Single());

            var padding = "\"padding\":\"" + new string('z', 96 * 1024) + "\"";
            const string target = "\"propertyNameToSearchFor\":\"ok\"";
            var bytes = Encoding.UTF8.GetBytes(targetAtEnd
                ? "{" + padding + "," + target + "}"
                : "{" + target + "," + padding + "}");
            using var content = new ByteArrayContent(bytes);
            using HttpResponseMessage response = await client.PostAsync("/inspect", content);
            response.EnsureSuccessStatusCode();
            byte[] replay = await response.Content.ReadAsByteArrayAsync();
            await Assert.That(replay.SequenceEqual(bytes)).IsTrue();
        }
        finally
        {
            await app.StopAsync();
        }
    }
}
