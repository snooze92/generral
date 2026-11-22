// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Notifications/Webhook/WebhookBase.cs, WebhookPayload.cs, WebhookEventType.cs,
// WebhookSeries.cs, WebhookEpisode.cs, WebhookEpisodeFile.cs, WebhookRelease.cs, WebhookGrabbedRelease.cs, WebhookImage.cs,
// WebhookGrabPayload.cs, WebhookImportPayload.cs, WebhookEpisodeDeletePayload.cs, WebhookSeriesAddPayload.cs and WebhookSeriesDeletePayload.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using Generral.Core.Datastore;
using Generral.Core.Languages;
using Generral.Core.MediaFiles;
using Generral.Core.Tv;

namespace Generral.Core.Notifications.Webhook;

/// <summary>Sonarr's event types, serialised in PascalCase like Sonarr's (<c>"eventType": "Download"</c>).</summary>
public enum WebhookEventType
{
    Test,
    Grab,
    Download,
    Rename,
    SeriesAdd,
    SeriesDelete,
    EpisodeFileDelete,
    Health,
    ApplicationUpdate,
    HealthRestored,
    ManualInteractionRequired,
}

public record WebhookPayload
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    [JsonConverter(typeof(JsonStringEnumConverter<WebhookEventType>))]
    public WebhookEventType EventType { get; init; }

    public string? InstanceName { get; init; }

    public string? ApplicationUrl { get; init; }

    public WebhookSeries? Series { get; init; }

    public List<WebhookEpisode>? Episodes { get; init; }
}

public sealed record WebhookGrabPayload : WebhookPayload
{
    public required WebhookRelease Release { get; init; }

    public string? DownloadClient { get; init; }

    public string? DownloadClientType { get; init; }

    public string? DownloadId { get; init; }
}

public sealed record WebhookImportPayload : WebhookPayload
{
    public required WebhookEpisodeFile EpisodeFile { get; init; }

    public bool IsUpgrade { get; init; }

    public string? DownloadClient { get; init; }

    public string? DownloadClientType { get; init; }

    public string? DownloadId { get; init; }

    public List<WebhookEpisodeFile>? DeletedFiles { get; init; }

    public WebhookGrabbedRelease? Release { get; init; }
}

public sealed record WebhookEpisodeDeletePayload : WebhookPayload
{
    public required WebhookEpisodeFile EpisodeFile { get; init; }

    public required string DeleteReason { get; init; }
}

public sealed record WebhookSeriesAddPayload : WebhookPayload;

public sealed record WebhookSeriesDeletePayload : WebhookPayload
{
    public bool DeletedFiles { get; init; }
}

/// <summary>Sonarr's series shape; the provider ids are 0 (Generral has none).</summary>
public sealed record WebhookSeries(int Id, string Title, string TitleSlug, string Path, SeriesType Type, int Year, List<string> Genres, List<WebhookImage> Images, List<string> Tags)
{
    public int TvdbId => 0;

    public int TvMazeId => 0;

    public int TmdbId => 0;

    public static WebhookSeries From(Series series, IReadOnlyList<string> tags) => new(
        series.Id, series.Title, series.TitleSlug, series.Path, series.SeriesType, series.Year ?? 0, series.Genres, WebhookImage.From(series.Images), [.. tags]);
}

public sealed record WebhookImage(string CoverType, string Url, string RemoteUrl)
{
    public static List<WebhookImage> From(ImageSet images) =>
        new[] { ("poster", images.Poster), ("banner", images.Banner), ("fanart", images.Fanart), ("screenshot", images.Thumb), ("clearlogo", images.Logo) }
            .Where(i => !string.IsNullOrWhiteSpace(i.Item2))
            .Select(i => new WebhookImage(i.Item1, i.Item2!, i.Item2!))
            .ToList();
}

public sealed record WebhookEpisode(int Id, int EpisodeNumber, int SeasonNumber, string Title, string? Overview, string? AirDate, DateTime? AirDateUtc, int SeriesId)
{
    public int TvdbId => 0;

    public static WebhookEpisode From(Episode episode) => new(
        episode.Id, episode.EpisodeNumber, episode.SeasonNumber, episode.Title, episode.Overview, episode.AirDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), episode.AirDateUtc, episode.SeriesId);
}

public sealed record WebhookEpisodeFile
{
    public int Id { get; init; }

    public required string RelativePath { get; init; }

    public required string Path { get; init; }

    public required string Quality { get; init; }

    public int QualityVersion { get; init; }

    public string? ReleaseGroup { get; init; }

    public string? SceneName { get; init; }

    public long Size { get; init; }

    public DateTime DateAdded { get; init; }

    public List<Language> Languages { get; init; } = [];

    public string? SourcePath { get; init; }

    public static WebhookEpisodeFile From(EpisodeFile file, string? sourcePath = null) => new()
    {
        Id = file.Id,
        RelativePath = file.RelativePath,
        Path = file.Path,
        Quality = file.Quality.Quality.Name,
        QualityVersion = file.Quality.Revision.Version,
        ReleaseGroup = file.ReleaseGroup,
        SceneName = file.SceneName,
        Size = file.Size,
        DateAdded = file.DateAdded,
        Languages = file.Languages,
        SourcePath = sourcePath,
    };
}

public sealed record WebhookRelease(string Quality, int QualityVersion, string? ReleaseGroup, string ReleaseTitle, string? Indexer, long Size, List<Language> Languages)
{
    public int CustomFormatScore => 0;

    public List<string> CustomFormats => [];
}

public sealed record WebhookGrabbedRelease(string? ReleaseTitle, string? Indexer, long? Size);
