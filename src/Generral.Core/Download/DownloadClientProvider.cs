// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Download/DownloadClientProvider.cs
using Generral.Core.Indexers;

namespace Generral.Core.Download;

/// <summary>The download client implementations, and the choice of a client for a grab.</summary>
public sealed class DownloadClientProvider(IEnumerable<IDownloadClient> implementations)
{
    private readonly Dictionary<string, IDownloadClient> _implementations = implementations.ToDictionary(i => i.Implementation, StringComparer.OrdinalIgnoreCase);

    public IDownloadClient? Find(string? implementation) =>
        implementation is not null && _implementations.TryGetValue(implementation, out var client) ? client : null;

    /// <summary>
    /// The indexer's pinned client, else the enabled clients for the protocol by priority, keeping only tagged clients that share a tag
    /// with the series or, when none does, untagged ones (Sonarr's rules, without the round-robin among equal priorities).
    /// </summary>
    /// <exception cref="DownloadClientException">No usable client.</exception>
    public DownloadClient Select(IReadOnlyList<DownloadClient> clients, DownloadProtocol protocol, Indexer? indexer, IReadOnlyCollection<int> seriesTags)
    {
        var available = clients.Where(c => c.Enable && Find(c.Implementation)?.Protocol == protocol).ToList();
        if (indexer is { DownloadClientId: > 0 } pinned)
        {
            return available.FirstOrDefault(c => c.Id == pinned.DownloadClientId)
                ?? throw new DownloadClientException($"Indexer {pinned.Name} is set to use an unavailable download client");
        }

        var tagged = available.Where(c => c.Tags.Intersect(seriesTags).Any()).ToList();
        var candidates = tagged.Count > 0 ? tagged : available.Where(c => c.Tags.Count == 0).ToList();
        return candidates.OrderBy(c => c.Priority).ThenBy(c => c.Id).FirstOrDefault()
            ?? throw new DownloadClientException($"No {protocol.ToString().ToLowerInvariant()} download client is available");
    }
}
