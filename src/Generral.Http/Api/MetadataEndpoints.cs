// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Metadata/MetadataResource.cs and MetadataController.cs
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Extras.Metadata;
using Generral.Core.Indexers;
using Generral.Core.Tv;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

/// <summary>Sonarr's <c>MetadataResource</c>.</summary>
public sealed record MetadataResource
{
    public int Id { get; init; }

    public string? Name { get; init; }

    public List<IndexerField>? Fields { get; init; }

    public string? ImplementationName { get; init; }

    public string? Implementation { get; init; }

    public string? ConfigContract { get; init; }

    public string? InfoLink { get; init; }

    public List<int>? Tags { get; init; }

    public List<MetadataResource>? Presets { get; init; }

    public bool Enable { get; init; }
}

public static class MetadataEndpoints
{
    private const string InfoLink = "https://support.plex.tv/articles/using-nfo-metadata-files-with-plex/";

    public static void MapMetadataEndpoints(this IEndpointRouteBuilder api)
    {
        var metadata = api.MapGroup("/metadata");
        metadata.MapGet("/", async (GenerralDbContext db, MetadataService service) =>
            (await db.Metadata.AsNoTracking().ToListAsync()).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(m => ToResource(m, service)));
        metadata.MapGet("/{id:int}", async (int id, GenerralDbContext db, MetadataService service) =>
            await db.Metadata.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id) is { } m ? Results.Ok(ToResource(m, service)) : Results.NotFound());
        metadata.MapPost("/", Create);
        metadata.MapPut("/{id:int}", Update);
        metadata.MapDelete("/{id:int}", Delete);
        metadata.MapGet("/schema", (IEnumerable<IMetadataConsumer> consumers) => consumers.Select(Schema));
        metadata.MapPost("/test", (MetadataResource resource, MetadataService service) =>
            service.Find(resource.Implementation) is null ? ValidationFailure.BadRequest(UnknownImplementation(resource.Implementation)) : Results.Ok(new JsonObject()));
    }

    private static async Task<IResult> Create(MetadataResource resource, GenerralDbContext db, MetadataService service, CancellationToken cancellationToken)
    {
        var definition = new MetadataDefinition { Name = "", Implementation = "" };
        if (await ApplyAsync(definition, resource, db, service, cancellationToken) is { } failure)
        {
            return failure;
        }

        db.Metadata.Add(definition);
        await db.SaveChangesAsync(cancellationToken);
        await SyncAllAsync(db, service, cancellationToken);
        return Results.Created($"metadata/{definition.Id}", ToResource(definition, service));
    }

    private static async Task<IResult> Update(int id, MetadataResource resource, GenerralDbContext db, MetadataService service, CancellationToken cancellationToken)
    {
        var definition = await db.Metadata.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        if (await ApplyAsync(definition, resource, db, service, cancellationToken) is { } failure)
        {
            return failure;
        }

        await db.SaveChangesAsync(cancellationToken);
        await SyncAllAsync(db, service, cancellationToken);
        return Results.Accepted($"metadata/{definition.Id}", ToResource(definition, service));
    }

    private static async Task<IResult> Delete(int id, GenerralDbContext db, MetadataService service, CancellationToken cancellationToken)
    {
        if (await db.Metadata.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken) == 0)
        {
            return Results.NotFound();
        }

        await SyncAllAsync(db, service, cancellationToken);
        return Results.Ok(new JsonObject());
    }

    /// <summary>Writes (or removes) the files of every series, as Sonarr does when the metadata settings change.</summary>
    private static async Task SyncAllAsync(GenerralDbContext db, MetadataService service, CancellationToken cancellationToken)
    {
        foreach (var seriesId in await db.Series.AsNoTracking().Select(s => s.Id).ToListAsync(cancellationToken))
        {
            await service.SyncSeriesAsync(seriesId, cancellationToken);
        }
    }

    private static async Task<IResult?> ApplyAsync(MetadataDefinition definition, MetadataResource resource, GenerralDbContext db, MetadataService service, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        var consumer = service.Find(resource.Implementation);
        var name = resource.Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            failures.Add(new ValidationFailure("name", "Name is required"));
        }
        else if ((await db.Metadata.AsNoTracking().Where(m => m.Id != definition.Id).Select(m => m.Name).ToListAsync(cancellationToken))
                 .Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(new ValidationFailure("name", "Should be unique"));
        }

        if (consumer is null)
        {
            failures.Add(UnknownImplementation(resource.Implementation));
        }

        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        definition.Name = name;
        definition.Implementation = consumer!.Implementation;
        definition.Enable = resource.Enable;
        definition.Settings = ProviderFields.Merge(resource.Fields, definition.Settings);
        return null;
    }

    private static MetadataResource Schema(IMetadataConsumer consumer) => new()
    {
        Name = consumer.Name,
        Fields = ProviderFields.Schema(consumer.Fields),
        ImplementationName = consumer.Name,
        Implementation = consumer.Implementation,
        ConfigContract = consumer.Implementation + "Settings",
        InfoLink = InfoLink,
        Tags = [],
        Presets = [],
        Enable = false,
    };

    private static MetadataResource ToResource(MetadataDefinition definition, MetadataService service)
    {
        var consumer = service.Find(definition.Implementation);
        return new MetadataResource
        {
            Id = definition.Id,
            Name = definition.Name,
            Fields = consumer is null ? [] : ProviderFields.Read(consumer.Fields, definition.Settings),
            ImplementationName = consumer?.Name ?? definition.Implementation,
            Implementation = definition.Implementation,
            ConfigContract = definition.Implementation + "Settings",
            InfoLink = InfoLink,
            Tags = [],
            Enable = definition.Enable,
        };
    }

    private static ValidationFailure UnknownImplementation(string? implementation) =>
        new("implementation", $"Unknown metadata implementation '{implementation}'");
}
