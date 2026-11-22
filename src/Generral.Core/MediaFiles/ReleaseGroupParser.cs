// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Parser/ReleaseGroupParser.cs (the main pattern, without anime and exception groups)
using System.Text.RegularExpressions;

namespace Generral.Core.MediaFiles;

public static partial class ReleaseGroupParser
{
    public static string? Parse(string title)
    {
        title = title.Trim();
        if (Path.GetExtension(title) is { Length: > 1 and <= 5 } extension && extension.Skip(1).All(char.IsAsciiLetterOrDigit))
        {
            title = title[..^extension.Length];
        }

        title = CleanReleaseGroupRegex().Replace(title, string.Empty);
        var matches = ReleaseGroupRegex().Matches(title);
        if (matches.Count == 0)
        {
            return null;
        }

        var group = matches[^1].Groups["releasegroup"].Value;
        return int.TryParse(group, out _) || InvalidReleaseGroupRegex().IsMatch(group) ? null : group;
    }

    [GeneratedRegex(@"-(?<releasegroup>[A-Za-zÀ-ÖØ-öø-ÿ0-9]+(?<part2>-[A-Za-zÀ-ÖØ-öø-ÿ0-9]+)?(?!.+?(?:HDTV|SDTV|480p|576p|720p|1080p|2160p)))(?<!(?:HDTV|SDTV|WEB-DL|Blu-Ray|480p|576p|720p|1080p|2160p|DTS-HD|DTS-X|DTS-MA|DTS-ES|-ES|-EN|-CAT|-GER|-FRA|-FRE|-ITA|\d{1,2}-bit|[ ._]\d{4}-\d{2}|-\d{2})(?:\k<part2>)?)(?:\b|[-._ ]|$)|[-._ ]\[(?<releasegroup>[A-Za-zÀ-ÖØ-öø-ÿ0-9]+)\]$", RegexOptions.IgnoreCase)]
    private static partial Regex ReleaseGroupRegex();

    [GeneratedRegex(@"^([se]\d+|[0-9a-f]{8})$", RegexOptions.IgnoreCase)]
    private static partial Regex InvalidReleaseGroupRegex();

    [GeneratedRegex(@"(-(RP|1|NZBGeek|Obfuscated|Scrambled|sample|Pre|postbot|xpost|Rakuv[a-z0-9]*|WhiteRev|BUYMORE|AsRequested|AlternativeToRequested|GEROV|Z0iDS3N|Chamele0n|4P|4Planet|AlteZachen|RePACKPOST))+$", RegexOptions.IgnoreCase)]
    private static partial Regex CleanReleaseGroupRegex();
}
