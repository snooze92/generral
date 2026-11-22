// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Profiles/Releases/ReleaseProfile.cs
namespace Generral.Core.Profiles;

/// <summary>Must contain / must not contain terms (plain text or <c>/regex/i</c>), scoped by tags and optionally one indexer.</summary>
public class ReleaseProfile
{
    public int Id { get; set; }

    public string? Name { get; set; }

    public bool Enabled { get; set; } = true;

    public List<string> Required { get; set; } = [];

    public List<string> Ignored { get; set; } = [];

    /// <summary>0 for all indexers.</summary>
    public int IndexerId { get; set; }

    public List<int> Tags { get; set; } = [];

    public List<int> ExcludedTags { get; set; } = [];
}
