// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Notifications/NotificationResource.cs and NotificationController.cs
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Indexers;
using Generral.Core.Notifications;
using Generral.Core.Tags;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

/// <summary>Sonarr's <c>NotificationResource</c>; triggers Generral doesn't raise yet are reported as unsupported.</summary>
public sealed record NotificationResource
{
    public int Id { get; init; }

    public string? Name { get; init; }

    public List<IndexerField>? Fields { get; init; }

    public string? ImplementationName { get; init; }

    public string? Implementation { get; init; }

    public string? ConfigContract { get; init; }

    public string? InfoLink { get; init; }

    public string? Link { get; init; }

    public List<int>? Tags { get; init; }

    public List<NotificationResource>? Presets { get; init; }

    public bool OnGrab { get; init; }

    public bool OnDownload { get; init; }

    public bool OnUpgrade { get; init; }

    public bool OnImportComplete { get; init; }

    public bool OnRename { get; init; }

    public bool OnSeriesAdd { get; init; }

    public bool OnSeriesDelete { get; init; }

    public bool OnEpisodeFileDelete { get; init; }

    public bool OnEpisodeFileDeleteForUpgrade { get; init; }

    public bool OnHealthIssue { get; init; }

    public bool IncludeHealthWarnings { get; init; }

    public bool OnHealthRestored { get; init; }

    public bool OnApplicationUpdate { get; init; }

    public bool OnManualInteractionRequired { get; init; }

    public bool SupportsOnGrab { get; init; }

    public bool SupportsOnDownload { get; init; }

    public bool SupportsOnUpgrade { get; init; }

    public bool SupportsOnImportComplete { get; init; }

    public bool SupportsOnRename { get; init; }

    public bool SupportsOnSeriesAdd { get; init; }

    public bool SupportsOnSeriesDelete { get; init; }

    public bool SupportsOnEpisodeFileDelete { get; init; }

    public bool SupportsOnEpisodeFileDeleteForUpgrade { get; init; }

    public bool SupportsOnHealthIssue { get; init; }

    public bool SupportsOnHealthRestored { get; init; }

    public bool SupportsOnApplicationUpdate { get; init; }

