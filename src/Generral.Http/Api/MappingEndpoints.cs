using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Mappings;
using Generral.Core.Messaging;
using Generral.Core.Sources;
using Generral.Core.Tv;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

public sealed record MappingResource
{
    public int Id { get; init; }

    public string? Name { get; init; }

    public int SeriesId { get; init; }

    public int SourceId { get; init; }

    public int Priority { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>YAML (or JSON) as authored, returned verbatim.</summary>
    public string? Definition { get; init; }

    public int Version { get; init; }

    public static MappingResource From(Mapping m) => new()
    {
        Id = m.Id,
        Name = m.Name,
        SeriesId = m.SeriesId,
        SourceId = m.SourceId,
        Priority = m.Priority,
        Enabled = m.Enabled,
        Definition = m.Definition,
        Version = m.Version,
    };
}

public sealed record MappingVersionResource(int Version, string Definition, DateTime Created);

/// <summary>An unsaved definition to preview. <c>id</c> (a saved mapping) makes its links count, so changes and numbers are those an apply would make.</summary>
public sealed record MappingPreviewRequest(int? Id, int? SeriesId, int SourceId, string? Definition);

public sealed record MappingPreviewEventResource(int Id, string Key, string Title, DateTime? Start, EventStatus Status);

public sealed record MappingPreviewRowResource
{
    public required MappingPreviewEventResource Event { get; init; }

    public bool Kept { get; init; }

    public string? RejectedBy { get; init; }

    /// <summary>The episode the event is linked to, with its current numbers.</summary>
    public int? EpisodeId { get; init; }

    public int? CurrentSeasonNumber { get; init; }

    public int? CurrentEpisodeNumber { get; init; }

    public int? SeasonNumber { get; init; }

    public int? EpisodeNumber { get; init; }

    public int? AbsoluteEpisodeNumber { get; init; }

    public string? Title { get; init; }

    public string? Overview { get; init; }

    public DateOnly? AirDate { get; init; }

    public DateTime? AirDateUtc { get; init; }

    public int? Runtime { get; init; }

    public string? Thumb { get; init; }

    public required JsonObject SearchTerms { get; init; }

    public bool NumberLocked { get; init; }

    /// <summary>The episode's season/episode number would change.</summary>
    public bool Renumbered { get; init; }

    public MappingChange Change { get; init; }

    public required IReadOnlyList<string> Errors { get; init; }
}

/// <summary>The preview's summary bar (docs/specs/05-mapping.md § Preview &amp; dry-run).</summary>
public sealed record MappingPreviewSummary(int Events, int Kept, int Episodes, int New, int Changed, int Unchanged, int Orphaned, int Filtered, int Errors, int Renumbered);

public sealed record MappingPreviewSeasonResource(int SeasonNumber, string? Title);

public sealed record MappingPreviewResource(
    IReadOnlyList<MappingPreviewRowResource> Rows,
    MappingPreviewSummary Summary,
    SeriesType SeriesType,
    IReadOnlyList<MappingPreviewSeasonResource> Seasons,
    IReadOnlyList<string> Errors);

public static class MappingEndpoints
{
    public static void MapMappingEndpoints(this IEndpointRouteBuilder api)
    {
        var mappings = api.MapGroup("/mapping");

        mappings.MapGet("/", GetAll);
        mappings.MapGet("/{id:int}", Get);
        mappings.MapPost("/", Create);
        mappings.MapPut("/{id:int}", Update);
        mappings.MapDelete("/{id:int}", Delete);
        mappings.MapGet("/{id:int}/versions", Versions);
        mappings.MapPost("/{id:int}/apply", Apply);
        mappings.MapPost("/preview", Preview);
    }

    private static async Task<List<MappingResource>> GetAll(GenerralDbContext db, int? seriesId)
    {
        var mappings = await db.Mappings.AsNoTracking()
            .Where(m => seriesId == null || m.SeriesId == seriesId)
            .OrderBy(m => m.SeriesId).ThenBy(m => m.Priority).ThenBy(m => m.Id)
            .ToListAsync();
        return mappings.Select(MappingResource.From).ToList();
    }

    private static async Task<IResult> Get(int id, GenerralDbContext db)
    {
        var mapping = await db.Mappings.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id);
        return mapping is null ? Results.NotFound() : Results.Ok(MappingResource.From(mapping));
    }

    private static async Task<IResult> Create(MappingResource resource, GenerralDbContext db, MappingEngine engine, CommandQueue queue)
    {
        var failures = await ValidateAsync(resource, null, db, engine);
        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        var mapping = new Mapping { Name = resource.Name!.Trim(), SeriesId = resource.SeriesId, SourceId = resource.SourceId };
        CopyTo(mapping, resource);
        db.Mappings.Add(mapping);
        await MappingHistory.RecordAsync(db, mapping);
        await db.SaveChangesAsync();

        EnqueueApply(queue, mapping);
        return Results.Created($"mapping/{mapping.Id}", MappingResource.From(mapping));
    }

