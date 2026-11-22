// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Indexers/IndexerDefinition.cs
using System.Text.Json.Nodes;

namespace Generral.Core.Indexers;

public class Indexer
{
    public const int DefaultPriority = 25;

    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary><c>Torznab</c> or <c>Newznab</c> (stored for Prowlarr sync, not searched until Usenet support lands).</summary>
    public required string Implementation { get; set; }

    /// <summary>Settings keyed by Sonarr field name (e.g. <c>seedCriteria.seedRatio</c>). Unknown fields are kept.</summary>
    public JsonObject Settings { get; set; } = [];

    public bool EnableRss { get; set; } = true;

    public bool EnableAutomaticSearch { get; set; } = true;

    public bool EnableInteractiveSearch { get; set; } = true;

    public int Priority { get; set; } = DefaultPriority;

    /// <summary>Pinned download client, 0 for any.</summary>
    public int DownloadClientId { get; set; }

    public int SeasonSearchMaximumSingleEpisodeAge { get; set; }

    public List<int> Tags { get; set; } = [];

    public bool Enable => EnableRss || EnableAutomaticSearch || EnableInteractiveSearch;
}
