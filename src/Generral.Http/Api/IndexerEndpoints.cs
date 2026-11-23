// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Indexers/IndexerResource.cs, src/Sonarr.Api.V3/Indexers/IndexerController.cs,
// src/Sonarr.Api.V3/ProviderControllerBase.cs, src/Sonarr.Api.V3/ProviderResource.cs and src/Sonarr.Http/ClientSchema/SchemaBuilder.cs
using System.Text.Json;
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.Tags;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

/// <summary>Sonarr's <c>IndexerResource</c>. Nullable where Prowlarr may send <c>null</c>.</summary>
public sealed record IndexerResource
{
    public int Id { get; init; }

    public string? Name { get; init; }

    public List<IndexerField>? Fields { get; init; }

    public string? ImplementationName { get; init; }

    public string? Implementation { get; init; }

    public string? ConfigContract { get; init; }

    public string? InfoLink { get; init; }

    public ProviderMessage? Message { get; init; }

    public List<int>? Tags { get; init; }

    public List<IndexerResource>? Presets { get; init; }

    public bool EnableRss { get; init; }

    public bool EnableAutomaticSearch { get; init; }

    public bool EnableInteractiveSearch { get; init; }

    public bool SupportsRss { get; init; }

    public bool SupportsSearch { get; init; }

    public DownloadProtocol Protocol { get; init; }

    public int? Priority { get; init; }

    public int? SeasonSearchMaximumSingleEpisodeAge { get; init; }

    public int? DownloadClientId { get; init; }
}

/// <summary>Sonarr's <c>ProviderMessage</c>; <c>type</c> is <c>info</c>, <c>warning</c> or <c>error</c>.</summary>
public sealed record ProviderMessage(string Message, string Type);

public static class IndexerEndpoints
{
    /// <summary>Maps Sonarr's indexer provider routes; used for both <c>/api/v1</c> and the <c>/api/v3</c> shim.</summary>
    public static void MapIndexerEndpoints(this IEndpointRouteBuilder api)
    {
        var indexers = api.MapGroup("/indexer");

        indexers.MapGet("/", GetAll);
        indexers.MapGet("/{id:int}", Get);
        indexers.MapPost("/", Create);
        indexers.MapPut("/{id:int}", Update);
        indexers.MapDelete("/{id:int}", Delete);
        indexers.MapGet("/schema", () => IndexerImplementations.All.Select(Schema).OrderBy(r => r.ImplementationName, StringComparer.Ordinal));
        indexers.MapPost("/test", Test);
    }

    private static async Task<IEnumerable<IndexerResource>> GetAll(GenerralDbContext db) =>
        (await db.Indexers.AsNoTracking().ToListAsync()).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Select(ToResource);

