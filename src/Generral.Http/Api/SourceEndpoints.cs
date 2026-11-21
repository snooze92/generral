using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Generral.Core.Datastore;
using Generral.Core.Messaging;
using Generral.Core.Sources;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Http;
using Generral.Core.Sources.Types;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

/// <summary>A source in the shape of Sonarr's provider resources: the type's settings are a <c>fields[]</c> list.</summary>
public sealed record SourceResource
{
    public int Id { get; init; }

    public string Name { get; init; } = "";

    /// <summary>Source type id, e.g. <c>thesportsdb</c>.</summary>
    public string Type { get; init; } = "";

    public string? TypeName { get; init; }

    public bool Enabled { get; init; } = true;

    /// <summary>Minutes, at least 15.</summary>
    public int RefreshInterval { get; init; } = (int)Source.DefaultRefreshInterval.TotalMinutes;

    public List<int> Tags { get; init; } = [];

    public List<SourceFieldResource> Fields { get; init; } = [];

    public DateTime? LastRefresh { get; init; }

    public DateTime? LastSuccess { get; init; }

    public string? LastError { get; init; }

    public DateTime? NextRefresh { get; init; }

    /// <summary>Events that are not gone.</summary>
    public int EventCount { get; init; }
}

/// <summary>Sonarr's <c>Field</c>. Only <c>name</c> and <c>value</c> are read back.</summary>
public sealed record SourceFieldResource
{
    public int Order { get; init; }

    public required string Name { get; init; }

    public string? Label { get; init; }

    public JsonNode? Value { get; init; }

    public string? Type { get; init; }

    public string? HelpText { get; init; }

    public string? Unit { get; init; }

    public string? Placeholder { get; init; }

    public bool Advanced { get; init; }

    public string? Privacy { get; init; }

    public IReadOnlyList<SourceSelectOption>? SelectOptions { get; init; }
}

