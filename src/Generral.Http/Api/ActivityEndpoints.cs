// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Queue/QueueResource.cs, QueueController.cs, src/Sonarr.Api.V3/History/HistoryResource.cs,
// HistoryController.cs, src/Sonarr.Api.V3/Blocklist/BlocklistResource.cs, BlocklistController.cs and src/Sonarr.Http/PagingResource.cs
using System.Text.Json.Nodes;
using Generral.Core.Blocklisting;
using Generral.Core.Datastore;
using Generral.Core.Download;
using Generral.Core.Download.TrackedDownloads;
using Generral.Core.History;
using Generral.Core.Indexers;
using Generral.Core.IndexerSearch;
using Generral.Core.Messaging;
using Generral.Core.Qualities;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

public enum SortDirection
{
    Default,
    Ascending,
    Descending,
}

public sealed record PagingResource<T>(int Page, int PageSize, string SortKey, SortDirection SortDirection, int TotalRecords, List<T> Records);

public enum QueueStatus
{
    Unknown,
    Queued,
    DownloadingMetadata,
    Paused,
    Downloading,
    Completed,
    Failed,
    Warning,
    Delay,
    DownloadClientUnavailable,
    Fallback,
}

/// <summary>Sonarr's <c>QueueResource</c> (one record per download, with all its episodes).</summary>
public sealed record QueueResource
{
    public int Id { get; init; }

    public int? SeriesId { get; init; }

    public int? EpisodeId { get; init; }

    public List<int> EpisodeIds { get; init; } = [];

    public int? SeasonNumber { get; init; }

    public List<object> Languages { get; init; } = [];

    public required QualityModel Quality { get; init; }

    public List<object> CustomFormats { get; init; } = [];

    public int CustomFormatScore { get; init; }

    public decimal Size { get; init; }

    public required string Title { get; init; }

    public decimal Sizeleft { get; init; }

    public TimeSpan? Timeleft { get; init; }

    public DateTime? EstimatedCompletionTime { get; init; }

    public DateTime? Added { get; init; }

    public QueueStatus Status { get; init; }

    public TrackedDownloadStatus TrackedDownloadStatus { get; init; }

    public TrackedDownloadState TrackedDownloadState { get; init; }

    public List<TrackedDownloadStatusMessage> StatusMessages { get; init; } = [];

    public string? ErrorMessage { get; init; }

    public required string DownloadId { get; init; }

    public DownloadProtocol Protocol { get; init; }

    public required string DownloadClient { get; init; }

    public bool DownloadClientHasPostImportCategory { get; init; }

    public string? Indexer { get; init; }

    public string? OutputPath { get; init; }

    public bool EpisodeHasFile { get; init; }
}

public sealed record HistoryResource(
    int Id,
    int EpisodeId,
    int SeriesId,
    string SourceTitle,
    List<object> Languages,
    QualityModel Quality,
    List<object> CustomFormats,
    int CustomFormatScore,
    bool QualityCutoffNotMet,
    DateTime Date,
    string? DownloadId,
    EpisodeHistoryEventType EventType,
    Dictionary<string, string?> Data);

public sealed record BlocklistResource(
    int Id,
    int SeriesId,
    List<int> EpisodeIds,
    string SourceTitle,
    List<object> Languages,
    QualityModel Quality,
    List<object> CustomFormats,
    DateTime Date,
    DownloadProtocol Protocol,
    string? Indexer,
    string? Message);

