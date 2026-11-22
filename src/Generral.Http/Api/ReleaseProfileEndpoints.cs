// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Profiles/Release/ReleaseProfileController.cs and ReleaseProfileResource.cs
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Profiles;
using Generral.Core.Tags;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

/// <summary>Sonarr's <c>ReleaseProfileResource</c>. <c>required</c>/<c>ignored</c> accept a comma-separated string too.</summary>
public sealed record ReleaseProfileResource
{
    public int Id { get; init; }

    public string? Name { get; init; }

    public bool Enabled { get; init; } = true;

    public JsonNode? Required { get; init; }

    public JsonNode? Ignored { get; init; }

    public int IndexerId { get; init; }

    public List<int> Tags { get; init; } = [];

    public List<int> ExcludedTags { get; init; } = [];

    public static ReleaseProfileResource From(ReleaseProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        Enabled = profile.Enabled,
        Required = new JsonArray([.. profile.Required.Select(t => JsonValue.Create(t))]),
        Ignored = new JsonArray([.. profile.Ignored.Select(t => JsonValue.Create(t))]),
        IndexerId = profile.IndexerId,
        Tags = profile.Tags,
        ExcludedTags = profile.ExcludedTags,
    };

    internal static List<string>? Terms(JsonNode? node) => node switch
    {
        null => [],
        JsonValue value when value.TryGetValue<string>(out var text) => text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList(),
        JsonArray array when array.All(t => t is JsonValue v && v.TryGetValue<string>(out _)) => array.Select(t => t!.GetValue<string>()).ToList(),
        _ => null,
    };
}

public static class ReleaseProfileEndpoints
{
    public static void MapReleaseProfileEndpoints(this IEndpointRouteBuilder api)
    {
        var profiles = api.MapGroup("/releaseprofile");

        profiles.MapGet("/", async (ReleaseProfileService service) => (await service.AllAsync()).Select(ReleaseProfileResource.From));
        profiles.MapGet("/{id:int}", Get);
        profiles.MapPost("/", Create);
        profiles.MapPut("/{id:int}", Update);
        profiles.MapDelete("/{id:int}", Delete);
    }

    private static async Task<IResult> Get(int id, ReleaseProfileService service) =>
        await service.GetAsync(id) is { } profile ? Results.Ok(ReleaseProfileResource.From(profile)) : Results.NotFound();

    private static async Task<IResult> Create(ReleaseProfileResource resource, ReleaseProfileService service, GenerralDbContext db, TagService tags)
    {
        var (profile, failures) = await ValidateAsync(resource, 0, db, tags);
        if (failures.Length > 0)
        {
            return ValidationFailure.BadRequest(failures);
        }

        profile = await service.AddAsync(profile!);
        return Results.Created($"releaseprofile/{profile.Id}", ReleaseProfileResource.From(profile));
    }

    private static async Task<IResult> Update(int id, ReleaseProfileResource resource, ReleaseProfileService service, GenerralDbContext db, TagService tags)
    {
        var (profile, failures) = await ValidateAsync(resource, id, db, tags);
        if (failures.Length > 0)
        {
            return ValidationFailure.BadRequest(failures);
        }

        profile = await service.UpdateAsync(profile!);
        return profile is null ? Results.NotFound() : Results.Accepted($"releaseprofile/{id}", ReleaseProfileResource.From(profile));
    }

    private static async Task<IResult> Delete(int id, ReleaseProfileService service) =>
        await service.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();

    private static async Task<(ReleaseProfile? Profile, ValidationFailure[] Failures)> ValidateAsync(ReleaseProfileResource resource, int id, GenerralDbContext db, TagService tags)
    {
        var failures = new List<ValidationFailure>();
        var required = ReleaseProfileResource.Terms(resource.Required);
        var ignored = ReleaseProfileResource.Terms(resource.Ignored);

        if (required is null)
        {
            failures.Add(new ValidationFailure("required", "Invalid field required, should be string or string array"));
        }

        if (ignored is null)
        {
            failures.Add(new ValidationFailure("ignored", "Invalid field ignored, should be string or string array"));
        }

        if (required is { Count: 0 } && ignored is { Count: 0 })
        {
            failures.Add(new ValidationFailure("required", "'Must contain' or 'Must not contain' is required"));
        }

        if (required?.Any(string.IsNullOrWhiteSpace) == true)
        {
            failures.Add(new ValidationFailure("required", "'Must contain' should not contain whitespaces or an empty string"));
        }

        if (ignored?.Any(string.IsNullOrWhiteSpace) == true)
        {
            failures.Add(new ValidationFailure("ignored", "'Must not contain' should not contain whitespaces or an empty string"));
        }

        if (resource.Enabled && resource.IndexerId != 0 && !await db.Indexers.AnyAsync(i => i.Id == resource.IndexerId))
        {
            failures.Add(new ValidationFailure("indexerId", "Indexer does not exist"));
        }

        if (await TagEndpoints.ValidateTagIdsAsync(tags, resource.Tags.Concat(resource.ExcludedTags)) is { } tagFailure)
        {
            failures.Add(tagFailure);
        }

        if (resource.Tags.Intersect(resource.ExcludedTags).Any())
        {
            failures.Add(new ValidationFailure("excludedTags", "A tag cannot be in both 'Tags' and 'Excluded Tags'"));
        }

        if (failures.Count > 0)
        {
            return (null, [.. failures]);
        }

        return (new ReleaseProfile
        {
            Id = id,
            Name = string.IsNullOrWhiteSpace(resource.Name) ? null : resource.Name.Trim(),
            Enabled = resource.Enabled,
            Required = required!,
            Ignored = ignored!,
            IndexerId = resource.IndexerId,
            Tags = resource.Tags.Distinct().ToList(),
            ExcludedTags = resource.ExcludedTags.Distinct().ToList(),
        }, []);
    }
}
