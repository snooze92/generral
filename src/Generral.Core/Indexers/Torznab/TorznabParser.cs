// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Indexers/Torznab/TorznabRssParser.cs, src/NzbDrone.Core/Indexers/RssParser.cs,
// src/NzbDrone.Core/Indexers/XElementExtensions.cs and src/NzbDrone.Core/Indexers/Newznab/NewznabCapabilitiesProvider.cs
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Generral.Core.Indexers.Torznab;

/// <summary>An error the indexer reported, or a response that isn't a Torznab document.</summary>
public sealed class TorznabException(string message, bool apiKeyError = false, Exception? innerException = null) : Exception(message, innerException)
{
    public bool ApiKeyError { get; } = apiKeyError;
}

public sealed record TorznabCapabilities
{
    public int DefaultPageSize { get; init; } = 100;

    public int MaxPageSize { get; init; } = 100;

    /// <summary>Null when <c>t=search</c> is not available.</summary>
    public IReadOnlyList<string>? SupportedSearchParameters { get; init; } = ["q"];

    /// <summary>Null when <c>t=tvsearch</c> is not available.</summary>
    public IReadOnlyList<string>? SupportedTvSearchParameters { get; init; } = ["q", "rid", "season", "ep"];

    public IReadOnlyList<TorznabCategory> Categories { get; init; } = [];
}

public sealed record TorznabCategory(int Id, string Name, IReadOnlyList<TorznabCategory> Subcategories);

public static partial class TorznabParser
{
    public static readonly XNamespace Ns = "http://torznab.com/schemas/2015/feed";

    private const string TorrentMimeType = "application/x-bittorrent";
    private const string MagnetMimeType = "application/x-bittorrent;x-scheme-handler/magnet";

    /// <exception cref="TorznabException">Error element, invalid API key or malformed XML.</exception>
    public static TorznabCapabilities ParseCapabilities(string xml)
    {
        var root = Load(xml).Element("caps") ?? throw new TorznabException("Unexpected XML: no <caps> element");

        var caps = new TorznabCapabilities();
        if (root.Element("limits") is { } limits)
        {
            caps = caps with
            {
                DefaultPageSize = ParseInt(limits.Attribute("default")?.Value) ?? caps.DefaultPageSize,
                MaxPageSize = ParseInt(limits.Attribute("max")?.Value) ?? caps.MaxPageSize,
            };
        }

        if (root.Element("searching") is { } searching)
        {
            caps = caps with
            {
                SupportedSearchParameters = SearchParameters(searching.Element("search"), caps.SupportedSearchParameters),
                SupportedTvSearchParameters = SearchParameters(searching.Element("tv-search"), caps.SupportedTvSearchParameters),
            };
        }

        var categories = root.Element("categories")?.Elements("category").Select(c => new TorznabCategory(
            ParseInt(c.Attribute("id")?.Value) ?? 0,
            c.Attribute("name")?.Value ?? "",
            c.Elements("subcat").Select(s => new TorznabCategory(ParseInt(s.Attribute("id")?.Value) ?? 0, s.Attribute("name")?.Value ?? "", [])).ToList())).ToList();

        return caps with { Categories = categories ?? [] };
    }

    /// <summary>Parses a <c>t=search</c>/RSS response. Items without a usable download link are skipped.</summary>
    /// <exception cref="TorznabException">Error element, invalid API key or malformed XML.</exception>
    public static List<ReleaseInfo> ParseReleases(string xml, Uri requestUrl, Indexer? indexer = null)
    {
        var channel = Load(xml).Root?.Element("channel");
        if (channel is null)
        {
            return [];
        }

        var releases = new List<ReleaseInfo>();
        foreach (var item in channel.Elements("item"))
        {
            var enclosure = Enclosure(item);
            var magnet = Attribute(item, "magneturl");
            var downloadUrl = Url(requestUrl, enclosure?.Attribute("url")?.Value) ?? Url(requestUrl, item.Element("link")?.Value);
            if (downloadUrl is null && string.IsNullOrWhiteSpace(magnet))
            {
                continue;
            }

            var seeders = ParseInt(Attribute(item, "seeders"));
            var peers = ParseInt(Attribute(item, "peers")) ?? (seeders + ParseInt(Attribute(item, "leechers")));
            var comments = item.Element("comments")?.Value;

            releases.Add(new ReleaseInfo
            {
                Guid = item.Element("guid")?.Value is { Length: > 0 } guid ? guid : downloadUrl ?? magnet!,
                Title = WebUtility.HtmlDecode(item.Element("title")?.Value ?? "Unknown"),
                Size = ParseLong(Attribute(item, "size")) ?? ParseLong(enclosure?.Attribute("length")?.Value) ?? 0,
                DownloadUrl = downloadUrl,
                MagnetUrl = string.IsNullOrWhiteSpace(magnet) ? null : magnet,
                InfoHash = Attribute(item, "infohash") is { Length: > 0 } hash ? hash : null,
                InfoUrl = Url(requestUrl, comments is null ? null : comments.EndsWith("#comments", StringComparison.Ordinal) ? comments[..^"#comments".Length] : comments),
                CommentUrl = Url(requestUrl, comments),
                Seeders = seeders,
                Peers = peers,
                PublishDate = ParseDate(item.Element("pubDate")?.Value),
                Categories = Attributes(item, "category").Select(ParseInt).OfType<int>().Distinct().ToList(),
                IndexerFlags = Flags(item),
                IndexerId = indexer?.Id ?? 0,
                Indexer = indexer?.Name ?? "",
                IndexerPriority = indexer?.Priority ?? Indexer.DefaultPriority,
            });
        }

        return releases;
    }

