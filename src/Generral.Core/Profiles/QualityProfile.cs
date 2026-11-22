// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Profiles/Qualities/QualityProfile.cs (Language: Radarr's),
// src/NzbDrone.Core/Profiles/Qualities/QualityProfileQualityItem.cs and src/NzbDrone.Core/Profiles/Qualities/QualityIndex.cs
using System.Text.Json.Serialization;
using Generral.Core.Languages;
using Generral.Core.Qualities;

namespace Generral.Core.Profiles;

public class QualityProfile
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public bool UpgradeAllowed { get; set; }

    /// <summary>A quality id or a group id (groups are numbered from 1000).</summary>
    public int Cutoff { get; set; }

    /// <summary>Lowest to highest preference; groups have an <c>Id</c>, a <c>Name</c> and <c>Items</c> instead of a <c>Quality</c>.</summary>
    public List<QualityProfileQualityItem> Items { get; set; } = [];

    /// <summary>The wanted audio language (Radarr-style), or <see cref="Language.Any"/> / <see cref="Language.Original"/>.</summary>
    public Language Language { get; set; } = Language.English;

    /// <summary>Index of the item holding <paramref name="qualityId"/> (or the group with that id), -1 when absent.</summary>
    public (int Index, int GroupIndex) GetIndex(int qualityId)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            if (item.Quality?.Id == qualityId || (item.Id > 0 && item.Id == qualityId))
            {
                return (i, -1);
            }

            var groupIndex = item.Items.FindIndex(g => g.Quality?.Id == qualityId);
            if (groupIndex >= 0)
            {
                return (i, groupIndex);
            }
        }

        return (-1, -1);
    }
}

public class QualityProfileQualityItem
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Id { get; set; }

    public string? Name { get; set; }

    public Quality? Quality { get; set; }

    public List<QualityProfileQualityItem> Items { get; set; } = [];

    public bool Allowed { get; set; }

    public double? MinSize { get; set; }

    public double? MaxSize { get; set; }

    public double? PreferredSize { get; set; }

    public IEnumerable<Quality> GetQualities() => Quality is null ? Items.Select(i => i.Quality!) : [Quality];
}