    private static async Task<IResult> Update(int id, MappingResource resource, GenerralDbContext db, MappingEngine engine, CommandQueue queue)
    {
        var mapping = await db.Mappings.SingleOrDefaultAsync(m => m.Id == id);
        if (mapping is null)
        {
            return Results.NotFound();
        }

        var failures = await ValidateAsync(resource, mapping, db, engine);
        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        mapping.Name = resource.Name!.Trim();
        CopyTo(mapping, resource);
        await MappingHistory.RecordAsync(db, mapping);
        await db.SaveChangesAsync();

        EnqueueApply(queue, mapping);
        return Results.Accepted($"mapping/{mapping.Id}", MappingResource.From(mapping));
    }

    /// <summary>Versions and links cascade; episodes no other mapping produces become orphaned (spec 03 § Episode).</summary>
    private static async Task<IResult> Delete(int id, GenerralDbContext db)
    {
        var mapping = await db.Mappings.SingleOrDefaultAsync(m => m.Id == id);
        if (mapping is null)
        {
            return Results.NotFound();
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.Episodes
            .Where(e => db.EpisodeLinks.Any(l => l.EpisodeId == e.Id && l.MappingId == id) && !db.EpisodeLinks.Any(l => l.EpisodeId == e.Id && l.MappingId != id))
            .ExecuteUpdateAsync(e => e.SetProperty(x => x.Status, EpisodeStatus.Orphaned));
        db.Mappings.Remove(mapping);
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return Results.NoContent();
    }

    private static async Task<IResult> Versions(int id, GenerralDbContext db)
    {
        if (!await db.Mappings.AnyAsync(m => m.Id == id))
        {
            return Results.NotFound();
        }

        var versions = await db.MappingVersions.AsNoTracking().Where(v => v.MappingId == id).OrderByDescending(v => v.Version).ToListAsync();
        return Results.Ok(versions.Select(v => new MappingVersionResource(v.Version, v.Definition, v.Created)));
    }

    private static async Task<IResult> Apply(int id, GenerralDbContext db, CommandQueue queue)
    {
        if (!await db.Mappings.AnyAsync(m => m.Id == id))
        {
            return Results.NotFound();
        }

        return CommandEndpoints.Created(queue.Enqueue(ApplyMappingsHandler.CommandName, ApplyMappingsHandler.Body(id)));
    }

    /// <summary>Evaluates an unsaved definition against the source's current events and the series' episodes. Nothing is stored.</summary>
    private static async Task<IResult> Preview(
        MappingPreviewRequest request, GenerralDbContext db, MappingEngine engine, ApplyMappingsService apply, TimeProvider time, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        var mapping = request.Id is > 0 ? await db.Mappings.AsNoTracking().SingleOrDefaultAsync(m => m.Id == request.Id, cancellationToken) : null;
        if (request.Id is > 0 && mapping is null)
        {
            failures.Add(new ValidationFailure("id", "Mapping does not exist"));
        }

        var seriesId = request.SeriesId ?? mapping?.SeriesId;
        var series = seriesId is { } sid ? await db.Series.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sid, cancellationToken) : null;
        if (seriesId is not null && series is null)
        {
            failures.Add(new ValidationFailure("seriesId", "Series does not exist"));
        }

        var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == request.SourceId, cancellationToken);
        if (source is null)
        {
            failures.Add(new ValidationFailure("sourceId", "Source does not exist"));
        }

        var definition = ParseDefinition(request.Definition, engine, failures);
        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        // Links only count while the mapping keeps its series and source.
        var links = mapping is not null && mapping.SeriesId == series?.Id && mapping.SourceId == source!.Id
            ? await db.EpisodeLinks.AsNoTracking().Where(l => l.MappingId == mapping.Id).ToListAsync(cancellationToken)
            : [];
        var episodes = series is null ? [] : await db.Episodes.AsNoTracking().Where(e => e.SeriesId == series.Id).ToListAsync(cancellationToken);

        var plan = engine.Evaluate(
            new MappingInput
            {
                Definition = definition!,
                Series = series ?? new Series { Title = "", SortTitle = "", CleanTitle = "", TitleSlug = "", Path = "", RootFolderPath = "" },
                Source = source!,
                SourceMeta = source!.Meta,
                Events = await db.Events.AsNoTracking().Where(e => e.SourceId == source.Id).ToListAsync(cancellationToken),
                Episodes = episodes,
                Links = links,
                Now = time.GetUtcNow().UtcDateTime,
                Timezone = apply.Timezone,
            },
            cancellationToken);

