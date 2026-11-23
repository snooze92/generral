// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs and XbmcMetadataSettings.cs
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using Generral.Core.Extensions;
using Generral.Core.Indexers;
using Generral.Core.MediaFiles;
using Generral.Core.Tv;

namespace Generral.Core.Extras.Metadata.Consumers.PlexNfo;

/// <summary>
/// Kodi-format NFO files as read by Plex's <i>Plex NFO Series</i> agent, plus Plex local-asset artwork (spec 08 § Plex-correct metadata).
/// With the NFO options off it writes artwork only, for the <i>Personal Media Shows</i> agent.
/// </summary>
public sealed class PlexNfoMetadata : IMetadataConsumer
{
    public const string Name = "PlexNfo";

    public string Implementation => Name;

    string IMetadataConsumer.Name => "Plex NFO";

    public IReadOnlyList<IndexerField> Fields { get; } =
    [
        new() { Order = 0, Name = "seriesMetadata", Label = "Series Metadata", Type = "checkbox", Value = true, HelpText = "tvshow.nfo, for the Plex NFO Series agent. Turn both metadata options off for artwork only (Personal Media Shows agent)" },
        new() { Order = 1, Name = "episodeMetadata", Label = "Episode Metadata", Type = "checkbox", Value = true, HelpText = "<filename>.nfo next to each episode" },
        new() { Order = 2, Name = "seriesImages", Label = "Series Images", Type = "checkbox", Value = true, HelpText = "poster.jpg, fanart.jpg, banner.jpg, clearlogo.png" },
        new() { Order = 3, Name = "seasonImages", Label = "Season Images", Type = "checkbox", Value = true, HelpText = "Season<number>.jpg in the season folder" },
        new() { Order = 4, Name = "episodeImages", Label = "Episode Images", Type = "checkbox", Value = true, HelpText = "<filename>.jpg" },
        new() { Order = 5, Name = "generatePosters", Label = "Generate Missing Series/Season Posters", Type = "checkbox", Value = true, HelpText = "Draw a plain title poster when a series or season has no poster URL. Never replaces a downloaded image" },
    ];

    /// <summary>The <c>&lt;uniqueid type="generral"&gt;</c> of a series: <c>series-&lt;uuid&gt;</c>.</summary>
    public static string SeriesUniqueId(Series series) =>
        "series-" + (series.Guid.LastIndexOf('/') is var slash and >= 0 ? series.Guid[(slash + 1)..] : series.Guid);

    public static string EpisodeUniqueId(Episode episode) => "ep-" + episode.Id.ToString(CultureInfo.InvariantCulture);

