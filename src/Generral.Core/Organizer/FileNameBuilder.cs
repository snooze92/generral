// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Organizer/FileNameBuilder.cs, EpisodeFormat.cs and FileNameBuilderTokenEqualityComparer.cs
// (only the tokens listed in spec 08 § Tokens, plus Generral's)
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Generral.Core.Extensions;
using Generral.Core.MediaFiles;
using Generral.Core.Qualities;
using Generral.Core.Sources.Extraction;
using Generral.Core.Tv;

namespace Generral.Core.Organizer;

/// <summary>What Generral knows about an episode beyond Sonarr's model: its event and part (for the Generral tokens).</summary>
public sealed record EpisodeNamingInfo(string? EventKey, string? PartKey, string? PartTitle, JsonObject? EventFields);

/// <summary>Everything a name is built from; <see cref="QualityTitles"/> are the quality definitions' titles by quality id.</summary>
public sealed record NamingInput(Series Series, IReadOnlyList<Episode> Episodes, EpisodeFile File)
{
    public IReadOnlyDictionary<int, string> QualityTitles { get; init; } = new Dictionary<int, string>();

    public IReadOnlyDictionary<int, EpisodeNamingInfo> Info { get; init; } = new Dictionary<int, EpisodeNamingInfo>();

    public string? SeasonTitle { get; init; }
}

public static partial class FileNameBuilder
{
    public const int MaxFileNameLength = 255;

    private static readonly string[] BadCharacters = ["\\", "/", "<", ">", "?", "*", "|", "\""];
    private static readonly string[] GoodCharacters = ["+", "+", "", "", "!", "-", "", ""];
    private static readonly char[] EpisodeTitleTrimCharacters = [' ', '.', '?'];

    public static bool HasSeasonEpisode(string pattern) => SeasonEpisodePatternRegex().IsMatch(pattern);

    public static bool HasAirDate(string pattern) => AirDateRegex().IsMatch(pattern);

    public static bool HasSeason(string pattern) => SeasonRegex().IsMatch(pattern) || pattern.Contains("{Season}", StringComparison.OrdinalIgnoreCase);

    /// <summary>The file name (with <paramref name="extension"/>, e.g. <c>.mkv</c>), or the original name when renaming is off.</summary>
    public static string BuildFileName(NamingInput input, NamingConfig config, string extension = "")
    {
        if (!config.RenameEpisodes)
        {
            return CleanFileName(OriginalTitle(input.File), config) + extension;
        }

        var episodes = input.Episodes.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList();
        var pattern = input.Series.SeriesType == SeriesType.Daily && episodes[0].SeasonNumber > 0 ? config.DailyEpisodeFormat : config.StandardEpisodeFormat;
        var components = new List<string>();
        var splitPatterns = pattern.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < splitPatterns.Length; i++)
        {
            var tokens = new Dictionary<string, Func<TokenMatch, string?>>(TokenComparer.Instance);
            var split = AddSeasonEpisodeNumberingTokens(splitPatterns[i], tokens, episodes, config);
            split = AddAbsoluteNumberingTokens(split, tokens, episodes, config);
            split = split.Replace("...", "{{ellipsis}}", StringComparison.Ordinal);

            AddSeriesTokens(tokens, input.Series);
            AddEpisodeTokens(tokens, episodes);
            tokens["{Episode Title}"] = _ => null;
            tokens["{Episode CleanTitle}"] = _ => null;
            AddEpisodeFileTokens(tokens, input.File);
            AddQualityTokens(tokens, input);
            AddGenerralTokens(tokens, input, episodes);

            var component = ReplaceTokens(split, tokens, config, escape: true).Trim();
            var maxLength = MaxFileNameLength - (i == splitPatterns.Length - 1 ? Encoding.UTF8.GetByteCount(extension) : 0);
            var maxTitleLength = maxLength - LengthWithoutEpisodeTitle(component, config);
            tokens["{Episode Title}"] = m => EpisodeTitle(EpisodeTitles(episodes), "+", maxTitleLength, m.CustomFormat);
            tokens["{Episode CleanTitle}"] = m => EpisodeTitle(EpisodeTitles(episodes).Select(CleanTitle).ToList(), "and", maxTitleLength, m.CustomFormat);
            component = ReplaceTokens(component, tokens, config).Trim();

            component = FileNameCleanupRegex().Replace(component, m => m.Captures[0].Value[0].ToString());
            component = TrimSeparatorsRegex().Replace(component, string.Empty);
            component = component.Replace("{ellipsis}", "...", StringComparison.Ordinal);
            components.Add(ReplaceReservedDeviceNames(component));
        }

