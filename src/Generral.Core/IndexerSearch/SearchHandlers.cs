// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/NzbDrone.Core/IndexerSearch/EpisodeSearchService.cs, SeasonSearchService.cs, SeriesSearchService.cs
// and src/NzbDrone.Core/Indexers/RssSyncService.cs
using System.Linq.Expressions;
using System.Text.Json.Nodes;
using Generral.Core.Messaging;
using Generral.Core.Tv;

namespace Generral.Core.IndexerSearch;

/// <summary>Searches episodes chosen from the command body and grabs the best accepted release per episode.</summary>
public abstract class EpisodeSearchHandlerBase(ReleaseSearchService search, TimeProvider time) : ICommandHandler
{
    public abstract string Name { get; }

    protected DateTime Now => time.GetUtcNow().UtcDateTime;

    public virtual string? Validate(JsonObject body) => null;

    public async Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken)
    {
        var result = await search.AutomaticSearchAsync(Episodes(body), context.Trigger == CommandTrigger.Manual, cancellationToken);
        return result.Summary;
    }

    protected abstract Expression<Func<Episode, bool>> Episodes(JsonObject body);

    protected static int? Int(JsonObject body, string name) =>
        body[name] is JsonValue value && value.TryGetValue<int>(out var id) ? id : null;
}

/// <summary><c>EpisodeSearch { episodeIds }</c>: also searches unmonitored episodes and episodes with a file when triggered manually.</summary>
public sealed class EpisodeSearchHandler(ReleaseSearchService search, TimeProvider time) : EpisodeSearchHandlerBase(search, time)
{
    public const string CommandName = "EpisodeSearch";

    public override string Name => CommandName;

    public override string? Validate(JsonObject body) =>
        Ids(body) is { Count: > 0 } ? null : "episodeIds must be a non-empty list of integers";

    protected override Expression<Func<Episode, bool>> Episodes(JsonObject body)
    {
        var ids = Ids(body)!;
        return e => ids.Contains(e.Id);
    }

    private static List<int>? Ids(JsonObject body) =>
        body["episodeIds"] is JsonArray array && array.All(v => v is JsonValue value && value.TryGetValue<int>(out _))
            ? array.Select(v => v!.GetValue<int>()).ToList()
            : null;
}

/// <summary><c>SeasonSearch { seriesId, seasonNumber }</c>: monitored, aired episodes without a file.</summary>
public sealed class SeasonSearchHandler(ReleaseSearchService search, TimeProvider time) : EpisodeSearchHandlerBase(search, time)
{
    public const string CommandName = "SeasonSearch";

    public override string Name => CommandName;

    public override string? Validate(JsonObject body) =>
        Int(body, "seriesId") is null || Int(body, "seasonNumber") is null ? "seriesId and seasonNumber must be integers" : null;

    protected override Expression<Func<Episode, bool>> Episodes(JsonObject body)
    {
        var (seriesId, season, now) = (Int(body, "seriesId"), Int(body, "seasonNumber"), Now);
        return e => e.SeriesId == seriesId && e.SeasonNumber == season && e.Monitored && e.EpisodeFileId == null && e.AirDateUtc <= now;
    }
}

/// <summary><c>SeriesSearch { seriesId }</c>: monitored, aired episodes without a file.</summary>
public sealed class SeriesSearchHandler(ReleaseSearchService search, TimeProvider time) : EpisodeSearchHandlerBase(search, time)
{
    public const string CommandName = "SeriesSearch";

    public override string Name => CommandName;

    public override string? Validate(JsonObject body) => Int(body, "seriesId") is null ? "seriesId must be an integer" : null;

    protected override Expression<Func<Episode, bool>> Episodes(JsonObject body)
    {
        var (seriesId, now) = (Int(body, "seriesId"), Now);
        return e => e.SeriesId == seriesId && e.Monitored && e.EpisodeFileId == null && e.AirDateUtc <= now;
    }
}

/// <summary><c>MissingEpisodeSearch</c>: Wanted → Missing, i.e. monitored, aired episodes of monitored series without a file.</summary>
public sealed class MissingEpisodeSearchHandler(ReleaseSearchService search, TimeProvider time) : EpisodeSearchHandlerBase(search, time)
{
    public const string CommandName = "MissingEpisodeSearch";

    public override string Name => CommandName;

    protected override Expression<Func<Episode, bool>> Episodes(JsonObject body)
    {
        var now = Now;
        return e => e.Monitored && e.Series!.Monitored && e.EpisodeFileId == null && e.AirDateUtc <= now;
    }
}

public sealed class RssSyncHandler(ReleaseSearchService search) : ICommandHandler
{
    public const string CommandName = "RssSync";

    public string Name => CommandName;

    public async Task<string?> ExecuteAsync(JsonObject body, CommandContext context, CancellationToken cancellationToken)
    {
        var result = await search.RssSyncAsync(cancellationToken);
        return $"{result.Decisions.Count} release(s) evaluated against {result.Searched} wanted episode(s), {result.Grabbed} grabbed";
    }
}