        var byId = episodes.ToDictionary(e => e.Id);
        var rows = plan.Rows.Select(r => ToResource(r, r.EpisodeId is { } eid ? byId[eid] : null)).ToList();
        var counts = rows.GroupBy(r => r.Change).ToDictionary(g => g.Key, g => g.Count());
        var summary = new MappingPreviewSummary(
            rows.Count,
            rows.Count(r => r.Kept),
            plan.Rows.Count(r => r.IsApplied),
            counts.GetValueOrDefault(MappingChange.New),
            counts.GetValueOrDefault(MappingChange.Changed),
            counts.GetValueOrDefault(MappingChange.Unchanged),
            counts.GetValueOrDefault(MappingChange.Orphaned),
            counts.GetValueOrDefault(MappingChange.Filtered),
            counts.GetValueOrDefault(MappingChange.Error),
            rows.Count(r => r.Renumbered));

        return Results.Ok(new MappingPreviewResource(
            rows,
            summary,
            plan.SeriesType,
            plan.Seasons.OrderBy(s => s.Key).Select(s => new MappingPreviewSeasonResource(s.Key, s.Value)).ToList(),
            rows.SelectMany(r => r.Errors.Select(e => $"{r.Event.Key}: {e}")).ToList()));
    }

    private static MappingPreviewRowResource ToResource(MappingPlanRow row, Episode? existing) => new()
    {
        Event = new MappingPreviewEventResource(row.Event.Id, row.Event.Key, row.Event.Title, row.Event.Start, row.Event.Status),
        Kept = row.Kept,
        RejectedBy = row.RejectedBy,
        EpisodeId = row.EpisodeId,
        CurrentSeasonNumber = existing?.SeasonNumber,
        CurrentEpisodeNumber = existing?.EpisodeNumber,
        SeasonNumber = row.SeasonNumber,
        EpisodeNumber = row.EpisodeNumber,
        AbsoluteEpisodeNumber = row.AbsoluteEpisodeNumber,
        Title = row.Title,
        Overview = row.Overview,
        AirDate = row.AirDate,
        AirDateUtc = row.AirDateUtc,
        Runtime = row.Runtime,
        Thumb = row.Thumb,
        SearchTerms = row.SearchTerms,
        NumberLocked = row.NumberLocked,
        Renumbered = row.IsApplied && existing is not null && (existing.SeasonNumber != row.SeasonNumber || existing.EpisodeNumber != row.EpisodeNumber),
        Change = row.Change,
        Errors = row.Errors,
    };

    private static void CopyTo(Mapping mapping, MappingResource resource)
    {
        mapping.Priority = resource.Priority;
        mapping.Enabled = resource.Enabled;
        mapping.Definition = resource.Definition!;
    }

    /// <summary>Saving queues <c>ApplyMappings</c> for that mapping only (spec 05 § Preview &amp; dry-run); disabled mappings are not applied.</summary>
    private static void EnqueueApply(CommandQueue queue, Mapping mapping)
    {
        if (mapping.Enabled)
        {
            queue.Enqueue(ApplyMappingsHandler.CommandName, ApplyMappingsHandler.Body(mapping.Id));
        }
    }

    private static async Task<List<ValidationFailure>> ValidateAsync(MappingResource resource, Mapping? existing, GenerralDbContext db, MappingEngine engine)
    {
        var failures = new List<ValidationFailure>();
        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            failures.Add(new ValidationFailure("name", "'Name' must not be empty."));
        }

        if (existing is not null && resource.SeriesId != existing.SeriesId)
        {
            failures.Add(new ValidationFailure("seriesId", "The series of a mapping cannot be changed"));
        }
        else if (!await db.Series.AnyAsync(s => s.Id == resource.SeriesId))
        {
            failures.Add(new ValidationFailure("seriesId", "Series does not exist"));
        }

        if (existing is not null && resource.SourceId != existing.SourceId)
        {
            failures.Add(new ValidationFailure("sourceId", "The source of a mapping cannot be changed"));
        }
        else if (!await db.Sources.AnyAsync(s => s.Id == resource.SourceId))
        {
            failures.Add(new ValidationFailure("sourceId", "Source does not exist"));
        }

        ParseDefinition(resource.Definition, engine, failures);
        return failures;
    }

    private static MappingDefinition? ParseDefinition(string? definition, MappingEngine engine, List<ValidationFailure> failures)
    {
        if (string.IsNullOrWhiteSpace(definition))
        {
            failures.Add(new ValidationFailure("definition", "'Definition' must not be empty."));
            return null;
        }

        try
        {
            var parsed = MappingDefinitionParser.FromYaml(definition);
            engine.Validate(parsed);
            return parsed;
        }
        catch (MappingDefinitionException e)
        {
            failures.Add(new ValidationFailure("definition", e.Message));
            return null;
        }
    }
}