    public bool SupportsOnManualInteractionRequired { get; init; }
}

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this IEndpointRouteBuilder api)
    {
        var notifications = api.MapGroup("/notification");
        notifications.MapGet("/", async (GenerralDbContext db, NotificationProvider provider) =>
            (await db.Notifications.AsNoTracking().ToListAsync()).OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase).Select(n => ToResource(n, provider)));
        notifications.MapGet("/{id:int}", async (int id, GenerralDbContext db, NotificationProvider provider) =>
            await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == id) is { } n ? Results.Ok(ToResource(n, provider)) : Results.NotFound());
        notifications.MapPost("/", Create);
        notifications.MapPut("/{id:int}", Update);
        notifications.MapDelete("/{id:int}", async (int id, GenerralDbContext db) =>
            await db.Notifications.Where(n => n.Id == id).ExecuteDeleteAsync() > 0 ? Results.Ok(new JsonObject()) : Results.NotFound());
        notifications.MapGet("/schema", (NotificationProvider provider) => provider.All.OrderBy(n => n.Name).Select(Schema));
        notifications.MapPost("/test", Test);
    }

    private static async Task<IResult> Create(NotificationResource resource, GenerralDbContext db, TagService tags, NotificationProvider provider, CancellationToken cancellationToken, bool forceSave = false)
    {
        var definition = new NotificationDefinition { Name = "", Implementation = "" };
        if (await ApplyAsync(definition, resource, db, tags, provider, forceSave, cancellationToken) is { } failure)
        {
            return failure;
        }

        db.Notifications.Add(definition);
        await db.SaveChangesAsync(cancellationToken);
        return Results.Created($"notification/{definition.Id}", ToResource(definition, provider));
    }

    private static async Task<IResult> Update(int id, NotificationResource resource, GenerralDbContext db, TagService tags, NotificationProvider provider, CancellationToken cancellationToken, bool forceSave = false)
    {
        var definition = await db.Notifications.SingleOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (definition is null)
        {
            return Results.NotFound();
        }

        if (await ApplyAsync(definition, resource, db, tags, provider, forceSave, cancellationToken) is { } failure)
        {
            return failure;
        }

        await db.SaveChangesAsync(cancellationToken);
        return Results.Accepted($"notification/{definition.Id}", ToResource(definition, provider));
    }

    private static async Task<IResult> Test(NotificationResource resource, GenerralDbContext db, NotificationProvider provider, CancellationToken cancellationToken)
    {
        if (provider.Find(resource.Implementation) is not { } notification)
        {
            return ValidationFailure.BadRequest(UnknownImplementation(resource.Implementation));
        }

        var stored = resource.Id > 0 ? await db.Notifications.AsNoTracking().SingleOrDefaultAsync(n => n.Id == resource.Id, cancellationToken) : null;
        var definition = new NotificationDefinition { Name = resource.Name ?? "", Implementation = notification.Implementation, Settings = ProviderFields.Merge(resource.Fields, stored?.Settings) };
        var failures = notification.Validate(definition.Settings).ToList();
        if (failures.Count == 0)
        {
            failures.AddRange(await notification.TestAsync(definition, cancellationToken));
        }

        return failures.Count > 0 ? ValidationFailure.BadRequest([.. failures.Select(ProviderFields.ToFailure)]) : Results.Ok(new JsonObject());
    }

    /// <summary>Validates and applies the resource; the connection must pass the test unless <paramref name="forceSave"/> is set (Sonarr parity).</summary>
    private static async Task<IResult?> ApplyAsync(
        NotificationDefinition definition, NotificationResource resource, GenerralDbContext db, TagService tags, NotificationProvider provider, bool forceSave, CancellationToken cancellationToken)
    {
        var failures = new List<ValidationFailure>();
        var notification = provider.Find(resource.Implementation);
        var name = resource.Name?.Trim() ?? "";
        if (name.Length == 0)
        {
            failures.Add(new ValidationFailure("name", "Name is required"));
        }
        else if ((await db.Notifications.AsNoTracking().Where(n => n.Id != definition.Id).Select(n => n.Name).ToListAsync(cancellationToken))
                 .Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(new ValidationFailure("name", "Should be unique"));
        }

        if (notification is null)
        {
            failures.Add(UnknownImplementation(resource.Implementation));
        }

        if (await TagEndpoints.ValidateTagIdsAsync(tags, resource.Tags) is { } tagFailure)
        {
            failures.Add(tagFailure);
        }

        var settings = ProviderFields.Merge(resource.Fields, definition.Settings);
        if (notification is not null)
        {
            failures.AddRange(notification.Validate(settings).Select(ProviderFields.ToFailure));
        }

        if (!forceSave && failures.Count == 0)
        {
            var candidate = new NotificationDefinition { Id = definition.Id, Name = name, Implementation = notification!.Implementation, Settings = settings };
            failures.AddRange((await notification.TestAsync(candidate, cancellationToken)).Select(ProviderFields.ToFailure));
        }

        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        var supported = notification!.SupportedTriggers;
        bool On(bool requested, NotificationTrigger trigger) => requested && supported.Contains(trigger);
        definition.Name = name;
        definition.Implementation = notification.Implementation;
        definition.Settings = settings;
        definition.Tags = (resource.Tags ?? []).Distinct().ToList();
        definition.OnGrab = On(resource.OnGrab, NotificationTrigger.Grab);
        definition.OnDownload = On(resource.OnDownload, NotificationTrigger.Download);
        definition.OnUpgrade = On(resource.OnUpgrade, NotificationTrigger.Upgrade);
        definition.OnSeriesAdd = On(resource.OnSeriesAdd, NotificationTrigger.SeriesAdd);
        definition.OnSeriesDelete = On(resource.OnSeriesDelete, NotificationTrigger.SeriesDelete);
        definition.OnEpisodeFileDelete = On(resource.OnEpisodeFileDelete, NotificationTrigger.EpisodeFileDelete);
        definition.OnEpisodeFileDeleteForUpgrade = On(resource.OnEpisodeFileDeleteForUpgrade, NotificationTrigger.EpisodeFileDeleteForUpgrade);
        return null;
    }

    private static NotificationResource Schema(INotification notification) => WithSupports(notification, new NotificationResource
    {
        Name = "",
        Fields = ProviderFields.Schema(notification.Fields),
        Tags = [],
        Presets = [],
    });

    private static NotificationResource ToResource(NotificationDefinition definition, NotificationProvider provider)
    {
        var notification = provider.Find(definition.Implementation);
        var resource = new NotificationResource
        {
            Id = definition.Id,
            Name = definition.Name,
            Fields = notification is null ? [] : ProviderFields.Read(notification.Fields, definition.Settings),
            Implementation = definition.Implementation,
            Tags = definition.Tags,
            OnGrab = definition.OnGrab,
            OnDownload = definition.OnDownload,
            OnUpgrade = definition.OnUpgrade,
            OnSeriesAdd = definition.OnSeriesAdd,
            OnSeriesDelete = definition.OnSeriesDelete,
            OnEpisodeFileDelete = definition.OnEpisodeFileDelete,
            OnEpisodeFileDeleteForUpgrade = definition.OnEpisodeFileDeleteForUpgrade,
        };
        return notification is null ? resource : WithSupports(notification, resource);
    }

    private static NotificationResource WithSupports(INotification notification, NotificationResource resource)
    {
        var supported = notification.SupportedTriggers;
        return resource with
        {
            ImplementationName = notification.Name,
            Implementation = notification.Implementation,
            ConfigContract = notification.Implementation + "Settings",
            InfoLink = $"https://wiki.servarr.com/sonarr/supported#{notification.Implementation.ToLowerInvariant()}",
            Link = notification.Link,
            SupportsOnGrab = supported.Contains(NotificationTrigger.Grab),
            SupportsOnDownload = supported.Contains(NotificationTrigger.Download),
            SupportsOnUpgrade = supported.Contains(NotificationTrigger.Upgrade),
            SupportsOnSeriesAdd = supported.Contains(NotificationTrigger.SeriesAdd),
            SupportsOnSeriesDelete = supported.Contains(NotificationTrigger.SeriesDelete),
            SupportsOnEpisodeFileDelete = supported.Contains(NotificationTrigger.EpisodeFileDelete),
            SupportsOnEpisodeFileDeleteForUpgrade = supported.Contains(NotificationTrigger.EpisodeFileDeleteForUpgrade),
        };
    }

    private static ValidationFailure UnknownImplementation(string? implementation) =>
        new("implementation", $"Unknown notification implementation '{implementation}'");
}
