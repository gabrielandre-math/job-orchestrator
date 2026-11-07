namespace JobOrchestrator.Intelligence.Features;

/// <summary>
/// Whether an answer has the shape its JSON schema asks for: types, required properties, array items and enums, the subset the
/// app's schemas use. Parseable JSON with the wrong shape is a failed answer: it goes to the next provider and is never cached.
/// </summary>
internal static class JsonShape
{
    public static bool Matches(string schema, string? json)
    {
        if (json is null) return false;
        try
        {
            using var s = JsonDocument.Parse(schema);
            using var j = JsonDocument.Parse(json);
            return Fits(s.RootElement, j.RootElement);
        }
        catch (JsonException) { return false; }
    }

    private static bool Fits(JsonElement schema, JsonElement value)
    {
        if (schema.ValueKind != JsonValueKind.Object) return true;
        if (schema.TryGetProperty("type", out var type) && !TypeOk(type, value)) return false;
        if (schema.TryGetProperty("enum", out var options) && options.ValueKind == JsonValueKind.Array
            && !options.EnumerateArray().Any(o => o.ValueKind == value.ValueKind && o.GetRawText() == value.GetRawText())) return false;
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array
                && required.EnumerateArray().Any(r => r.GetString() is { } name && !value.TryGetProperty(name, out _))) return false;
            if (schema.TryGetProperty("properties", out var properties) && properties.ValueKind == JsonValueKind.Object)
                foreach (var p in properties.EnumerateObject())
                    if (value.TryGetProperty(p.Name, out var child) && !Fits(p.Value, child)) return false;
        }
        if (value.ValueKind == JsonValueKind.Array && schema.TryGetProperty("items", out var items))
            foreach (var item in value.EnumerateArray())
                if (!Fits(items, item)) return false;
        return true;
    }

    private static bool TypeOk(JsonElement type, JsonElement value) => type.ValueKind switch
    {
        JsonValueKind.String => Is(type.GetString()!, value),
        JsonValueKind.Array => type.EnumerateArray().Any(t => t.GetString() is { } name && Is(name, value)),
        _ => true
    };

    private static bool Is(string type, JsonElement value) => type switch
    {
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "number" => value.ValueKind == JsonValueKind.Number,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => true
    };
}
