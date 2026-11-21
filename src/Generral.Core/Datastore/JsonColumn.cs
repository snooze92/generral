using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Generral.Core.Datastore;

/// <summary>Stores a property as a JSON TEXT column (camelCase, nulls omitted), compared by its serialized form.</summary>
public static class JsonColumn
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static PropertyBuilder<T> HasJsonConversion<T>(this PropertyBuilder<T> property)
    {
        var converter = new ValueConverter<T, string>(v => Serialize(v), s => Deserialize<T>(s));
        var comparer = new ValueComparer<T>(
            (a, b) => Serialize(a) == Serialize(b),
            v => Serialize(v).GetHashCode(StringComparison.Ordinal),
            v => Deserialize<T>(Serialize(v)));

        return property.HasConversion(converter, comparer);
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options)!;
}
