// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Qualities/QualityDefinition.cs and src/NzbDrone.Core/Qualities/QualityDefinitionLimits.cs
namespace Generral.Core.Qualities;

/// <summary>Size limits (MB per minute of runtime) and display title of a quality. <see cref="Weight"/> is not stored.</summary>
public class QualityDefinition
{
    public const int MinLimit = 0;
    public const int MaxLimit = 1000;

    public QualityDefinition()
    {
    }

    public QualityDefinition(Quality quality)
    {
        Quality = quality;
        Title = quality.Name;
    }

    public int Id { get; set; }

    public Quality Quality { get; set; } = Quality.Unknown;

    public string Title { get; set; } = string.Empty;

    public string? GroupName { get; set; }

    public int Weight { get; set; }

    public double? MinSize { get; set; }

    public double? MaxSize { get; set; }

    public double? PreferredSize { get; set; }
}