    private static XDocument Load(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml);
        }
        catch (XmlException e)
        {
            throw new TorznabException($"Invalid XML: {e.Message}", innerException: e);
        }

        if (document.Descendants("error").FirstOrDefault() is { } error)
        {
            var code = ParseInt(error.Attribute("code")?.Value) ?? 0;
            var description = error.Attribute("description")?.Value ?? "";
            if (code is >= 100 and <= 199)
            {
                throw new TorznabException("Invalid API key", apiKeyError: true);
            }

            throw description == "Request limit reached"
                ? new TorznabException("API limit reached")
                : new TorznabException($"Torznab error detected: {description}");
        }

        return document;
    }

    private static IReadOnlyList<string>? SearchParameters(XElement? element, IReadOnlyList<string>? defaults)
    {
        if (element is null || element.Attribute("available")?.Value != "yes")
        {
            return null;
        }

        return element.Attribute("supportedParams")?.Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries) ?? defaults;
    }

    private static XElement? Enclosure(XElement item)
    {
        var enclosures = item.Elements("enclosure").ToList();
        return enclosures.FirstOrDefault(e => e.Attribute("type")?.Value == TorrentMimeType)
               ?? enclosures.FirstOrDefault(e => e.Attribute("type")?.Value == MagnetMimeType)
               ?? (enclosures.Count == 1 && enclosures[0].Attribute("type") is null ? enclosures[0] : null);
    }

    private static IndexerFlags Flags(XElement item)
    {
        var flags = IndexerFlags.None;
        var download = ParseDouble(Attribute(item, "downloadvolumefactor")) ?? 1;
        var upload = ParseDouble(Attribute(item, "uploadvolumefactor")) ?? 1;

        flags |= download switch
        {
            0.5 => IndexerFlags.Halfleech,
            0.75 => IndexerFlags.Freeleech25,
            0.25 => IndexerFlags.Freeleech75,
            0.0 => IndexerFlags.Freeleech,
            _ => IndexerFlags.None,
        };

        if (upload == 2.0)
        {
            flags |= IndexerFlags.DoubleUpload;
        }

        var tags = Attributes(item, "tag").ToList();
        if (tags.Any(t => t.Equals("internal", StringComparison.OrdinalIgnoreCase)))
        {
            flags |= IndexerFlags.Internal;
        }

        if (tags.Any(t => t.Equals("scene", StringComparison.OrdinalIgnoreCase)))
        {
            flags |= IndexerFlags.Scene;
        }

        return flags;
    }

    private static string? Attribute(XElement item, string name) => Attributes(item, name).FirstOrDefault();

    private static IEnumerable<string> Attributes(XElement item, string name) =>
        item.Elements(Ns + "attr")
            .Where(e => string.Equals(e.Attribute("name")?.Value, name, StringComparison.OrdinalIgnoreCase))
            .Select(e => e.Attribute("value")?.Value)
            .OfType<string>();

    private static string? Url(Uri requestUrl, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(requestUrl, value.Trim(), out var url) ? url.AbsoluteUri : null;
    }

    private static DateTime ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.UnixEpoch;
        }

        if (!DateTime.TryParse(value, DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal, out var result) &&
            !DateTime.TryParse(TimeZoneSuffixRegex().Replace(value, ""), DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal, out result))
        {
            return DateTime.UnixEpoch;
        }

        return result.ToUniversalTime();
    }

    private static int? ParseInt(string? value) => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static long? ParseLong(string? value) => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;

    private static double? ParseDouble(string? value) => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : null;

    [GeneratedRegex(@"\s[A-Z]{2,4}$")]
    private static partial Regex TimeZoneSuffixRegex();
}
