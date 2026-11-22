// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/DownloadClient/DownloadClientResource.cs, DownloadClientController.cs
// and src/Sonarr.Api.V3/ProviderControllerBase.cs
using System.Text.Json;
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Download;
using Generral.Core.Download.Clients.QBittorrent;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.Tags;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

/// <summary>Sonarr's <c>DownloadClientResource</c>.</summary>
public sealed record DownloadClientResource
{
    public int Id { get; init; }

    public string? Name { get; init; }

    public List<IndexerField>? Fields { get; init; }

    public string? ImplementationName { get; init; }

    public string? Implementation { get; init; }

    public string? ConfigContract { get; init; }

    public string? InfoLink { get; init; }

    public List<int>? Tags { get; init; }

    public List<DownloadClientResource>? Presets { get; init; }

    public bool Enable { get; init; }

    public DownloadProtocol Protocol { get; init; }

    public int? Priority { get; init; }

    public bool? RemoveCompletedDownloads { get; init; }

    public bool? RemoveFailedDownloads { get; init; }
}

public static class DownloadClientEndpoints
{
    private sealed record Implementation(string Name, string ConfigContract, DownloadProtocol Protocol, IReadOnlyList<IndexerField> Fields);

    private static readonly IReadOnlyList<Implementation> Implementations =
    [
        new(QBittorrentClient.Name, "QBittorrentSettings", DownloadProtocol.Torrent, QBittorrentSettings.Fields),
    ];

