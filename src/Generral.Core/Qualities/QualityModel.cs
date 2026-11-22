// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Qualities/QualityModel.cs, src/NzbDrone.Core/Qualities/Revision.cs
// and src/NzbDrone.Core/Qualities/QualityDetectionSource.cs
namespace Generral.Core.Qualities;

public enum QualityDetectionSource
{
    Unknown,
    Name,
    Extension,
    MediaInfo,
}

/// <summary>Proper/repack revision: <see cref="Real"/> wins over <see cref="Version"/>.</summary>
public sealed class Revision : IComparable<Revision>
{
    public int Version { get; set; } = 1;

    public int Real { get; set; }

    public bool IsRepack { get; set; }

    public int CompareTo(Revision? other)
    {
        if (other is null)
        {
            return 1;
        }

        return Real != other.Real ? Real.CompareTo(other.Real) : Version.CompareTo(other.Version);
    }

    public override string ToString() => Real > 0 ? $"v{Version} Real:{Real}" : $"v{Version}";
}

public sealed class QualityModel
{
    public Quality Quality { get; set; } = Quality.Unknown;

    public Revision Revision { get; set; } = new();

    public QualityDetectionSource SourceDetectionSource { get; set; }

    public QualityDetectionSource ResolutionDetectionSource { get; set; }

    public QualityDetectionSource RevisionDetectionSource { get; set; }

    public override string ToString() => $"{Quality} {Revision}";
}