    private static async Task<IResult> Get(int id, GenerralDbContext db) =>
        await db.Indexers.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id) is { } indexer ? Results.Ok(ToResource(indexer)) : Results.NotFound();

    private static async Task<IResult> Create(IndexerResource resource, GenerralDbContext db, TagService tags, TorznabClient torznab, CancellationToken cancellationToken, bool forceSave = false)
    {
        var indexer = new Indexer { Name = "", Implementation = "" };
        if (await ApplyAsync(indexer, resource, db, tags, torznab, forceSave, cancellationToken) is { } failure)
        {
            return failure;
        }

        db.Indexers.Add(indexer);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"indexer/{indexer.Id}", ToResource(indexer));
    }

    private static async Task<IResult> Update(int id, IndexerResource resource, GenerralDbContext db, TagService tags, TorznabClient torznab, CancellationToken cancellationToken, bool forceSave = false)
    {
        var indexer = await db.Indexers.SingleOrDefaultAsync(i => i.Id == id, cancellationToken);
        if (indexer is null)
        {
            return Results.NotFound();
        }

        if (await ApplyAsync(indexer, resource, db, tags, torznab, forceSave, cancellationToken) is { } failure)
        {
            return failure;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Accepted($"indexer/{indexer.Id}", ToResource(indexer));
    }

    private static async Task<IResult> Delete(int id, GenerralDbContext db, IndexerStatusService status)
    {
        var indexer = await db.Indexers.SingleOrDefaultAsync(i => i.Id == id);
        if (indexer is null)
        {
            return Results.NotFound();
        }

        db.Indexers.Remove(indexer);
        await db.SaveChangesAsync();
        status.Remove(id);
        return Results.Ok(new JsonObject());
    }

    private static async Task<IResult> Test(IndexerResource resource, GenerralDbContext db, TorznabClient torznab, IndexerStatusService status, CancellationToken cancellationToken)
    {
        var stored = resource.Id > 0 ? await db.Indexers.AsNoTracking().SingleOrDefaultAsync(i => i.Id == resource.Id, cancellationToken) : null;
        if (IndexerImplementations.Find(resource.Implementation) is null)
        {
            return ValidationFailure.BadRequest(UnknownImplementation(resource.Implementation));
        }

        var settings = TorznabSettings.From(Settings(resource, stored?.Settings));
        var failures = settings.Validate().Select(ToFailure).ToList();
        if (failures.Count == 0 && await torznab.TestAsync(settings, cancellationToken) is { } failure)
        {
            failures.Add(ToFailure(failure));
        }

        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        if (stored is not null)
        {
            status.RecordSuccess(stored);
        }

        return Results.Ok(new JsonObject());
    }

    /// <summary>
    /// Validates and applies the resource. With <paramref name="forceSave"/> only structural checks run (Prowlarr retries
    /// with it when the indexer is unreachable). Otherwise enabled indexers must pass the caps test.
    /// </summary>
    private static async Task<IResult?> ApplyAsync(Indexer indexer, IndexerResource resource, GenerralDbContext db, TagService tags, TorznabClient torznab, bool forceSave, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        var name = resource.Name?.Trim() ?? "";
        var implementation = IndexerImplementations.Find(resource.Implementation);
        if (name.Length == 0)
        {
            failures.Add(new ValidationFailure("name", "Name is required"));
        }
        else if ((await db.Indexers.AsNoTracking().Where(i => i.Id != indexer.Id).Select(i => i.Name).ToListAsync(cancellationToken))
                 .Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(new ValidationFailure("name", "Should be unique"));
        }

        if (implementation is null)
        {
            failures.Add(UnknownImplementation(resource.Implementation));
        }

        var settings = Settings(resource, indexer.Settings);
        var priority = resource.Priority ?? Indexer.DefaultPriority;
        if (!forceSave && failures.Count == 0)
        {
            if (priority is < 1 or > 50)
            {
                failures.Add(new ValidationFailure("priority", "Must be between 1 and 50"));
            }

            failures.AddRange(TorznabSettings.From(settings).Validate().Select(ToFailure));
            if (await TagEndpoints.ValidateTagIdsAsync(tags, resource.Tags) is { } tagFailure)
            {
                failures.Add(tagFailure);
            }

            var enabled = resource.EnableRss || resource.EnableAutomaticSearch || resource.EnableInteractiveSearch;
            if (failures.Count == 0 && enabled && await torznab.TestAsync(TorznabSettings.From(settings), cancellationToken) is { } testFailure)
            {
                failures.Add(ToFailure(testFailure));
            }
        }

        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        indexer.Name = name;
        indexer.Implementation = implementation!.Name;
        indexer.Settings = settings;
        indexer.EnableRss = resource.EnableRss;
        indexer.EnableAutomaticSearch = resource.EnableAutomaticSearch;
        indexer.EnableInteractiveSearch = resource.EnableInteractiveSearch;
        indexer.Priority = Math.Clamp(priority, 1, 50);
        indexer.DownloadClientId = resource.DownloadClientId ?? 0;
        indexer.SeasonSearchMaximumSingleEpisodeAge = resource.SeasonSearchMaximumSingleEpisodeAge ?? 0;
        indexer.Tags = (resource.Tags ?? []).Distinct().ToList();
        return null;
    }

    /// <summary>
    /// The stored settings with the posted fields applied: a <c>null</c> value clears a field, the masked placeholder keeps
    /// the stored value, and fields that aren't posted are kept (Prowlarr only sends the ones it manages).
    /// </summary>
    private static JsonObject Settings(IndexerResource resource, JsonObject? stored)
    {
        var settings = stored?.DeepClone().AsObject() ?? [];
        foreach (var field in (resource.Fields ?? []).Where(f => f?.Name is { Length: > 0 }))
        {
            if (field.Value is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>() == SourceEndpoints.PrivateValue)
            {
                continue;
            }

            if (field.Value is null)
            {
                settings.Remove(field.Name);
            }
            else
            {
                settings[field.Name] = field.Value.DeepClone();
            }
        }

        return settings;
    }

    private static IndexerResource Schema(IndexerImplementation implementation) => new()
    {
        Name = "",
        Fields = implementation.Fields.Select(f => f with { Value = f.Value?.DeepClone() }).ToList(),
        ImplementationName = implementation.Name,
        Implementation = implementation.Name,
        ConfigContract = implementation.ConfigContract,
        InfoLink = InfoLink(implementation.Name),
        Tags = [],
        Presets = [],
        EnableRss = true,
        EnableAutomaticSearch = true,
        EnableInteractiveSearch = true,
        SupportsRss = true,
        SupportsSearch = true,
        Protocol = implementation.Protocol,
        Priority = Indexer.DefaultPriority,
        SeasonSearchMaximumSingleEpisodeAge = 0,
        DownloadClientId = 0,
    };

    private static IndexerResource ToResource(Indexer indexer)
    {
        var implementation = IndexerImplementations.Find(indexer.Implementation);
        var schema = implementation?.Fields ?? [];
        var fields = schema.Select(f => f with { Value = Mask(f, indexer.Settings.TryGetPropertyValue(f.Name, out var value) ? value?.DeepClone() : f.Value?.DeepClone()) }).ToList();

        // Settings the schema doesn't know about are kept and returned as plain fields.
        fields.AddRange(indexer.Settings
            .Where(p => schema.All(f => f.Name != p.Key))
            .Select((p, i) => new IndexerField { Order = schema.Count + i, Name = p.Key, Label = p.Key, Value = p.Value?.DeepClone() }));

        var supported = implementation?.Supported == true;
        return new IndexerResource
        {
            Id = indexer.Id,
            Name = indexer.Name,
            Fields = fields,
            ImplementationName = indexer.Implementation,
            Implementation = indexer.Implementation,
            ConfigContract = implementation?.ConfigContract,
            InfoLink = InfoLink(indexer.Implementation),
            Message = supported ? null : new ProviderMessage("Usenet is not supported yet: this indexer is not used", "warning"),
            Tags = indexer.Tags,
            EnableRss = indexer.EnableRss,
            EnableAutomaticSearch = indexer.EnableAutomaticSearch,
            EnableInteractiveSearch = indexer.EnableInteractiveSearch,
            SupportsRss = supported,
            SupportsSearch = supported,
            Protocol = implementation?.Protocol ?? DownloadProtocol.Unknown,
            Priority = indexer.Priority,
            SeasonSearchMaximumSingleEpisodeAge = indexer.SeasonSearchMaximumSingleEpisodeAge,
            DownloadClientId = indexer.DownloadClientId,
        };
    }

    private static JsonNode? Mask(IndexerField field, JsonNode? value) =>
        field.Privacy != "normal" && value is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>().Length > 0
            ? SourceEndpoints.PrivateValue
            : value;

    private static string InfoLink(string implementation) => $"https://wiki.servarr.com/sonarr/supported#{implementation.ToLowerInvariant()}";

    private static ValidationFailure ToFailure(IndexerValidationFailure f) => new(f.PropertyName, f.ErrorMessage);

    private static ValidationFailure UnknownImplementation(string? implementation) =>
        new("implementation", $"Unknown indexer implementation '{implementation}'");
}
