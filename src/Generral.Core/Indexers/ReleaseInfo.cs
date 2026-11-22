// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/Parser/Model/ReleaseInfo.cs, TorrentInfo.cs, IndexerFlags.cs
namespace Generral.Core.Indexers;

public enum DownloadProtocol
{
    Unknown,
    Usenet,
    Torrent,
}

[Flags]
public enum IndexerFlags
{
    None = 0,
    Freeleech = 1,
    Halfleech = 2,
    DoubleUpload = 4,
    Internal = 8,
    Scene = 16,
    Freeleech75 = 32,
    Freeleech25 = 64,
    Nuked = 128,
    Subtitles = 256,
}

/// <summary>One search or RSS result of an indexer.</summary>
public sealed record ReleaseInfo
{
    public required string Guid { get; init; }

    public required string Title { get; init; }

    public long Size { get; init; }

    public string? DownloadUrl { get; init; }

    public string? MagnetUrl { get; init; }

    public string? InfoHash { get; init; }

    public string? InfoUrl { get; init; }

    public string? CommentUrl { get; init; }

    public int? Seeders { get; init; }

    public int? Peers { get; init; }

    public DateTime PublishDate { get; init; }

    public int IndexerId { get; init; }

    public string Indexer { get; init; } = "";

    public int IndexerPriority { get; init; } = Indexers.Indexer.DefaultPriority;

    public DownloadProtocol DownloadProtocol { get; init; } = DownloadProtocol.Torrent;

    public IReadOnlyList<int> Categories { get; init; } = [];

    public IndexerFlags IndexerFlags { get; init; }
}
