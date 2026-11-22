// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Organizer/NamingConfig.cs and FileNameBuilder.cs (MultiEpisodeStyle, ColonReplacementFormat)
using System.Text.Json.Serialization;
using Generral.Core.Configuration;
using Generral.Core.Datastore;

namespace Generral.Core.Organizer;

public enum MultiEpisodeStyle
{
    Extend = 0,
    Duplicate = 1,
    Repeat = 2,
    Scene = 3,
    Range = 4,
    PrefixedRange = 5,
}

public enum ColonReplacementFormat
{
    Delete = 0,
    Dash = 1,
    SpaceDash = 2,
    SpaceDashSpace = 3,
    Smart = 4,
    Custom = 5,
}

/// <summary>Spec 08 § Naming defaults. Enums are numbers in JSON, like Sonarr's resource.</summary>
public sealed record NamingConfig
{
    public bool RenameEpisodes { get; init; } = true;

    public bool ReplaceIllegalCharacters { get; init; } = true;

    [JsonConverter(typeof(JsonNumberEnumConverter<ColonReplacementFormat>))]
    public ColonReplacementFormat ColonReplacementFormat { get; init; } = ColonReplacementFormat.Smart;

    public string CustomColonReplacementFormat { get; init; } = "";

    [JsonConverter(typeof(JsonNumberEnumConverter<MultiEpisodeStyle>))]
    public MultiEpisodeStyle MultiEpisodeStyle { get; init; } = MultiEpisodeStyle.PrefixedRange;

    public string StandardEpisodeFormat { get; init; } = "{Series Title} - S{season:00}E{episode:00} - {Episode Title} [{Quality Full}]";

    public string DailyEpisodeFormat { get; init; } = "{Series Title} - {Air-Date} - {Episode Title} [{Quality Full}]";

    /// <summary>Series folders are created with this when a series is added (spec 08 also suggests the year when known).</summary>
    public string SeriesFolderFormat { get; init; } = "{Series TitleYear}";

    public string SeasonFolderFormat { get; init; } = "Season {season}";

    public string SpecialsFolderFormat { get; init; } = "Specials";

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(StandardEpisodeFormat) || string.IsNullOrWhiteSpace(DailyEpisodeFormat))
        {
            return "Episode formats cannot be empty";
        }

        if (!FileNameBuilder.HasSeasonEpisode(StandardEpisodeFormat))
        {
            return "Standard episode format must contain season and episode numbers (S{season:00}E{episode:00})";
        }

        if (!FileNameBuilder.HasSeasonEpisode(DailyEpisodeFormat) && !FileNameBuilder.HasAirDate(DailyEpisodeFormat))
        {
            return "Daily episode format must contain the air date or season and episode numbers";
        }

        if (string.IsNullOrWhiteSpace(SeriesFolderFormat) || string.IsNullOrWhiteSpace(SeasonFolderFormat))
        {
            return "Folder formats cannot be empty";
        }

        return FileNameBuilder.HasSeason(SeasonFolderFormat) ? null : "Season folder format must contain the season number";
    }
}

public sealed class NamingConfigService(GenerralDbContext db) : JsonConfigService<NamingConfig>(db, "naming")
{
    protected override string? Validate(NamingConfig config) => config.Validate();
}