public sealed record SourceTestResource
{
    public required bool Success { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<EventResource> Events { get; init; } = [];

    public IReadOnlyList<SourceItemError> ItemErrors { get; init; } = [];

    public required IReadOnlyList<RawResponse> Responses { get; init; }

    public JsonObject Meta { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record EventResource
{
    public int Id { get; init; }

    public int SourceId { get; init; }

    public required string Key { get; init; }

    public required string Title { get; init; }

    public DateTime? Start { get; init; }

    public DateTime? End { get; init; }

    public string? Timezone { get; init; }

    public EventStatus Status { get; init; }

    public required JsonObject Fields { get; init; }

    public required ImageSet Images { get; init; }

    public DateTime? FirstSeen { get; init; }

    public DateTime? LastSeen { get; init; }

    public DateTime? LastChanged { get; init; }

    public int MissedRefreshes { get; init; }

    /// <summary>Only on <c>GET /event/{id}</c>, newest first.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<EventRevisionResource>? Revisions { get; init; }

    public static EventResource From(Event ev, IReadOnlyList<EventRevisionResource>? revisions = null) => new()
    {
        Id = ev.Id,
        SourceId = ev.SourceId,
        Key = ev.Key,
        Title = ev.Title,
        Start = ev.Start,
        End = ev.End,
        Timezone = ev.Timezone,
        Status = ev.Status,
        Fields = ev.Fields,
        Images = ev.Images,
        FirstSeen = ev.FirstSeen,
        LastSeen = ev.LastSeen,
        LastChanged = ev.LastChanged,
        MissedRefreshes = ev.MissedRefreshes,
        Revisions = revisions,
    };

    public static EventResource From(ExtractedEvent ev) => new()
    {
        Key = ev.Key,
        Title = ev.Title,
        Start = ev.Start,
        End = ev.End,
        Timezone = ev.Timezone,
        Status = ev.Status,
        Fields = ev.Fields,
        Images = ev.Images,
    };
}

/// <summary>The event as it was before a change.</summary>
public sealed record EventRevisionResource(int Id, DateTime Created, string Title, DateTime? Start, DateTime? End, EventStatus Status, JsonObject Fields, ImageSet Images)
{
    public static EventRevisionResource From(EventRevision r) => new(r.Id, r.Created, r.Title, r.Start, r.End, r.Status, r.Fields, r.Images);
}

public static class SourceEndpoints
{
    /// <summary>Sonarr's placeholder for a stored password: sending it back keeps the stored value.</summary>
    public const string PrivateValue = "********";

    public static void MapSourceEndpoints(this IEndpointRouteBuilder api)
    {
        var sources = api.MapGroup("/source");

        sources.MapGet("/", GetAll);
        sources.MapGet("/{id:int}", Get);
        sources.MapPost("/", Create);
        sources.MapPut("/{id:int}", Update);
        sources.MapDelete("/{id:int}", Delete);
        sources.MapGet("/schema", (SourceTypeRegistry types) => types.All.Select(Schema));
        sources.MapPost("/test", Test);
        sources.MapPost("/{id:int}/refresh", Refresh);
        sources.MapGet("/{id:int}/events", Events);

        api.MapGet("/event/{id:int}", GetEvent);
    }

    private static async Task<List<SourceResource>> GetAll(GenerralDbContext db, SourceTypeRegistry types)
    {
        var sources = await db.Sources.AsNoTracking().OrderBy(s => s.Name).ToListAsync();
        var counts = await db.Events.Where(e => e.Status != EventStatus.Gone)
            .GroupBy(e => e.SourceId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Key, g => g.Count);

        return sources.Select(s => ToResource(s, types.Find(s.Type), counts.GetValueOrDefault(s.Id))).ToList();
    }

    private static async Task<IResult> Get(int id, GenerralDbContext db, SourceTypeRegistry types)
    {
        var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
        return source is null ? Results.NotFound() : Results.Ok(await ToResourceAsync(db, source, types));
    }

    private static async Task<IResult> Create(SourceResource resource, GenerralDbContext db, SourceTypeRegistry types)
    {
        var settings = Settings(resource, null);
        var failures = await ValidateAsync(resource, settings, db, types, id: null);
        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        var source = new Source { Name = resource.Name.Trim(), Type = resource.Type };
        Apply(source, resource, settings, types);
        db.Sources.Add(source);
        await db.SaveChangesAsync();

        return Results.Created($"source/{source.Id}", ToResource(source, types.Find(source.Type), 0));
    }

    private static async Task<IResult> Update(int id, SourceResource resource, GenerralDbContext db, SourceTypeRegistry types)
    {
        var source = await db.Sources.SingleOrDefaultAsync(s => s.Id == id);
        if (source is null)
        {
            return Results.NotFound();
        }

        var settings = Settings(resource, source);
        var failures = await ValidateAsync(resource, settings, db, types, id);
        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        source.Name = resource.Name.Trim();
        Apply(source, resource, settings, types);
        await db.SaveChangesAsync();

        return Results.Accepted($"source/{source.Id}", await ToResourceAsync(db, source, types));
    }

    private static async Task<IResult> Delete(int id, GenerralDbContext db)
    {
        var source = await db.Sources.SingleOrDefaultAsync(s => s.Id == id);
        if (source is null)
        {
            return Results.NotFound();
        }

        var mappings = await db.Mappings.Where(m => m.SourceId == id).Select(m => m.Name).ToListAsync();
        if (mappings.Count > 0)
        {
            return ValidationFailure.BadRequest(new ValidationFailure("id", $"Source is used by mapping(s): {string.Join(", ", mappings)}"));
        }

        db.Sources.Remove(source);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    /// <summary>Fetches and extracts an unsaved definition. Nothing is stored; responses come from the cache when possible.</summary>
    private static async Task<IResult> Test(SourceResource resource, GenerralDbContext db, SourceTypeRegistry types, CancellationToken cancellationToken)
    {
        var stored = resource.Id > 0 ? await db.Sources.AsNoTracking().SingleOrDefaultAsync(s => s.Id == resource.Id, cancellationToken) : null;
        var settings = Settings(resource, stored);
        var type = types.Find(resource.Type ?? "");
        if (type is null)
        {
            return ValidationFailure.BadRequest(UnknownType(resource.Type));
        }

        var failures = type.Validate(settings).Select(f => new ValidationFailure(f.PropertyName, f.ErrorMessage)).ToArray();
        if (failures.Length > 0)
        {
            return ValidationFailure.BadRequest(failures);
        }

        try
        {
            var result = await type.FetchAsync(settings, cancellationToken: cancellationToken);
            return Results.Ok(new SourceTestResource
            {
                Success = true,
                Events = result.Events.Select(EventResource.From).ToList(),
                ItemErrors = result.ItemErrors,
                Responses = result.Responses,
                Meta = result.Meta,
                Warnings = result.Warnings,
            });
        }
        catch (SourceFetchException e)
        {
            return Results.Ok(new SourceTestResource { Success = false, Error = e.Message, Responses = e.Responses });
        }
    }

    private static async Task<IResult> Refresh(int id, GenerralDbContext db, CommandQueue queue)
    {
        if (!await db.Sources.AnyAsync(s => s.Id == id))
        {
            return Results.NotFound();
        }

        return CommandEndpoints.Created(queue.Enqueue(RefreshSourceHandler.CommandName, new JsonObject { ["sourceId"] = id }));
    }

    private static async Task<IResult> Events(int id, GenerralDbContext db, bool includeGone = false)
    {
        if (!await db.Sources.AnyAsync(s => s.Id == id))
        {
            return Results.NotFound();
        }

        var events = await db.Events.AsNoTracking()
            .Where(e => e.SourceId == id && (includeGone || e.Status != EventStatus.Gone))
            .ToListAsync();

        return Results.Ok(events.OrderBy(e => e.Start ?? DateTime.MaxValue).ThenBy(e => e.Key, StringComparer.Ordinal).Select(e => EventResource.From(e)));
    }

    private static async Task<IResult> GetEvent(int id, GenerralDbContext db)
    {
        var ev = await db.Events.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id);
        if (ev is null)
        {
            return Results.NotFound();
        }

        var revisions = await db.EventRevisions.AsNoTracking().Where(r => r.EventId == id).ToListAsync();
        return Results.Ok(EventResource.From(ev, revisions.OrderByDescending(r => r.Created).ThenByDescending(r => r.Id).Select(EventRevisionResource.From).ToList()));
    }

    private static SourceResource Schema(ISourceType type) => new()
    {
        Name = type.Name,
        Type = type.Id,
        TypeName = type.Name,
        Fields = type.Fields.Select((f, i) => Field(f, i, f.Value?.DeepClone())).ToList(),
    };

    private static async Task<SourceResource> ToResourceAsync(GenerralDbContext db, Source source, SourceTypeRegistry types)
    {
        var count = await db.Events.CountAsync(e => e.SourceId == source.Id && e.Status != EventStatus.Gone);
        return ToResource(source, types.Find(source.Type), count);
    }

    private static SourceResource ToResource(Source source, ISourceType? type, int eventCount)
    {
        var schema = type?.Fields ?? [];
        var fields = schema.Select((f, i) => Field(f, i, Mask(f, source.Settings.TryGetPropertyValue(f.Name, out var value) ? value?.DeepClone() : f.Value?.DeepClone()))).ToList();

        // Settings the type doesn't know about are kept and returned as plain fields.
        fields.AddRange(source.Settings
            .Where(p => schema.All(f => f.Name != p.Key))
            .Select((p, i) => new SourceFieldResource { Order = schema.Count + i, Name = p.Key, Label = p.Key, Value = p.Value?.DeepClone() }));

        return new SourceResource
        {
            Id = source.Id,
            Name = source.Name,
            Type = source.Type,
            TypeName = type?.Name,
            Enabled = source.Enabled,
            RefreshInterval = (int)source.RefreshInterval.TotalMinutes,
            Tags = source.Tags,
            Fields = fields,
            LastRefresh = source.LastRefresh,
            LastSuccess = source.LastSuccess,
            LastError = source.LastError,
            NextRefresh = source.Enabled ? SourceRefreshService.NextRefresh(source) : null,
            EventCount = eventCount,
        };
    }

    private static SourceFieldResource Field(SourceField field, int order, JsonNode? value) => new()
    {
        Order = order,
        Name = field.Name,
        Label = field.Label,
        Value = value,
        Type = JsonNamingPolicy.CamelCase.ConvertName(field.Type.ToString()),
        HelpText = field.HelpText,
        Unit = field.Unit,
        Placeholder = field.Placeholder,
        Advanced = field.Advanced,
        Privacy = field.Type == SourceFieldType.Password ? "password" : null,
        SelectOptions = field.SelectOptions,
    };

    private static JsonNode? Mask(SourceField field, JsonNode? value) =>
        field.Type == SourceFieldType.Password && FieldValues.ToText(FieldValues.FromJson(value)) is { Length: > 0 } ? PrivateValue : value;

    /// <summary>The settings object from the fields; a masked value keeps the stored one.</summary>
    private static JsonObject Settings(SourceResource resource, Source? stored)
    {
        var settings = new JsonObject();
        foreach (var field in (resource.Fields ?? []).Where(f => f?.Name is not null && f.Value is not null))
        {
            var value = field.Value is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == PrivateValue && stored is not null
                ? stored.Settings[field.Name]
                : field.Value;
            if (value is not null)
            {
                settings[field.Name] = value.DeepClone();
            }
        }

        return settings;
    }

    private static async Task<List<ValidationFailure>> ValidateAsync(SourceResource resource, JsonObject settings, GenerralDbContext db, SourceTypeRegistry types, int? id)
    {
        var failures = new List<ValidationFailure>();
        var name = resource.Name?.Trim() ?? "";

        if (name.Length == 0)
        {
            failures.Add(new ValidationFailure("name", "Name is required"));
        }
        else if (await db.Sources.AnyAsync(s => s.Name == name && s.Id != id))
        {
            failures.Add(new ValidationFailure("name", "Should be unique"));
        }

        if (resource.RefreshInterval < SourceRefreshService.MinRefreshInterval.TotalMinutes)
        {
            failures.Add(new ValidationFailure("refreshInterval", $"Must be at least {SourceRefreshService.MinRefreshInterval.TotalMinutes:0} minutes"));
        }

        var type = types.Find(resource.Type ?? "");
        if (type is null)
        {
            failures.Add(UnknownType(resource.Type));
        }
        else
        {
            failures.AddRange(type.Validate(settings).Select(f => new ValidationFailure(f.PropertyName, f.ErrorMessage)));
        }

        return failures;
    }

    private static ValidationFailure UnknownType(string? type) => new("type", $"Unknown source type '{type}'");

    private static void Apply(Source source, SourceResource resource, JsonObject settings, SourceTypeRegistry types)
    {
        source.Type = types.Get(resource.Type).Id;
        source.Settings = settings;
        source.Enabled = resource.Enabled;
        source.RefreshInterval = TimeSpan.FromMinutes(resource.RefreshInterval);
        source.Tags = resource.Tags ?? [];
    }
}