public static class ActivityEndpoints
{
    public static void MapActivityEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/queue", GetQueue);
        api.MapDelete("/queue/{id:int}", RemoveFromQueue);
        api.MapGet("/history", GetHistory);
        api.MapGet("/blocklist", GetBlocklist);
        api.MapDelete("/blocklist/{id:int}", DeleteBlocklist);
    }

    private static async Task<PagingResource<QueueResource>> GetQueue(
        TrackedDownloadService tracked,
        GenerralDbContext db,
        TimeProvider time,
        int page = 1,
        int pageSize = 10,
        bool includeUnknownSeriesItems = false)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var downloads = tracked.Queue().Where(t => includeUnknownSeriesItems || t.SeriesId is not null).ToList();
        var episodeIds = downloads.SelectMany(t => t.EpisodeIds.Take(1)).ToList();
        var withFile = (await db.Episodes.AsNoTracking().Where(e => episodeIds.Contains(e.Id) && e.EpisodeFileId != null).Select(e => e.Id).ToListAsync()).ToHashSet();
        var queue = downloads.Select(t => ToResource(t, now) with { EpisodeHasFile = t.EpisodeIds.Count > 0 && withFile.Contains(t.EpisodeIds[0]) }).ToList();
        return Page(queue, page, pageSize, "timeleft", SortDirection.Ascending);
    }

    private static async Task<IResult> RemoveFromQueue(
        int id,
        TrackedDownloadService tracked,
        CommandQueue commands,
        CancellationToken cancellationToken,
        bool removeFromClient = true,
        bool blocklist = false,
        bool skipRedownload = false)
    {
        if (tracked.Find(id) is not { } download)
        {
            return Results.NotFound();
        }

        try
        {
            if (await tracked.RemoveAsync(download, removeFromClient, blocklist, skipRedownload, cancellationToken) is { Count: > 0 } episodeIds)
            {
                commands.Enqueue(EpisodeSearchHandler.CommandName, new JsonObject { ["episodeIds"] = new JsonArray(episodeIds.Select(e => (JsonNode)e).ToArray()) });
            }
        }
        catch (DownloadClientException e)
        {
            return Results.Conflict(new { message = e.Message });
        }

        return Results.Ok(new JsonObject());
    }

    private static async Task<PagingResource<HistoryResource>> GetHistory(
        GenerralDbContext db,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 10,
        string? sortKey = null,
        string? sortDirection = null,
        int? episodeId = null,
        int? seriesId = null,
        string? downloadId = null,
        string? eventType = null)
    {
        var query = db.History.AsNoTracking();
        if (episodeId is { } episode)
        {
            query = query.Where(h => h.EpisodeId == episode);
        }

        if (seriesId is { } series)
        {
            query = query.Where(h => h.SeriesId == series);
        }

        if (downloadId is { Length: > 0 })
        {
            query = query.Where(h => h.DownloadId == downloadId);
        }

        if (Enum.TryParse<EpisodeHistoryEventType>(eventType, ignoreCase: true, out var type))
        {
            query = query.Where(h => h.EventType == type);
        }

        var ascending = string.Equals(sortDirection, "ascending", StringComparison.OrdinalIgnoreCase);
        var (total, rows) = await PageAsync(ascending ? query.OrderBy(h => h.Date).ThenBy(h => h.Id) : query.OrderByDescending(h => h.Date).ThenByDescending(h => h.Id), page, pageSize, cancellationToken);
        var records = rows.Select(h => new HistoryResource(h.Id, h.EpisodeId, h.SeriesId, h.SourceTitle, [], h.Quality, [], 0, false, h.Date, h.DownloadId, h.EventType, h.Data)).ToList();
        return new PagingResource<HistoryResource>(Math.Max(page, 1), PageSize(pageSize), sortKey ?? "date", ascending ? SortDirection.Ascending : SortDirection.Descending, total, records);
    }

    private static async Task<PagingResource<BlocklistResource>> GetBlocklist(
        GenerralDbContext db,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 10,
        string? sortKey = null,
        string? sortDirection = null,
        int? seriesId = null)
    {
        var query = db.Blocklist.AsNoTracking();
        if (seriesId is { } series)
        {
            query = query.Where(b => b.SeriesId == series);
        }

        var ascending = string.Equals(sortDirection, "ascending", StringComparison.OrdinalIgnoreCase);
        var (total, rows) = await PageAsync(ascending ? query.OrderBy(b => b.Date).ThenBy(b => b.Id) : query.OrderByDescending(b => b.Date).ThenByDescending(b => b.Id), page, pageSize, cancellationToken);
        var records = rows.Select(b => new BlocklistResource(b.Id, b.SeriesId, b.EpisodeIds, b.SourceTitle, [], b.Quality, [], b.Date, b.Protocol, b.Indexer, b.Message)).ToList();
        return new PagingResource<BlocklistResource>(Math.Max(page, 1), PageSize(pageSize), sortKey ?? "date", ascending ? SortDirection.Ascending : SortDirection.Descending, total, records);
    }

    private static async Task<IResult> DeleteBlocklist(int id, GenerralDbContext db)
    {
        var deleted = await db.Blocklist.Where(b => b.Id == id).ExecuteDeleteAsync();
        return deleted > 0 ? Results.Ok(new JsonObject()) : Results.NotFound();
    }

    private static QueueResource ToResource(TrackedDownload t, DateTime now)
    {
        var item = t.Item;
        var timeLeft = item?.RemainingTime;
        return new QueueResource
        {
            Id = t.QueueId,
            SeriesId = t.SeriesId,
            EpisodeId = t.EpisodeIds.Count > 0 ? t.EpisodeIds[0] : null,
            EpisodeIds = t.EpisodeIds,
            Quality = t.Quality,
            Size = item?.TotalSize ?? t.Size,
            Title = item?.Title ?? t.Title,
            Sizeleft = item?.RemainingSize ?? t.Size,
            Timeleft = timeLeft,
            EstimatedCompletionTime = timeLeft is { } left ? now + left : null,
            Added = t.Added,
            Status = item is null ? QueueStatus.Queued : Enum.Parse<QueueStatus>(item.Status.ToString()),
            TrackedDownloadStatus = t.Status,
            TrackedDownloadState = t.State,
            StatusMessages = t.StatusMessages,
            ErrorMessage = item?.Message,
            DownloadId = t.DownloadId,
            Protocol = t.Protocol,
            DownloadClient = t.DownloadClientName,
            Indexer = t.Indexer,
            OutputPath = item?.OutputPath,
        };
    }

    private static PagingResource<T> Page<T>(List<T> all, int page, int pageSize, string sortKey, SortDirection direction)
    {
        var (current, size) = (Math.Max(page, 1), PageSize(pageSize));
        return new PagingResource<T>(current, size, sortKey, direction, all.Count, all.Skip((current - 1) * size).Take(size).ToList());
    }

    private static async Task<(int Total, List<T> Rows)> PageAsync<T>(IQueryable<T> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var size = PageSize(pageSize);
        return (await query.CountAsync(cancellationToken), await query.Skip((Math.Max(page, 1) - 1) * size).Take(size).ToListAsync(cancellationToken));
    }

    private static int PageSize(int pageSize) => Math.Clamp(pageSize, 1, 1000);
}
