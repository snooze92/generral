// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Download/Clients/QBittorrent/QBittorrentSettings.cs, QBittorrentPriority.cs,
// QBittorrentState.cs and QBittorrentContentLayout.cs
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;

namespace Generral.Core.Download.Clients.QBittorrent;

public enum QBittorrentPriority
{
    Last = 0,
    First = 1,
}

public enum QBittorrentState
{
    Start = 0,
    ForceStart = 1,
    Stop = 2,
}

public enum QBittorrentContentLayout
{
    Default = 0,
    Original = 1,
    Subfolder = 2,
    NoSubfolder = 3,
}

/// <summary>The typed view of a qBittorrent client's settings (Sonarr field names).</summary>
public sealed partial record QBittorrentSettings
{
    public const string DefaultCategory = "generral";

    public string Host { get; init; } = "localhost";

    public int Port { get; init; } = 8080;

    public bool UseSsl { get; init; }

    public string? UrlBase { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public string? TvCategory { get; init; } = DefaultCategory;

    public string? TvImportedCategory { get; init; }

    public QBittorrentPriority RecentTvPriority { get; init; }

    public QBittorrentPriority OlderTvPriority { get; init; }

    public QBittorrentState InitialState { get; init; }

    public bool SequentialOrder { get; init; }

    public bool FirstAndLast { get; init; }

    public QBittorrentContentLayout ContentLayout { get; init; }

    /// <summary>Comma-separated qBittorrent tags added to every torrent.</summary>
    public string? TorrentTags { get; init; }

    public Uri BaseUrl => new($"{(UseSsl ? "https" : "http")}://{Host}:{Port.ToString(CultureInfo.InvariantCulture)}{NormalizedUrlBase}");

    private string NormalizedUrlBase => string.IsNullOrWhiteSpace(UrlBase) ? "" : "/" + UrlBase.Trim().Trim('/');

    public static IReadOnlyList<IndexerField> Fields { get; } =
    [
        new() { Order = 0, Name = "host", Label = "Host", Value = "localhost" },
        new() { Order = 1, Name = "port", Label = "Port", Type = "number", Value = 8080 },
        new() { Order = 2, Name = "useSsl", Label = "Use SSL", Type = "checkbox", Value = false, HelpText = "Use a secure connection. See Options -> Web UI -> 'Use HTTPS instead of HTTP' in qBittorrent." },
        new() { Order = 3, Name = "urlBase", Label = "URL Base", Advanced = true, HelpText = "Adds a prefix to the qBittorrent url, such as http://[host]:[port]/[urlBase]/api" },
        new() { Order = 4, Name = "username", Label = "Username", Privacy = "userName" },
        new() { Order = 5, Name = "password", Label = "Password", Type = "password", Privacy = "password" },
        new() { Order = 6, Name = "tvCategory", Label = "Category", Value = DefaultCategory, HelpText = "Adding a category specific to Generral avoids conflicts with unrelated non-Generral downloads. Using a category is optional, but strongly recommended." },
        new() { Order = 7, Name = "tvImportedCategory", Label = "Post-Import Category", Advanced = true, HelpText = "Category for Generral to set after it has imported the download. Leave blank to keep the same category." },
        new() { Order = 8, Name = "recentTvPriority", Label = "Recent Priority", Type = "select", Value = 0, SelectOptions = PriorityOptions, HelpText = "Priority to use when grabbing episodes that aired within the last 14 days" },
        new() { Order = 9, Name = "olderTvPriority", Label = "Older Priority", Type = "select", Value = 0, SelectOptions = PriorityOptions, HelpText = "Priority to use when grabbing episodes that aired over 14 days ago" },
        new()
        {
            Order = 10, Name = "initialState", Label = "Initial State", Type = "select", Value = 0, HelpText = "Initial state for torrents added to qBittorrent",
            SelectOptions = [new(0, "Started", 0), new(1, "Force Started", 1), new(2, "Stopped", 2)],
        },
        new() { Order = 11, Name = "sequentialOrder", Label = "Sequential Order", Type = "checkbox", Value = false, HelpText = "Download in sequential order (qBittorrent 4.1.0+)" },
        new() { Order = 12, Name = "firstAndLast", Label = "First and Last First", Type = "checkbox", Value = false, HelpText = "Download first and last pieces first (qBittorrent 4.1.0+)" },
        new()
        {
            Order = 13, Name = "contentLayout", Label = "Content Layout", Type = "select", Value = 0, HelpText = "Whether to use qBittorrent's configured content layout, the original layout from the torrent or always create a subfolder (qBittorrent 4.3.2+)",
            SelectOptions = [new(0, "Default", 0), new(1, "Original", 1), new(2, "Subfolder", 2), new(3, "NoSubfolder", 3)],
        },
        new() { Order = 14, Name = "torrentTags", Label = "Tags", Advanced = true, HelpText = "Comma-separated qBittorrent tags added to every torrent (Generral tracks downloads by infohash, not tags)" },
    ];

    private static IReadOnlyList<IndexerSelectOption> PriorityOptions => [new(0, "Last", 0), new(1, "First", 1)];

    public static QBittorrentSettings From(JsonObject settings)
    {
        var defaults = new QBittorrentSettings();
        return new QBittorrentSettings
        {
            Host = Text(settings, "host") ?? defaults.Host,
            Port = (int?)Number(settings, "port") ?? defaults.Port,
            UseSsl = Bool(settings, "useSsl"),
            UrlBase = Text(settings, "urlBase"),
            Username = Text(settings, "username"),
            Password = Text(settings, "password"),
            TvCategory = settings.ContainsKey("tvCategory") ? Text(settings, "tvCategory") : DefaultCategory,
            TvImportedCategory = Text(settings, "tvImportedCategory"),
            RecentTvPriority = (QBittorrentPriority)((int?)Number(settings, "recentTvPriority") ?? 0),
            OlderTvPriority = (QBittorrentPriority)((int?)Number(settings, "olderTvPriority") ?? 0),
            InitialState = (QBittorrentState)((int?)Number(settings, "initialState") ?? 0),
            SequentialOrder = Bool(settings, "sequentialOrder"),
            FirstAndLast = Bool(settings, "firstAndLast"),
            ContentLayout = (QBittorrentContentLayout)((int?)Number(settings, "contentLayout") ?? 0),
            TorrentTags = Text(settings, "torrentTags"),
        };
    }

    public IReadOnlyList<IndexerValidationFailure> Validate()
    {
        var failures = new List<IndexerValidationFailure>();
        if (!HostRegex().IsMatch(Host))
        {
            failures.Add(new IndexerValidationFailure("host", "Invalid Host"));
        }

        if (Port is < 1 or > 65535)
        {
            failures.Add(new IndexerValidationFailure("port", "Must be between 1 and 65535"));
        }

        if (!string.IsNullOrWhiteSpace(UrlBase) && UrlRegex().IsMatch(UrlBase))
        {
            failures.Add(new IndexerValidationFailure("urlBase", "Invalid URL Base"));
        }

        foreach (var (name, category) in new[] { ("tvCategory", TvCategory), ("tvImportedCategory", TvImportedCategory) })
        {
            if (category is not null && !CategoryRegex().IsMatch(category))
            {
                failures.Add(new IndexerValidationFailure(name, @"Can not contain '\', '//', or start/end with '/'"));
            }
        }

        if (!Enum.IsDefined(RecentTvPriority) || !Enum.IsDefined(OlderTvPriority) || !Enum.IsDefined(InitialState) || !Enum.IsDefined(ContentLayout))
        {
            failures.Add(new IndexerValidationFailure("", "Invalid option value"));
        }

        return failures;
    }

    private static string? Text(JsonObject settings, string name) =>
        settings[name] is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>().Trim() is { Length: > 0 } s ? s : null;

    private static bool Bool(JsonObject settings, string name) => settings[name] is JsonValue v && v.GetValueKind() == JsonValueKind.True;

    private static double? Number(JsonObject settings, string name) => settings[name] switch
    {
        JsonValue v when v.GetValueKind() == JsonValueKind.Number => v.Deserialize<double>(),
        JsonValue v when v.GetValueKind() == JsonValueKind.String && double.TryParse(v.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => null,
    };

    [GeneratedRegex(@"^[-_a-z0-9.:\[\]]+$", RegexOptions.IgnoreCase)]
    private static partial Regex HostRegex();

    [GeneratedRegex(@"^\/?https?://", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();

    [GeneratedRegex(@"^([^\\\/](\/?[^\\\/])*)?$")]
    private static partial Regex CategoryRegex();
}
