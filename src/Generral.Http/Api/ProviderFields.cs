// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/ProviderControllerBase.cs and src/Sonarr.Http/ClientSchema/SchemaBuilder.cs
using System.Text.Json;
using System.Text.Json.Nodes;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;

namespace Generral.Http.Api;

/// <summary>Sonarr's provider <c>fields[]</c> ↔ stored settings, with secrets masked on read and kept when the mask is posted back.</summary>
internal static class ProviderFields
{
    /// <summary>The stored settings with the posted fields applied; the masked placeholder keeps the stored value.</summary>
    public static JsonObject Merge(IEnumerable<IndexerField>? fields, JsonObject? stored)
    {
        var settings = stored?.DeepClone().AsObject() ?? [];
        foreach (var field in (fields ?? []).Where(f => f?.Name is { Length: > 0 }))
        {
            if (field.Value is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == SourceEndpoints.PrivateValue)
            {
                continue;
            }

            if (field.Value is null)
            {
                settings.Remove(field.Name);
            }
            else
            {
                settings[field.Name] = field.Value.DeepClone();
            }
        }

        return settings;
    }

    public static List<IndexerField> Schema(IEnumerable<IndexerField> schema) => schema.Select(f => f with { Value = f.Value?.DeepClone() }).ToList();

    public static List<IndexerField> Read(IEnumerable<IndexerField> schema, JsonObject settings) =>
        schema.Select(f => f with { Value = Mask(f, settings.TryGetPropertyValue(f.Name, out var value) ? value?.DeepClone() : f.Value?.DeepClone()) }).ToList();

    public static ValidationFailure ToFailure(IndexerValidationFailure f) => new(f.PropertyName, f.ErrorMessage);

    private static JsonNode? Mask(IndexerField field, JsonNode? value) =>
        field.Privacy is "password" or "apiKey" && value is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>().Length > 0
            ? SourceEndpoints.PrivateValue
            : value;
}