        return string.Join(Path.DirectorySeparatorChar, components) + extension;
    }

    public static string GetSeriesFolder(Series series, NamingConfig config)
    {
        var tokens = new Dictionary<string, Func<TokenMatch, string?>>(TokenComparer.Instance);
        AddSeriesTokens(tokens, series);
        var folder = CleanFolderName(ReplaceTokens(config.SeriesFolderFormat, tokens, config));
        return ReplaceReservedDeviceNames(folder).Replace("{ellipsis}", "...", StringComparison.Ordinal);
    }

    public static string GetSeasonFolder(Series series, int seasonNumber, string? seasonTitle, NamingConfig config)
    {
        var tokens = new Dictionary<string, Func<TokenMatch, string?>>(TokenComparer.Instance);
        AddSeriesTokens(tokens, series);
        tokens["{Season}"] = m => seasonNumber.ToString(m.CustomFormat, CultureInfo.InvariantCulture);
        tokens["{Season Title}"] = m => string.IsNullOrWhiteSpace(seasonTitle) ? string.Empty : Truncate(seasonTitle, m.CustomFormat);
        var format = seasonNumber == 0 ? config.SpecialsFolderFormat : config.SeasonFolderFormat;
        var folder = CleanFolderName(ReplaceTokens(format, tokens, config)).Trim(' ', '.', '-', '_');
        return ReplaceReservedDeviceNames(folder).Replace("{ellipsis}", "...", StringComparison.Ordinal);
    }

    /// <summary>The series folder, plus the season folder when the series uses them.</summary>
    public static string BuildSeasonPath(Series series, int seasonNumber, string? seasonTitle, NamingConfig config) =>
        series.SeasonFolder ? Path.Combine(series.Path, CleanFileName(GetSeasonFolder(series, seasonNumber, seasonTitle, config), config)) : series.Path;

    public static string BuildFilePath(NamingInput input, NamingConfig config, string extension) =>
        Path.Combine(BuildSeasonPath(input.Series, input.Episodes.Min(e => e.SeasonNumber), input.SeasonTitle, config), BuildFileName(input, config, extension));

    public static string CleanTitle(string title)
    {
        title = title.Replace("&", "and", StringComparison.Ordinal);
        title = ScenifyReplaceChars().Replace(title, " ");
        title = ScenifyRemoveChars().Replace(title, string.Empty);
        return title.RemoveDiacritics();
    }

    public static string TitleYear(string title, int? year) =>
        year is null or 0 || YearRegex().IsMatch(title) ? title : $"{title} ({year})";

    public static string CleanFolderName(string name) =>
        FileNameCleanupRegex().Replace(name, m => m.Captures[0].Value[0].ToString()).Trim(' ', '.');

    public static string CleanFileName(string name, NamingConfig config)
    {
        var result = name;
        if (config.ReplaceIllegalCharacters)
        {
            if (config.ColonReplacementFormat == ColonReplacementFormat.Smart)
            {
                result = result.Replace(": ", " - ", StringComparison.Ordinal).Replace(":", "-", StringComparison.Ordinal);
            }
            else
            {
                var replacement = config.ColonReplacementFormat switch
                {
                    ColonReplacementFormat.Dash => "-",
                    ColonReplacementFormat.SpaceDash => " -",
                    ColonReplacementFormat.SpaceDashSpace => " - ",
                    ColonReplacementFormat.Custom => config.CustomColonReplacementFormat,
                    _ => string.Empty,
                };
                result = result.Replace(":", replacement, StringComparison.Ordinal);
            }
        }
        else
        {
            result = result.Replace(":", string.Empty, StringComparison.Ordinal);
        }

        for (var i = 0; i < BadCharacters.Length; i++)
        {
            result = result.Replace(BadCharacters[i], config.ReplaceIllegalCharacters ? GoodCharacters[i] : string.Empty, StringComparison.Ordinal);
        }

        return result.TrimStart(' ', '.').TrimEnd(' ');
    }

    private static void AddSeriesTokens(Dictionary<string, Func<TokenMatch, string?>> tokens, Series series)
    {
        var title = series.Title;
        tokens["{Series Title}"] = m => Truncate(title, m.CustomFormat);
        tokens["{Series CleanTitle}"] = m => Truncate(CleanTitle(title), m.CustomFormat);
        tokens["{Series TitleYear}"] = m => Truncate(TitleYear(title, series.Year), m.CustomFormat);
        tokens["{Series Year}"] = _ => series.Year?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string AddSeasonEpisodeNumberingTokens(string pattern, Dictionary<string, Func<TokenMatch, string?>> tokens, List<Episode> episodes, NamingConfig config)
    {
        var formats = SeasonEpisodePatternRegex().Matches(pattern)
            .Select(m => new EpisodeFormat(m.Groups["separator"].Value, m.Groups["episodeSeparator"].Value, m.Groups["episode"].Value, m.Groups["seasonEpisode"].Value))
            .DistinctBy(f => f.SeasonEpisodePattern)
            .ToList();

        var index = 1;
        foreach (var format in formats)
        {
            var seasonEpisode = format.SeasonEpisodePattern;
            seasonEpisode = config.MultiEpisodeStyle switch
            {
                MultiEpisodeStyle.Duplicate => FormatNumberTokens(seasonEpisode, format.Separator + format.SeasonEpisodePattern, episodes),
                MultiEpisodeStyle.Repeat => FormatNumberTokens(seasonEpisode, format.EpisodeSeparator + format.EpisodePattern, episodes),
                MultiEpisodeStyle.Scene => FormatNumberTokens(seasonEpisode, "-" + format.EpisodeSeparator + format.EpisodePattern, episodes),
                MultiEpisodeStyle.Range => FormatRangeNumberTokens(seasonEpisode, "-" + format.EpisodePattern, episodes),
                MultiEpisodeStyle.PrefixedRange => FormatRangeNumberTokens(seasonEpisode, "-" + format.EpisodeSeparator + format.EpisodePattern, episodes),
                _ => FormatNumberTokens(seasonEpisode, "-" + format.EpisodePattern, episodes),
            };

            var token = $"{{Season Episode{index++}}}";
            pattern = pattern.Replace(format.SeasonEpisodePattern, token, StringComparison.Ordinal);
            var value = seasonEpisode;
            tokens[token] = _ => value;
        }

        tokens["{Season}"] = m => episodes[0].SeasonNumber.ToString(m.CustomFormat, CultureInfo.InvariantCulture);
        tokens["{Episode}"] = m => episodes.Count > 1
            ? episodes[0].EpisodeNumber.ToString(m.CustomFormat, CultureInfo.InvariantCulture) + "-" + episodes[^1].EpisodeNumber.ToString(m.CustomFormat, CultureInfo.InvariantCulture)
            : episodes[0].EpisodeNumber.ToString(m.CustomFormat, CultureInfo.InvariantCulture);
        return pattern;
    }

    /// <summary>Generral has no anime type: <c>{absolute}</c> renders whenever every episode has an absolute number.</summary>
    private static string AddAbsoluteNumberingTokens(string pattern, Dictionary<string, Func<TokenMatch, string?>> tokens, List<Episode> episodes, NamingConfig config)
    {
        var index = 1;
        foreach (var match in AbsoluteEpisodePatternRegex().Matches(pattern).DistinctBy(m => m.Groups["absolute"].Value).ToList())
        {
            var absolute = match.Groups["absolute"].Value;
            if (episodes.Any(e => e.AbsoluteEpisodeNumber is null))
            {
                pattern = pattern.Replace(absolute, string.Empty, StringComparison.Ordinal);
                continue;
            }

            var separator = match.Groups["separator"].Value is { Length: > 0 } s && !string.IsNullOrWhiteSpace(s) ? s : "-";
            var range = config.MultiEpisodeStyle is MultiEpisodeStyle.Range or MultiEpisodeStyle.PrefixedRange;
            var formatPattern = config.MultiEpisodeStyle == MultiEpisodeStyle.Duplicate ? separator + absolute : "-" + absolute;
            var numbered = range && episodes.Count > 1 ? [episodes[0], episodes[^1]] : episodes;
            var value = string.Concat(numbered.Select((e, i) => AbsoluteEpisodeRegex().Replace(i == 0 ? absolute : formatPattern, m => NumberToken(m.Value, e.AbsoluteEpisodeNumber!.Value))));

            var token = $"{{Absolute Pattern{index++}}}";
            pattern = pattern.Replace(absolute, token, StringComparison.Ordinal);
            tokens[token] = _ => value;
        }

        return pattern;
    }

    private static void AddEpisodeTokens(Dictionary<string, Func<TokenMatch, string?>> tokens, List<Episode> episodes)
    {
        var airDate = episodes[0].AirDate?.ToString("yyyy MM dd", CultureInfo.InvariantCulture) ?? "Unknown";
        tokens["{Air Date}"] = _ => airDate;
    }

    private static void AddEpisodeFileTokens(Dictionary<string, Func<TokenMatch, string?>> tokens, EpisodeFile file)
    {
        tokens["{Original Title}"] = _ => OriginalTitle(file);
        tokens["{Original Filename}"] = _ => OriginalFileName(file);
        tokens["{Release Group}"] = m => string.IsNullOrWhiteSpace(file.ReleaseGroup) ? m.DefaultValue("Generral") : Truncate(file.ReleaseGroup, m.CustomFormat);

        // Release profiles have no preferred-word scores in Generral.
        tokens["{Preferred Words}"] = _ => string.Empty;
    }

    private static void AddQualityTokens(Dictionary<string, Func<TokenMatch, string?>> tokens, NamingInput input)
    {
        var quality = input.File.Quality;
        var title = input.QualityTitles.GetValueOrDefault(quality.Quality.Id) is { Length: > 0 } t ? t : quality.Quality.Name;
        var proper = quality.Revision.Version > 1 ? "Proper" : string.Empty;
        var real = quality.Revision.Real > 0 ? "REAL" : string.Empty;
        tokens["{Quality Full}"] = _ => $"{title} {proper} {real}";
        tokens["{Quality Title}"] = _ => title;
    }

    private static void AddGenerralTokens(Dictionary<string, Func<TokenMatch, string?>> tokens, NamingInput input, List<Episode> episodes)
    {
        var infos = episodes.Select(e => input.Info.GetValueOrDefault(e.Id)).ToList();
        var first = infos[0];
        tokens["{Part Title}"] = m => Truncate(string.Join(" + ", infos.Select(i => i?.PartTitle).OfType<string>().Distinct()), m.CustomFormat);
        tokens["{Part Key}"] = _ => string.Join("+", infos.Select(i => i?.PartKey).OfType<string>().Distinct());
        tokens["{Event Key}"] = _ => first?.EventKey ?? string.Empty;
        tokens["{Generral Id}"] = _ => $"generral-ep-{episodes[0].Id}";
        tokens["{Event}"] = m => m.CustomFormat is { } field && first?.EventFields?[field] is { } value ? FieldValues.ToText(FieldValues.FromJson(value)) ?? string.Empty : string.Empty;
    }

    private static string ReplaceTokens(string pattern, Dictionary<string, Func<TokenMatch, string?>> tokens, NamingConfig config, bool escape = false) =>
        TitleRegex().Replace(pattern, match => ReplaceToken(match, tokens, config, escape));

    private static string ReplaceToken(Match match, Dictionary<string, Func<TokenMatch, string?>> tokens, NamingConfig config, bool escape)
    {
        if (match.Groups["escaped"].Success)
        {
            return escape ? match.Value : match.Value == "{{" ? "{" : "}";
        }

        var token = new TokenMatch(
            match.Groups["prefix"].Value,
            match.Groups["separator"].Value,
            match.Groups["suffix"].Value,
            match.Groups["token"].Value,
            match.Groups["customFormat"].Value is { Length: > 0 } format && !string.IsNullOrWhiteSpace(format) ? format : null);

        // The Generral {Event:<field>} token keeps the field name's case.
        var handler = tokens.GetValueOrDefault(token.Token, _ => string.Empty);
        var replacement = handler(token);
        if (replacement is null)
        {
            return match.Value;
        }

        replacement = replacement.Trim();
        if (token.Token.All(c => !char.IsLetter(c) || char.IsLower(c)))
        {
            replacement = replacement.ToLowerInvariant();
        }
        else if (token.Token.All(c => !char.IsLetter(c) || char.IsUpper(c)))
        {
            replacement = replacement.ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(token.Separator))
        {
            replacement = replacement.Replace(" ", token.Separator, StringComparison.Ordinal);
        }

        replacement = CleanFileName(replacement, config);
        if (!string.IsNullOrWhiteSpace(replacement))
        {
            replacement = token.Prefix + replacement + token.Suffix;
        }

        return escape ? replacement.Replace("{", "{{", StringComparison.Ordinal).Replace("}", "}}", StringComparison.Ordinal) : replacement;
    }

    private static string FormatNumberTokens(string basePattern, string formatPattern, List<Episode> episodes)
    {
        var pattern = string.Concat(episodes.Select((e, i) => EpisodeRegex().Replace(i == 0 ? basePattern : formatPattern, m => NumberToken(m.Value, e.EpisodeNumber))));
        return SeasonRegex().Replace(pattern, m => NumberToken(m.Value, episodes[0].SeasonNumber));
    }

    private static string FormatRangeNumberTokens(string seasonEpisodePattern, string formatPattern, List<Episode> episodes) =>
        FormatNumberTokens(seasonEpisodePattern, formatPattern, episodes.Count > 1 ? [episodes[0], episodes[^1]] : episodes);

    private static string NumberToken(string token, int value)
    {
        var split = token.Trim('{', '}').Split(':');
        return value.ToString(split.Length == 1 ? "0" : split[1], CultureInfo.InvariantCulture);
    }

    private static List<string> EpisodeTitles(List<Episode> episodes)
    {
        if (episodes.Count == 1)
        {
            return [episodes[0].Title.TrimEnd(EpisodeTitleTrimCharacters)];
        }

        var titles = episodes.Select(e => MultiPartCleanupRegex().Replace(e.Title.TrimEnd(EpisodeTitleTrimCharacters), string.Empty).Trim()).Distinct().ToList();
        return titles.All(string.IsNullOrWhiteSpace) ? episodes.Select(e => e.Title.TrimEnd(EpisodeTitleTrimCharacters)).Distinct().ToList() : titles;
    }

    private static string EpisodeTitle(List<string> titles, string separator, int maxLength, string? formatter)
    {
        if (int.TryParse(formatter, CultureInfo.InvariantCulture, out var max) && max > 0)
        {
            maxLength = Math.Min(maxLength, max);
        }

        var joined = string.Join($" {separator.Trim()} ", titles);
        if (Encoding.UTF8.GetByteCount(joined) <= maxLength)
        {
            return joined;
        }

        var first = titles[0];
        var firstLength = Encoding.UTF8.GetByteCount(first);
        if (titles.Count >= 2 && firstLength + Encoding.UTF8.GetByteCount(titles[^1]) + 3 <= maxLength)
        {
            return $"{first.TrimEnd(' ', '.')}{{ellipsis}}{titles[^1]}";
        }

        if (titles.Count > 1 && firstLength + 3 <= maxLength)
        {
            return $"{first.TrimEnd(' ', '.')}{{ellipsis}}";
        }

        return titles.Count == 1 && firstLength <= maxLength ? first : $"{first[..Math.Clamp(maxLength - 3, 0, first.Length)].TrimEnd(' ', '.')}{{ellipsis}}";
    }

    private static int LengthWithoutEpisodeTitle(string pattern, NamingConfig config)
    {
        var tokens = new Dictionary<string, Func<TokenMatch, string?>>(TokenComparer.Instance)
        {
            ["{Episode Title}"] = _ => string.Empty,
            ["{Episode CleanTitle}"] = _ => string.Empty,
            ["{ellipsis}"] = _ => "...",
        };
        return Encoding.UTF8.GetByteCount(ReplaceTokens(pattern, tokens, config));
    }

    private static string OriginalTitle(EpisodeFile file) => string.IsNullOrWhiteSpace(file.SceneName) ? OriginalFileName(file) : file.SceneName;

    private static string OriginalFileName(EpisodeFile file) =>
        Path.GetFileNameWithoutExtension(file.OriginalFilePath ?? (string.IsNullOrWhiteSpace(file.RelativePath) ? file.Path : file.RelativePath));

    private static string Truncate(string? input, string? formatter)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        _ = int.TryParse(formatter, CultureInfo.InvariantCulture, out var max);
        if (max == 0 || input.Length <= Math.Abs(max))
        {
            return input;
        }

        return max < 0
            ? $"{{ellipsis}}{input[(input.Length - (Math.Abs(max) - 3))..].TrimStart(' ', '.')}"
            : $"{input[..(max - 3)].TrimEnd(' ', '.')}{{ellipsis}}";
    }

    private static string ReplaceReservedDeviceNames(string input) => ReservedDeviceNamesRegex().Replace(input, m => m.Value.Replace('.', '_'));

    private sealed record EpisodeFormat(string Separator, string EpisodeSeparator, string EpisodePattern, string SeasonEpisodePattern);

    private sealed record TokenMatch(string Prefix, string Separator, string Suffix, string Token, string? CustomFormat)
    {
        public string DefaultValue(string value) => string.IsNullOrEmpty(Prefix) && string.IsNullOrEmpty(Suffix) ? value : string.Empty;
    }

    private sealed partial class TokenComparer : IEqualityComparer<string>
    {
        public static readonly TokenComparer Instance = new();

        public bool Equals(string? x, string? y) => Simplify(x) == Simplify(y);

        public int GetHashCode(string obj) => Simplify(obj).GetHashCode(StringComparison.Ordinal);

        private static string Simplify(string? token) => SimpleTokenRegex().Replace(token ?? string.Empty, string.Empty).ToLowerInvariant();

        [GeneratedRegex(@"\s|_|\W", RegexOptions.IgnoreCase)]
        private static partial Regex SimpleTokenRegex();
    }

    /// <summary>Sonarr's, with <c>_</c> allowed in the custom format so <c>{Event:field_name}</c> works.</summary>
    [GeneratedRegex(@"(?<escaped>\{\{|\}\})|\{(?<prefix>[- ._\[(]*)(?<token>(?:[a-z0-9]+)(?:(?<separator>[- ._]+)(?:[a-z0-9]+))?)(?::(?<customFormat>[ ,a-z0-9_+-]+(?<![- ])))?(?<suffix>[- ._)\]]*)\}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TitleRegex();

    [GeneratedRegex(@"(?<episode>\{episode(?:\:0+)?})", RegexOptions.IgnoreCase)]
    private static partial Regex EpisodeRegex();

    [GeneratedRegex(@"(?<season>\{season(?:\:0+)?})", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonRegex();

    [GeneratedRegex(@"(?<absolute>\{absolute(?:\:0+)?})", RegexOptions.IgnoreCase)]
    private static partial Regex AbsoluteEpisodeRegex();

    [GeneratedRegex(@"(?<separator>(?<=})[- ._]+?)?(?<seasonEpisode>s?{season(?:\:0+)?}(?<episodeSeparator>[- ._]?[ex])(?<episode>{episode(?:\:0+)?}))(?<separator>[- ._]+?(?={))?", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonEpisodePatternRegex();

    [GeneratedRegex(@"(?<separator>(?<=})[- ._]+?)?(?<absolute>{absolute(?:\:0+)?})(?<separator>[- ._]+?(?={))?", RegexOptions.IgnoreCase)]
    private static partial Regex AbsoluteEpisodePatternRegex();

    [GeneratedRegex(@"\{Air(\s|\W|_)Date\}", RegexOptions.IgnoreCase)]
    private static partial Regex AirDateRegex();

    [GeneratedRegex(@"([- ._])(\1)+")]
    private static partial Regex FileNameCleanupRegex();

    [GeneratedRegex(@"[- ._]+$")]
    private static partial Regex TrimSeparatorsRegex();

    [GeneratedRegex(@"(?<=\s)(,|<|>|\/|\\|;|:|'|""|\||`|’|~|!|\?|@|$|%|^|\*|-|_|=){1}(?=\s)|('|`|’|:|\?|,)(?=(?:(?:s|m|t|ve|ll|d|re)\s)|\s|$)|(\(|\)|\[|\]|\{|\})", RegexOptions.IgnoreCase)]
    private static partial Regex ScenifyRemoveChars();

    [GeneratedRegex(@"[\/]", RegexOptions.IgnoreCase)]
    private static partial Regex ScenifyReplaceChars();

    [GeneratedRegex(@"(?:\:?\s?(?:\(\d+\)|(Part|Pt\.?)\s?\d+))$", RegexOptions.IgnoreCase)]
    private static partial Regex MultiPartCleanupRegex();

    [GeneratedRegex(@"\(\d{4}\)$", RegexOptions.IgnoreCase)]
    private static partial Regex YearRegex();

    [GeneratedRegex(@"^(?:aux|com[1-9]|con|lpt[1-9]|nul|prn)\.", RegexOptions.IgnoreCase)]
    private static partial Regex ReservedDeviceNamesRegex();
}
