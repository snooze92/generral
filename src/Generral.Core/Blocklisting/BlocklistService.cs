// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Blocklisting/BlocklistService.cs
using System.Globalization;
using Generral.Core.Datastore;
using Generral.Core.History;
using Generral.Core.Indexers;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Blocklisting;

public sealed class BlocklistService(GenerralDbContext db, TimeProvider time)
{
    public Task<bool> IsHashBlocklistedAsync(int seriesId, string hash, CancellationToken cancellationToken = default) =>
        db.Blocklist.AnyAsync(b => b.SeriesId == seriesId && b.TorrentInfoHash == hash.ToUpper(), cancellationToken);

    /// <summary>Sonarr's <c>Handle(DownloadFailedEvent)</c>: blocklists the release of a failed grab.</summary>
    public async Task BlockAsync(IReadOnlyList<EpisodeHistory> grabbed, string message, string? source, CancellationToken cancellationToken = default)
    {
        var grab = grabbed[0];
        var data = grab.Data;
        db.Blocklist.Add(new Blocklist
        {
            SeriesId = grab.SeriesId,
            EpisodeIds = grabbed.Select(h => h.EpisodeId).Distinct().ToList(),
            SourceTitle = grab.SourceTitle,
            Quality = grab.Quality,
            Date = time.GetUtcNow().UtcDateTime,
            PublishedDate = DateTime.TryParse(data.GetValueOrDefault(EpisodeHistory.PublishedDate), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var published) ? published : null,
            Size = long.TryParse(data.GetValueOrDefault(EpisodeHistory.Size), CultureInfo.InvariantCulture, out var size) ? size : null,
            Indexer = data.GetValueOrDefault(EpisodeHistory.Indexer),
            Protocol = int.TryParse(data.GetValueOrDefault(EpisodeHistory.Protocol), CultureInfo.InvariantCulture, out var protocol) ? (DownloadProtocol)protocol : DownloadProtocol.Torrent,
            IndexerFlags = Enum.TryParse<IndexerFlags>(data.GetValueOrDefault("indexerFlags"), out var flags) ? flags : IndexerFlags.None,
            Message = message,
            Source = source,
            TorrentInfoHash = data.GetValueOrDefault(EpisodeHistory.TorrentInfoHash) ?? grab.DownloadId,
        });
        await db.SaveChangesAsync(cancellationToken);
    }
}