    public MetadataFileResult? SeriesMetadata(MetadataSeries metadata, JsonObject settings)
    {
        if (!settings.Bool("seriesMetadata", true))
        {
            return null;
        }

        var series = metadata.Series;
        var tvShow = new XElement("tvshow", new XElement("title", series.Title));
        AddIfPresent(tvShow, "plot", series.Overview);
        AddIfPresent(tvShow, "premiered", metadata.Premiered?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        tvShow.Add(new XElement("status", series.Status.ToString()));
        AddIfPresent(tvShow, "studio", series.Network);
        foreach (var genre in series.Genres)
        {
            tvShow.Add(new XElement("genre", genre));
        }

        tvShow.Add(UniqueId(SeriesUniqueId(series)));
        foreach (var season in metadata.Seasons.Where(s => !string.IsNullOrWhiteSpace(s.Title)).OrderBy(s => s.SeasonNumber))
        {
            tvShow.Add(new XElement("namedseason", new XAttribute("number", season.SeasonNumber), season.Title));
        }

        return new MetadataFileResult("tvshow.nfo", Write([tvShow]));
    }

    public MetadataFileResult? EpisodeMetadata(EpisodeFile file, IReadOnlyList<Episode> episodes, JsonObject settings)
    {
        if (!settings.Bool("episodeMetadata", true) || episodes.Count == 0)
        {
            return null;
        }

        // Multi-episode files: one <episodedetails> per episode in the same file, as the Plex NFO agent expects.
        var details = episodes.Select(episode =>
        {
            var element = new XElement(
                "episodedetails",
                new XElement("title", episode.Title),
                new XElement("season", episode.SeasonNumber),
                new XElement("episode", episode.EpisodeNumber));
            AddIfPresent(element, "plot", episode.Overview);
            AddIfPresent(element, "aired", (episode.AirDate ?? (episode.AirDateUtc is { } utc ? DateOnly.FromDateTime(utc) : null))?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            if (episode.Runtime is > 0)
            {
                element.Add(new XElement("runtime", episode.Runtime));
            }

            element.Add(UniqueId(EpisodeUniqueId(episode)));
            return element;
        });

        return new MetadataFileResult(Path.ChangeExtension(file.RelativePath, ".nfo"), Write(details));
    }

    public IEnumerable<ImageFileResult> SeriesImages(Series series, JsonObject settings)
    {
        if (!settings.Bool("seriesImages", true))
        {
            yield break;
        }

        var generate = settings.Bool("generatePosters", true);
        foreach (var (name, url) in new[] { ("poster.jpg", series.Images.Poster), ("fanart.jpg", series.Images.Fanart), ("banner.jpg", series.Images.Banner), ("clearlogo.png", series.Images.Logo) })
        {
            var generated = generate && name == "poster.jpg" ? new GeneratedPoster(series.Title, series.Year?.ToString(CultureInfo.InvariantCulture)) : null;
            if (!string.IsNullOrWhiteSpace(url) || generated is not null)
            {
                yield return new ImageFileResult(name, string.IsNullOrWhiteSpace(url) ? null : url, generated);
            }
        }
    }

    public IEnumerable<ImageFileResult> SeasonImages(Series series, Season season, string seasonFolder, JsonObject settings)
    {
        var url = string.IsNullOrWhiteSpace(season.Images.Poster) ? null : season.Images.Poster;
        var generate = settings.Bool("generatePosters", true);
        if (!settings.Bool("seasonImages", true) || (url is null && !generate) || seasonFolder.Length == 0)
        {
            yield break;
        }

        var name = season.SeasonNumber == 0 ? "season-specials-poster.jpg" : $"Season{season.SeasonNumber.ToString("00", CultureInfo.InvariantCulture)}.jpg";
        yield return new ImageFileResult(Path.Combine(seasonFolder, name), url, generate ? new GeneratedPoster(SeasonHeading(season), series.Title) : null);
    }

    /// <summary>The season title, else <c>Specials</c>, the year (year-numbered seasons) or <c>Season N</c>.</summary>
    private static string SeasonHeading(Season season) => season switch
    {
        { Title: { Length: > 0 } title } when !string.IsNullOrWhiteSpace(title) => title,
        { SeasonNumber: 0 } => "Specials",
        { SeasonNumber: >= 1000 and var year } => year.ToString(CultureInfo.InvariantCulture),
        var other => $"Season {other.SeasonNumber.ToString(CultureInfo.InvariantCulture)}",
    };

    public IEnumerable<ImageFileResult> EpisodeImages(EpisodeFile file, IReadOnlyList<Episode> episodes, JsonObject settings)
    {
        var url = episodes.Select(e => e.Images.Thumb).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u))
                  ?? episodes.Select(e => e.Images.Poster).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
        if (settings.Bool("episodeImages", true) && url is not null)
        {
            yield return new ImageFileResult(Path.ChangeExtension(file.RelativePath, ".jpg"), url);
        }
    }

    private static XElement UniqueId(string id) => new("uniqueid", new XAttribute("type", "generral"), new XAttribute("default", "true"), id);

    private static void AddIfPresent(XElement parent, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parent.Add(new XElement(name, value));
        }
    }

    private static string Write(IEnumerable<XElement> elements)
    {
        using var writer = new Utf8StringWriter();
        using (var xml = XmlWriter.Create(writer, new XmlWriterSettings { Encoding = Encoding.UTF8, Indent = true, ConformanceLevel = ConformanceLevel.Fragment }))
        {
            xml.WriteProcessingInstruction("xml", "version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"");
            foreach (var element in elements)
            {
                element.WriteTo(xml);
            }
        }

        return writer + "\n";
    }

    private sealed class Utf8StringWriter : StringWriter
    {
        public Utf8StringWriter()
            : base(CultureInfo.InvariantCulture)
        {
        }

        public override Encoding Encoding => Encoding.UTF8;
    }
}
