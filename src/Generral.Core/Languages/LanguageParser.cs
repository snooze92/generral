// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Parser/LanguageParser.cs (release titles only) and Parser.cs (HasMultipleLanguages)
using System.Text.RegularExpressions;

namespace Generral.Core.Languages;

public static class LanguageParser
{
    private static readonly Regex CleanSeriesTitleRegex = new(@".*?[_. ](S\d{2}(?:E\d{2,4})*[_. ].*)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LanguagesOnlyRegex = new(@"(?<english>\b(?:ing|eng)\b)|(?<italian>\b(?:ita|italian)\b)|(?<german>(?:swiss)?german\b|videomann|ger[. ]dub|\bger\b)|(?<flemish>flemish)|(?<greek>greek)|(?<french>(?:_|\b)(?:FR|VF|VF2|VFF|VFI|VFQ|TRUEFRENCH|FRENCH|FRE|FRA)(?:_|\b))|(?<russian>\b(?:rus|ru)\b)|(?<hungarian>\b(?:HUNDUB|HUN)\b)|(?<hebrew>\bHebDub\b)|(?<polish>\b(?:PL\W?DUB|DUB\W?PL|LEK\W?PL|PL\W?LEK)\b)|(?<chinese>\[(?:CH[ST]|BIG5|GB)\]|简|繁|字幕|国语音轨[.+])|(?<bulgarian>\bbgaudio\b)|(?<spanish>\b(?:español|castellano|esp|spa(?!\(Latino\)))\b)|(?<ukrainian>\b(?:\dx?)?(?:ukr))|(?<thai>\b(?:THAI)\b)|(?<romanian>\b(?:RoDubbed|ROMANIAN)\b)|(?<catalan>[-,. ]cat[. ](?:DD|subs)|\b(?:catalan|catalán)\b)|(?<latvian>\b(?:lat|lav|lv)\b)|(?<turkish>\b(?:tur)\b)|(?<urdu>\burdu\b)|(?<romansh>\b(?:romansh|rumantsch|romansch)\b)|(?<georgian>\b(?:geo|ka|kat|georgian)\b)|(?<irish>\b(?:irish|gaeilge)\b)|(?<japanese>\(JA\)|JAP|JPN)|(?<portuguese>[_. ]por[_. ])|(?<original>\b(?:orig|original)\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LanguageRegex = new(@$"(?:{LanguagesOnlyRegex})(?!(?:[-_. ](?:{LanguagesOnlyRegex}))*[-_. ]subs?)",
                                                                RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CaseSensitiveLanguageRegex = new(@"(?:(?i)(?<!SUB[\W|_|^]))(?:(?<lithuanian>\bLT\b)|(?<czech>\bCZ\b)|(?<polish>\bPL\b)|(?<bulgarian>\bBG\b)|(?<slovak>\bSK\b)|(?<german>\bDE\b))(?:(?i)(?![\W|_|^]SUB))",
                                                                RegexOptions.Compiled);

    private static readonly Regex GermanDualLanguageRegex = new(@"(?<!WEB[-_. ]?)\bDL\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex GermanMultiLanguageRegex = new(@"\bML\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MultiRegex = new(@"[-_. \[](?<multi>multi|multilang|multilanguage)[-_. \]]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly (string[] Words, Language Language)[] Words =
    [
        (["spanish"], Language.Spanish),
        (["danish"], Language.Danish),
        (["dutch"], Language.Dutch),
        (["japanese"], Language.Japanese),
        (["icelandic"], Language.Icelandic),
        (["mandarin", "cantonese", "chinese"], Language.Chinese),
        (["korean"], Language.Korean),
        (["russian"], Language.Russian),
        (["polish"], Language.Polish),
        (["vietnamese"], Language.Vietnamese),
        (["swedish"], Language.Swedish),
        (["norwegian"], Language.Norwegian),
        (["finnish"], Language.Finnish),
        (["turkish"], Language.Turkish),
        (["portuguese"], Language.Portuguese),
        (["hungarian"], Language.Hungarian),
        (["hebrew"], Language.Hebrew),
        (["arabic"], Language.Arabic),
        (["hindi"], Language.Hindi),
        (["malayalam"], Language.Malayalam),
        (["ukrainian"], Language.Ukrainian),
        (["bulgarian"], Language.Bulgarian),
        (["slovak"], Language.Slovak),
        (["brazilian", "dublado"], Language.PortugueseBrazil),
        (["latino"], Language.SpanishLatino),
        (["latvian"], Language.Latvian),
        (["azerbaijani", "azerbaijan"], Language.Azerbaijani),
        (["uzbek", "uzbekistan"], Language.Uzbek),
    ];

    private static readonly (string Group, Language Language)[] CaseSensitiveGroups =
    [
        ("lithuanian", Language.Lithuanian),
        ("czech", Language.Czech),
        ("polish", Language.Polish),
        ("bulgarian", Language.Bulgarian),
        ("slovak", Language.Slovak),
        ("german", Language.German),
    ];

    private static readonly (string Group, Language Language)[] Groups =
    [
        ("english", Language.English),
        ("italian", Language.Italian),
        ("german", Language.German),
        ("flemish", Language.Flemish),
        ("greek", Language.Greek),
        ("french", Language.French),
        ("russian", Language.Russian),
        ("dutch", Language.Dutch),
        ("hungarian", Language.Hungarian),
        ("hebrew", Language.Hebrew),
        ("polish", Language.Polish),
        ("chinese", Language.Chinese),
        ("bulgarian", Language.Bulgarian),
        ("ukrainian", Language.Ukrainian),
        ("spanish", Language.Spanish),
        ("thai", Language.Thai),
        ("romanian", Language.Romanian),
        ("catalan", Language.Catalan),
        ("latvian", Language.Latvian),
        ("turkish", Language.Turkish),
        ("urdu", Language.Urdu),
        ("romansh", Language.Romansh),
        ("georgian", Language.Georgian),
        ("irish", Language.Irish),
        ("japanese", Language.Japanese),
        ("portuguese", Language.Portuguese),
        ("original", Language.Original),
    ];

    /// <summary>The languages tagged in a release title; <see cref="Language.Unknown"/> alone when there is no tag.</summary>
    public static List<Language> ParseLanguages(string title)
    {
        title = CleanSeriesTitleRegex.Replace(title, "$1");
        var lowerTitle = title.ToLowerInvariant();
        var languages = Words.Where(w => w.Words.Any(lowerTitle.Contains)).Select(w => w.Language).ToList();

        var caseSensitive = CaseSensitiveLanguageRegex.Match(title);
        languages.AddRange(CaseSensitiveGroups.Where(g => caseSensitive.Groups[g.Group].Success).Select(g => g.Language));
        foreach (Match match in LanguageRegex.Matches(title))
        {
            languages.AddRange(Groups.Where(g => match.Groups[g.Group].Success).Select(g => g.Language));
        }

        if (lowerTitle.Contains("english"))
        {
            languages.Add(Language.English);
        }

        if (languages.Count == 0)
        {
            languages.Add(Language.Unknown);
        }

        if (languages.Count == 1 && languages[0] == Language.German)
        {
            if (GermanDualLanguageRegex.IsMatch(title))
            {
                languages.Add(Language.Original);
            }
            else if (GermanMultiLanguageRegex.IsMatch(title))
            {
                languages.Add(Language.Original);
                languages.Add(Language.English);
            }
        }

        return languages.Distinct().ToList();
    }

    /// <summary><c>MULTi</c>, <c>multilang</c>…: several audio tracks, the original among them.</summary>
    public static bool HasMultipleLanguages(string title) => MultiRegex.IsMatch(title);
}
