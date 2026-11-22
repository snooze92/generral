// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/MediaFiles/MediaFileExtensions.cs
namespace Generral.Core.Qualities;

public static class MediaFileExtensions
{
    private static readonly Dictionary<string, Quality> FileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".webm", Quality.Unknown },

        { ".m4v", Quality.SDTV },
        { ".3gp", Quality.SDTV },
        { ".nsv", Quality.SDTV },
        { ".ty", Quality.SDTV },
        { ".strm", Quality.SDTV },
        { ".rm", Quality.SDTV },
        { ".rmvb", Quality.SDTV },
        { ".m3u", Quality.SDTV },
        { ".ifo", Quality.SDTV },
        { ".mov", Quality.SDTV },
        { ".qt", Quality.SDTV },
        { ".divx", Quality.SDTV },
        { ".xvid", Quality.SDTV },
        { ".bivx", Quality.SDTV },
        { ".nrg", Quality.SDTV },
        { ".pva", Quality.SDTV },
        { ".wmv", Quality.SDTV },
        { ".asf", Quality.SDTV },
        { ".asx", Quality.SDTV },
        { ".ogm", Quality.SDTV },
        { ".ogv", Quality.SDTV },
        { ".m2v", Quality.SDTV },
        { ".avi", Quality.SDTV },
        { ".bin", Quality.SDTV },
        { ".dat", Quality.SDTV },
        { ".dvr-ms", Quality.SDTV },
        { ".mpg", Quality.SDTV },
        { ".mpeg", Quality.SDTV },
        { ".mp4", Quality.SDTV },
        { ".avc", Quality.SDTV },
        { ".vp3", Quality.SDTV },
        { ".svq3", Quality.SDTV },
        { ".nuv", Quality.SDTV },
        { ".viv", Quality.SDTV },
        { ".dv", Quality.SDTV },
        { ".fli", Quality.SDTV },
        { ".flv", Quality.SDTV },
        { ".wpl", Quality.SDTV },

        { ".img", Quality.DVD },
        { ".iso", Quality.DVD },
        { ".vob", Quality.DVD },

        { ".mkv", Quality.HDTV720p },
        { ".ts", Quality.HDTV720p },
        { ".wtv", Quality.HDTV720p },

        { ".m2ts", Quality.Bluray720p },
    };

    public static Quality GetQualityForExtension(string extension) =>
        FileExtensions.TryGetValue(extension, out var quality) ? quality : Quality.Unknown;
}
