// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Parser/Parser.cs (CleanSeriesTitle, NormalizeTitle)
// and src/NzbDrone.Core/Organizer/FileNameBuilder.cs (CleanFileName, CleanFolderName)
using System.Text.RegularExpressions;
using Generral.Core.Extensions;

namespace Generral.Core.Tv;

/// <summary>The derived title forms of a series.</summary>
public static partial class SeriesTitles
{
    private static readonly string[] BadCharacters = ["\\", "/", "<", ">", "?", "*", "|", "\""];
    private static readonly string[] GoodCharacters = ["+", "+", "", "", "!", "-", "", ""];

    /// <summary>Used for matching: lower case, no articles, conjunctions, punctuation or spaces.</summary>
    public static string Clean(string title)
    {
        if (string.IsNullOrWhiteSpace(title) || long.TryParse(title, out _))
        {
            return title;
        }

        title = PercentRegex().Replace(title, "percent");
        return NormalizeRegex().Replace(title, string.Empty).ToLowerInvariant().RemoveDiacritics();
    }

    /// <summary>Lower case without punctuation or a leading article.</summary>
    public static string Sort(string title)
    {
        title = PunctuationRegex().Replace(title, string.Empty);
        title = ArticleWordRegex().Replace(title, string.Empty);
        title = DuplicateSpacesRegex().Replace(title, " ");
        return title.Trim().ToLowerInvariant();
    }

    /// <summary>The default series folder name (spec 08): <c>{Series Title}</c>, or <c>{Series Title} ({Series Year})</c>.</summary>
    public static string FolderName(string title, int? year)
    {
        var name = year is { } y ? $"{title} ({y})" : title;
        name = name.Replace(": ", " - ").Replace(":", "-");
        for (var i = 0; i < BadCharacters.Length; i++)
        {
            name = name.Replace(BadCharacters[i], GoodCharacters[i]);
        }

        name = FileNameCleanupRegex().Replace(name, m => m.Captures[0].Value[0].ToString());
        return name.Trim(' ', '.');
    }

    [GeneratedRegex(@"((?:\b|_)(?<!^)([aà](?!$)|an|the|and|or|of)(?!$)(?:\b|_))|\W|_", RegexOptions.IgnoreCase)]
    private static partial Regex NormalizeRegex();

    [GeneratedRegex(@"(?<=\b\d+)%")]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"[^\w\s]")]
    private static partial Regex PunctuationRegex();

    [GeneratedRegex(@"^(a|an|the)\s", RegexOptions.IgnoreCase)]
    private static partial Regex ArticleWordRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex DuplicateSpacesRegex();

    [GeneratedRegex(@"([- ._])(\1)+")]
    private static partial Regex FileNameCleanupRegex();
}
