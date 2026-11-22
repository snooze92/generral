// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Blocklisting/Blocklist.cs and src/NzbDrone.Core/Parser/ReleaseComparer.cs
using Generral.Core.Indexers;
using Generral.Core.Qualities;

namespace Generral.Core.Blocklisting;

public class Blocklist
{
    public int Id { get; set; }

    public int SeriesId { get; set; }

    public List<int> EpisodeIds { get; set; } = [];

    public required string SourceTitle { get; set; }

    public QualityModel Quality { get; set; } = new();

    public DateTime Date { get; set; }

    public DateTime? PublishedDate { get; set; }

    public long? Size { get; set; }

    public DownloadProtocol Protocol { get; set; }

    public string? Indexer { get; set; }

    public IndexerFlags IndexerFlags { get; set; }

    public string? Message { get; set; }

    public string? Source { get; set; }

    public string? TorrentInfoHash { get; set; }

    /// <summary>Sonarr's <c>Blocklisted</c>: by infohash when the release has one, else by title and indexer.</summary>
    public bool Matches(ReleaseInfo release)
    {
        if (release.DownloadProtocol != Protocol)
        {
            return false;
        }

        if (release.InfoHash is { Length: > 0 } hash)
        {
            return hash.Equals(TorrentInfoHash, StringComparison.OrdinalIgnoreCase);
        }

        return SourceTitle.Equals(release.Title, StringComparison.OrdinalIgnoreCase) &&
               (string.IsNullOrWhiteSpace(Indexer) || Indexer.Equals(release.Indexer, StringComparison.OrdinalIgnoreCase));
    }
}
