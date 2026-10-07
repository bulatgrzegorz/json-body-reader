using System.Text.Json;

namespace BodyReaderJson;

public static class Baselines
{
    public static async Task<string?> FullReadAsync(Stream stream,
        CancellationToken cancellationToken = default)
    {
        using var body = new MemoryStream();
        await stream.CopyToAsync(body, cancellationToken);
        return FindElement(JsonSerializer.Deserialize<JsonElement>(body.ToArray()));
    }

    public static async Task<string?> DeserializeAsync(Stream stream,
        CancellationToken cancellationToken = default)
    {
        JsonElement document = await JsonSerializer.DeserializeAsync<JsonElement>(stream, cancellationToken: cancellationToken);
        return FindElement(document);
    }

    private static string? FindElement(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals("propertyNameToSearchFor"u8)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }

                var nested = FindElement(property.Value);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement child in element.EnumerateArray())
            {
                var nested = FindElement(child);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
