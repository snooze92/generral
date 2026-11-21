using System.Collections.Concurrent;
using Generral.Core.Datastore;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Generral.Core.Mappings;

public sealed record ApplyMappingResult(int MappingId, string MappingName, int Added, int Changed, int Unchanged, int Orphaned, int Errors, string? Error)
{
    public bool Success => Error is null;

    public string Summary => Success
        ? $"{MappingName}: {Added} added, {Changed} changed, {Unchanged} unchanged, {Orphaned} orphaned, {Errors} errors"
        : $"{MappingName}: {Error}";
}

/// <summary>Evaluates mappings and persists the plan: seasons, episodes, links and orphan status (docs/specs/05-mapping.md).</summary>
public sealed class ApplyMappingsService(IServiceScopeFactory scopes, MappingEngine engine, TimeProvider time, ILogger<ApplyMappingsService> logger)
{
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _perSeries = new();

    /// <summary>Series timezone (container <c>TZ</c>); per-series timezones are not configurable yet.</summary>
    public TimeZoneInfo Timezone { get; init; } = TimeZoneInfo.Local;

    /// <exception cref="KeyNotFoundException">No such mapping.</exception>
    public async Task<ApplyMappingResult> ApplyAsync(int mappingId, CancellationToken cancellationToken = default)
    {
        int seriesId;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
            seriesId = await db.Mappings.Where(m => m.Id == mappingId).Select(m => (int?)m.SeriesId).SingleOrDefaultAsync(cancellationToken)
                       ?? throw new KeyNotFoundException($"Mapping {mappingId} not found");
        }

        // Mappings of one series share its episode numbers.
        var seriesLock = _perSeries.GetOrAdd(seriesId, _ => new SemaphoreSlim(1));
        await seriesLock.WaitAsync(cancellationToken);
        try
        {
            return await ApplyLockedAsync(mappingId, cancellationToken);
        }
        finally
        {
            seriesLock.Release();
        }
    }

    private async Task<ApplyMappingResult> ApplyLockedAsync(int mappingId, CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var mapping = await db.Mappings.Include(m => m.Series).Include(m => m.Source).SingleOrDefaultAsync(m => m.Id == mappingId, cancellationToken)
                      ?? throw new KeyNotFoundException($"Mapping {mappingId} not found");
        var series = mapping.Series!;

        await MappingHistory.RecordAsync(db, mapping, cancellationToken);

        MappingPlan plan;
        try
        {
            plan = engine.Evaluate(
                new MappingInput
                {
                    Definition = MappingDefinitionParser.FromYaml(mapping.Definition),
                    Series = series,
                    Source = mapping.Source!,
                    SourceMeta = mapping.Source!.Meta,
                    Events = await db.Events.AsNoTracking().Where(e => e.SourceId == mapping.SourceId).ToListAsync(cancellationToken),
                    Episodes = await db.Episodes.Where(e => e.SeriesId == series.Id).ToListAsync(cancellationToken),
                    Links = await db.EpisodeLinks.AsNoTracking().Where(l => l.MappingId == mapping.Id).ToListAsync(cancellationToken),
                    Now = time.GetUtcNow().UtcDateTime,
                    Timezone = Timezone,
                },
                cancellationToken);
        }
        catch (MappingDefinitionException e)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogWarning("Mapping {Name} is invalid: {Error}", mapping.Name, e.Message);
            return new ApplyMappingResult(mapping.Id, mapping.Name, 0, 0, 0, 0, 0, e.Message);
        }

        // Episodes are tracked (loaded above); applying in two passes avoids transient unique-index clashes when numbers swap.
        var episodes = db.Episodes.Local.ToDictionary(e => e.Id);
        var seasons = await db.Seasons.Where(s => s.SeriesId == series.Id).ToDictionaryAsync(s => s.SeasonNumber, cancellationToken);
        foreach (var (number, title) in plan.Seasons)
        {
            if (!seasons.TryGetValue(number, out var season))
            {
                db.Seasons.Add(new Season { SeriesId = series.Id, SeasonNumber = number, Title = title, Monitored = series.MonitorNewItems == NewItemMonitorType.All });
            }
            else if (title is not null)
            {
                season.Title = title;
            }
        }

        series.SeriesType = plan.SeriesType;

        var moved = plan.Rows
            .Where(r => r.IsApplied && r.EpisodeId is { } id && (episodes[id].SeasonNumber != r.SeasonNumber || episodes[id].EpisodeNumber != r.EpisodeNumber))
            .Select(r => episodes[r.EpisodeId!.Value])
            .ToList();
        if (moved.Count > 0)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
            foreach (var episode in moved)
            {
                episode.EpisodeNumber = -episode.Id;
            }

            await db.SaveChangesAsync(cancellationToken);
            await SaveAsync(db, plan, mapping, episodes, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            await SaveAsync(db, plan, mapping, episodes, cancellationToken);
        }

        var counts = plan.Rows.GroupBy(r => r.Change).ToDictionary(g => g.Key, g => g.Count());
        var result = new ApplyMappingResult(
            mapping.Id,
            mapping.Name,
            counts.GetValueOrDefault(MappingChange.New),
            counts.GetValueOrDefault(MappingChange.Changed),
            counts.GetValueOrDefault(MappingChange.Unchanged),
            counts.GetValueOrDefault(MappingChange.Orphaned),
            counts.GetValueOrDefault(MappingChange.Error),
            null);
        logger.LogInformation("Applied mapping {Summary}", result.Summary);
        return result;
    }

    private static async Task SaveAsync(GenerralDbContext db, MappingPlan plan, Mapping mapping, Dictionary<int, Episode> episodes, CancellationToken cancellationToken)
    {
        foreach (var row in plan.Rows)
        {
            switch (row.Change)
            {
                case MappingChange.New:
                    var episode = new Episode { SeriesId = mapping.SeriesId, Title = "" };
                    row.ApplyTo(episode);
                    db.EpisodeLinks.Add(new EpisodeLink { MappingId = mapping.Id, EventId = row.Event.Id, Episode = episode });
                    break;
                case MappingChange.Changed:
                    row.ApplyTo(episodes[row.EpisodeId!.Value]);
                    break;
                case MappingChange.Orphaned:
                    episodes[row.EpisodeId!.Value].Status = EpisodeStatus.Orphaned;
                    break;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
