// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Indexers/Newznab/NewznabSettings.cs, src/NzbDrone.Core/Indexers/Torznab/TorznabSettings.cs,
// src/NzbDrone.Core/Indexers/SeedCriteriaSettings.cs, src/NzbDrone.Core/Indexers/FailDownloads.cs and src/NzbDrone.Core/Indexers/SeasonPackSeedGoal.cs
using System.Text.Json.Nodes;

namespace Generral.Core.Indexers;

/// <summary>An indexer implementation: its Sonarr identity and settings schema.</summary>
public sealed record IndexerImplementation(string Name, string ConfigContract, DownloadProtocol Protocol, IReadOnlyList<IndexerField> Fields)
{
    /// <summary>Only Torznab is searched; Newznab indexers are stored so that Prowlarr sync stays idempotent.</summary>
    public bool Supported => Protocol == DownloadProtocol.Torrent;
}

/// <summary>The Torznab and Newznab schemas. Field names match Sonarr's, because Prowlarr writes them by name.</summary>
public static class IndexerImplementations
{
    public const string Torznab = "Torznab";
    public const string Newznab = "Newznab";

    /// <summary>TV and TV/Sport.</summary>
    public static readonly IReadOnlyList<int> DefaultCategories = [5000, 5060];

    private static readonly IReadOnlyList<IndexerSelectOption> CategoryOptions =
    [
        new(2000, "Movies", 0),
        new(5000, "TV", 1),
        new(5030, "TV/SD", 2),
        new(5040, "TV/HD", 3),
        new(5045, "TV/UHD", 4),
        new(5060, "TV/Sport", 5),
        new(5070, "TV/Anime", 6),
        new(5080, "TV/Documentary", 7),
    ];

    private static readonly IReadOnlyList<IndexerField> NewznabFields =
    [
        new() { Order = 0, Name = "baseUrl", Label = "URL" },
        new() { Order = 1, Name = "apiPath", Label = "API Path", Value = "/api", Advanced = true, HelpText = "Path to the api, usually /api" },
        new() { Order = 2, Name = "apiKey", Label = "API Key", Privacy = "apiKey" },
        new() { Order = 3, Name = "categories", Label = "Categories", Type = "select", Value = new JsonArray(DefaultCategories.Select(c => (JsonNode)c).ToArray()), SelectOptions = CategoryOptions, HelpText = "Drop down list, leave blank to disable standard/daily shows" },
        new() { Order = 4, Name = "animeCategories", Label = "Anime Categories", Type = "select", Value = new JsonArray(), SelectOptions = CategoryOptions, HelpText = "Accepted for Prowlarr, not used" },
        new() { Order = 5, Name = "animeStandardFormatSearch", Label = "Anime Standard Format Search", Type = "checkbox", Value = false, HelpText = "Accepted for Prowlarr, not used" },
        new() { Order = 6, Name = "additionalParameters", Label = "Additional Parameters", Advanced = true, HelpText = "Appended to every request, e.g. &foo=bar" },
        new() { Order = 7, Name = "multiLanguages", Label = "Multi Languages", Type = "select", Value = new JsonArray(), SelectOptions = [] },
        new()
        {
            Order = 8, Name = "failDownloads", Label = "Fail Downloads", Type = "select", Value = new JsonArray(),
            SelectOptions = [new(0, "Executables", 0), new(1, "Potentially Dangerous", 1), new(2, "User Defined Extensions", 2)],
        },
    ];

    private static readonly IReadOnlyList<IndexerField> TorrentFields =
    [
        new() { Order = 9, Name = "minimumSeeders", Label = "Minimum Seeders", Type = "number", Value = 1 },
        new() { Order = 10, Name = "seedCriteria.seedRatio", Label = "Seed Ratio", Type = "number", IsFloat = true, HelpText = "Empty uses the download client's default" },
        new() { Order = 11, Name = "seedCriteria.seedTime", Label = "Seed Time", Type = "number", Unit = "minutes", HelpText = "Empty uses the download client's default" },
        new()
        {
            Order = 12, Name = "seedCriteria.seasonPackSeedGoal", Label = "Season Pack Seed Goal", Type = "select", Value = 0,
            SelectOptions = [new(0, "Use standard goals", 0), new(1, "Use season pack goals", 1)],
        },
        new() { Order = 13, Name = "seedCriteria.seasonPackSeedRatio", Label = "Season Pack Seed Ratio", Type = "number", IsFloat = true },
        new() { Order = 14, Name = "seedCriteria.seasonPackSeedTime", Label = "Season Pack Seed Time", Type = "number", Unit = "minutes" },
        new() { Order = 15, Name = "rejectBlocklistedTorrentHashesWhileGrabbing", Label = "Reject Blocklisted Torrent Hashes While Grabbing", Type = "checkbox", Value = false },
        new() { Order = 16, Name = "rejectTorrentFilesWithBlockedExtensionsWhileGrabbing", Label = "Reject Torrent Files With Blocked Extensions While Grabbing", Type = "checkbox", Value = false },
    ];

    public static IReadOnlyList<IndexerImplementation> All { get; } =
    [
        new(Newznab, "NewznabSettings", DownloadProtocol.Usenet, NewznabFields),
        new(Torznab, "TorznabSettings", DownloadProtocol.Torrent, [.. NewznabFields, .. TorrentFields]),
    ];

    public static IndexerImplementation? Find(string? name) =>
        All.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
}
