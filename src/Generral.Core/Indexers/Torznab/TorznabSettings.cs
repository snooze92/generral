// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Indexers/Torznab/TorznabSettings.cs, src/NzbDrone.Core/Indexers/Newznab/NewznabSettings.cs
// and src/NzbDrone.Core/Indexers/SeedCriteriaSettings.cs
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Generral.Core.Indexers.Torznab;

public sealed record IndexerValidationFailure(string PropertyName, string ErrorMessage);

/// <summary>The typed view of an indexer's settings (Sonarr field names, flattened like Sonarr's <c>fields[]</c>).</summary>
public sealed partial record TorznabSettings
{
    public string? BaseUrl { get; init; }

    public string ApiPath { get; init; } = "/api";

    public string? ApiKey { get; init; }

    public IReadOnlyList<int> Categories { get; init; } = IndexerImplementations.DefaultCategories;

    /// <summary>Accepted for Prowlarr, not used.</summary>
    public IReadOnlyList<int> AnimeCategories { get; init; } = [];

    /// <summary>Appended to every request, e.g. <c>&amp;foo=bar</c>.</summary>
    public string? AdditionalParameters { get; init; }

    public int MinimumSeeders { get; init; } = 1;

    public double? SeedRatio { get; init; }

    /// <summary>Minutes.</summary>
    public int? SeedTime { get; init; }

    /// <summary>Minutes.</summary>
    public int? SeasonPackSeedTime { get; init; }

    public bool RejectBlocklistedTorrentHashesWhileGrabbing { get; init; }

    public static TorznabSettings From(JsonObject settings) => new()
    {
        BaseUrl = Text(settings, "baseUrl"),
        ApiPath = Text(settings, "apiPath") ?? "/api",
        ApiKey = Text(settings, "apiKey"),
        Categories = Ints(settings, "categories") ?? IndexerImplementations.DefaultCategories,
        AnimeCategories = Ints(settings, "animeCategories") ?? [],
        AdditionalParameters = Text(settings, "additionalParameters"),
        MinimumSeeders = (int?)Number(settings, "minimumSeeders") ?? 1,
        SeedRatio = Number(settings, "seedCriteria.seedRatio"),
        SeedTime = (int?)Number(settings, "seedCriteria.seedTime"),
        SeasonPackSeedTime = (int?)Number(settings, "seedCriteria.seasonPackSeedTime"),
        RejectBlocklistedTorrentHashesWhileGrabbing = settings["rejectBlocklistedTorrentHashesWhileGrabbing"] is JsonValue v && v.GetValueKind() == JsonValueKind.True,
    };

    public IReadOnlyList<IndexerValidationFailure> Validate()
    {
        var failures = new List<IndexerValidationFailure>();
        if (Categories.Count == 0 && AnimeCategories.Count == 0)
        {
            failures.Add(new IndexerValidationFailure("", "Either 'Categories' or 'Anime Categories' must be provided"));
        }

        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
        {
            failures.Add(new IndexerValidationFailure("baseUrl", "must be valid URL that starts with http(s)://"));
        }

        if (UrlRegex().IsMatch(ApiPath))
        {
            failures.Add(new IndexerValidationFailure("apiPath", "Must be a valid URL path (ie: '/api')"));
        }

        if (!string.IsNullOrWhiteSpace(AdditionalParameters) && !AdditionalParametersRegex().IsMatch(AdditionalParameters))
        {
            failures.Add(new IndexerValidationFailure("additionalParameters", "Must be like '&name=value'"));
        }

        if (SeedRatio <= 0 || SeedTime <= 0 || SeasonPackSeedTime <= 0)
        {
            failures.Add(new IndexerValidationFailure("seedCriteria", "Should be greater than zero"));
        }

        return failures;
    }

    /// <summary><c>{baseUrl}{apiPath}?t=…</c>, with the additional parameters and the API key.</summary>
    public Uri RequestUrl(string query)
    {
        var url = $"{BaseUrl!.TrimEnd('/')}{ApiPath.TrimEnd('/')}?{query}{AdditionalParameters}";
        return new Uri(string.IsNullOrWhiteSpace(ApiKey) ? url : $"{url}&apikey={Uri.EscapeDataString(ApiKey)}");
    }

    private static string? Text(JsonObject settings, string name) =>
        settings[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() is { Length: > 0 } s ? s : null;

    private static double? Number(JsonObject settings, string name) => settings[name] switch
    {
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => v.Deserialize<double>(),
        JsonValue v when v.GetValueKind() == JsonValueKind.String && double.TryParse(v.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    private static List<int>? Ints(JsonObject settings, string name) => settings[name] switch
    {
        JsonArray a => a.OfType<JsonValue>().Where(v => v.GetValueKind() == JsonValueKind.Number).Select(v => (int)v.Deserialize<double>()).ToList(),
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>()
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(s => int.TryParse(s, CultureInfo.InvariantCulture, out var i) ? (int?)i : null).OfType<int>().ToList(),
        _ => null,
    };

    [GeneratedRegex(@"^\/?https?://[-_a-z0-9.]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"(&.+?\=.+?)+")]
    private static partial Regex AdditionalParametersRegex();
}