    public static void MapDownloadClientEndpoints(this IEndpointRouteBuilder api)
    {
        var clients = api.MapGroup("/downloadclient");

        clients.MapGet("/", async (GenerralDbContext db) =>
            (await db.DownloadClients.AsNoTracking().ToListAsync()).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Select(ToResource));
        clients.MapGet("/{id:int}", async (int id, GenerralDbContext db) =>
            await db.DownloadClients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == id) is { } client ? Results.Ok(ToResource(client)) : Results.NotFound());
        clients.MapPost("/", Create);
        clients.MapPut("/{id:int}", Update);
        clients.MapDelete("/{id:int}", Delete);
        clients.MapGet("/schema", () => Implementations.Select(Schema));
        clients.MapPost("/test", Test);
    }

    private static async Task<IResult> Create(DownloadClientResource resource, GenerralDbContext db, TagService tags, DownloadClientProvider provider, CancellationToken cancellationToken, bool forceSave = false)
    {
        var client = new DownloadClient { Name = "", Implementation = "" };
        if (await ApplyAsync(client, resource, db, tags, provider, forceSave, cancellationToken) is { } failure)
        {
            return failure;
        }

        db.DownloadClients.Add(client);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"downloadclient/{client.Id}", ToResource(client));
    }

    private static async Task<IResult> Update(int id, DownloadClientResource resource, GenerralDbContext db, TagService tags, DownloadClientProvider provider, CancellationToken cancellationToken, bool forceSave = false)
    {
        var client = await db.DownloadClients.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (client is null)
        {
            return Results.NotFound();
        }

        if (await ApplyAsync(client, resource, db, tags, provider, forceSave, cancellationToken) is { } failure)
        {
            return failure;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Accepted($"downloadclient/{client.Id}", ToResource(client));
    }

    private static async Task<IResult> Delete(int id, GenerralDbContext db)
    {
        var client = await db.DownloadClients.SingleOrDefaultAsync(c => c.Id == id);
        if (client is null)
        {
            return Results.NotFound();
        }

        db.DownloadClients.Remove(client);
        await db.Indexers.Where(i => i.DownloadClientId == id).ExecuteUpdateAsync(i => i.SetProperty(x => x.DownloadClientId, 0));
        await db.SaveChangesAsync();
        return Results.Ok(new JsonObject());
    }

    private static async Task<IResult> Test(DownloadClientResource resource, GenerralDbContext db, DownloadClientProvider provider, CancellationToken cancellationToken)
    {
        if (provider.Find(resource.Implementation) is not { } implementation)
        {
            return ValidationFailure.BadRequest(UnknownImplementation(resource.Implementation));
        }

        var stored = resource.Id > 0 ? await db.DownloadClients.AsNoTracking().SingleOrDefaultAsync(c => c.Id == resource.Id, cancellationToken) : null;
        var client = new DownloadClient { Name = resource.Name ?? "", Implementation = implementation.Implementation, Settings = Settings(resource, stored?.Settings) };
        var failures = implementation.Validate(client.Settings).ToList();
        if (failures.Count == 0)
        {
            failures.AddRange(await implementation.TestAsync(client, cancellationToken));
        }

        return failures.Count > 0 ? ValidationFailure.BadRequest([.. failures.Select(ToFailure)]) : Results.Ok(new JsonObject());
    }

    /// <summary>Validates and applies the resource; enabled clients must pass the test unless <paramref name="forceSave"/> is set.</summary>
    private static async Task<IResult?> ApplyAsync(DownloadClient client, DownloadClientResource resource, GenerralDbContext db, TagService tags, DownloadClientProvider provider, bool forceSave, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        var name = resource.Name?.Trim() ?? "";
        var implementation = provider.Find(resource.Implementation);
        if (name.Length == 0)
        {
            failures.Add(new ValidationFailure("name", "Name is required"));
        }
        else if ((await db.DownloadClients.AsNoTracking().Where(c => c.Id != client.Id).Select(c => c.Name).ToListAsync(cancellationToken))
                 .Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(new ValidationFailure("name", "Should be unique"));
        }

        if (implementation is null)
        {
            failures.Add(UnknownImplementation(resource.Implementation));
        }

        var priority = resource.Priority ?? DownloadClient.DefaultPriority;
        if (priority is < 1 or > 50)
        {
            failures.Add(new ValidationFailure("priority", "Must be between 1 and 50"));
        }

        if (await TagEndpoints.ValidateTagIdsAsync(tags, resource.Tags) is { } tagFailure)
        {
            failures.Add(tagFailure);
        }

        var settings = Settings(resource, client.Settings);
        if (implementation is not null)
        {
            failures.AddRange(implementation.Validate(settings).Select(ToFailure));
        }

        var candidate = new DownloadClient { Id = client.Id, Name = name, Implementation = implementation?.Implementation ?? "", Settings = settings };
        if (!forceSave && failures.Count == 0 && resource.Enable)
        {
            failures.AddRange((await implementation!.TestAsync(candidate, cancellationToken)).Select(ToFailure));
        }

        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        client.Name = name;
        client.Implementation = implementation!.Implementation;
        client.Settings = settings;
        client.Enable = resource.Enable;
        client.Priority = priority;
        client.RemoveCompletedDownloads = resource.RemoveCompletedDownloads ?? true;
        client.RemoveFailedDownloads = resource.RemoveFailedDownloads ?? true;
        client.Tags = (resource.Tags ?? []).Distinct().ToList();
        return null;
    }

    /// <summary>The stored settings with the posted fields applied; the masked placeholder keeps the stored value.</summary>
    private static JsonObject Settings(DownloadClientResource resource, JsonObject? stored)
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

    private static DownloadClientResource Schema(Implementation implementation) => new()
    {
        Name = "",
        Fields = implementation.Fields.Select(f => f with { Value = f.Value?.DeepClone() }).ToList(),
        ImplementationName = "qBittorrent",
        Implementation = implementation.Name,
        ConfigContract = implementation.ConfigContract,
        InfoLink = InfoLink(implementation.Name),
        Tags = [],
        Presets = [],
        Enable = true,
        Protocol = implementation.Protocol,
        Priority = DownloadClient.DefaultPriority,
        RemoveCompletedDownloads = true,
        RemoveFailedDownloads = true,
    };

    private static DownloadClientResource ToResource(DownloadClient client)
    {
        var implementation = Implementations.FirstOrDefault(i => string.Equals(i.Name, client.Implementation, StringComparison.OrdinalIgnoreCase));
        var schema = implementation?.Fields ?? [];
        return new DownloadClientResource
        {
            Id = client.Id,
            Name = client.Name,
            Fields = schema.Select(f => f with { Value = Mask(f, client.Settings.TryGetPropertyValue(f.Name, out var value) ? value?.DeepClone() : f.Value?.DeepClone()) }).ToList(),
            ImplementationName = "qBittorrent",
            Implementation = client.Implementation,
            ConfigContract = implementation?.ConfigContract,
            InfoLink = InfoLink(client.Implementation),
            Tags = client.Tags,
            Enable = client.Enable,
            Protocol = implementation?.Protocol ?? DownloadProtocol.Unknown,
            Priority = client.Priority,
            RemoveCompletedDownloads = client.RemoveCompletedDownloads,
            RemoveFailedDownloads = client.RemoveFailedDownloads,
        };
    }

    private static JsonNode? Mask(IndexerField field, JsonNode? value) =>
        field.Privacy is "password" or "apiKey" && value is JsonValue v && v.GetValueKind() == JsonValueKind.String && v.GetValue<string>().Length > 0
            ? SourceEndpoints.PrivateValue
            : value;

    private static string InfoLink(string implementation) => $"https://wiki.servarr.com/sonarr/supported#{implementation.ToLowerInvariant()}";

    private static ValidationFailure ToFailure(IndexerValidationFailure f) => new(f.PropertyName, f.ErrorMessage);

    private static ValidationFailure UnknownImplementation(string? implementation) =>
        new("implementation", $"Unknown download client implementation '{implementation}'");
}
