// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V5/Tags/TagController.cs
using System.Text.RegularExpressions;
using Generral.Core.Tags;
using Microsoft.AspNetCore.Routing;

namespace Generral.Http.Api;

public sealed record TagResource
{
    public int Id { get; init; }

    public string? Label { get; init; }

    public static TagResource From(Tag tag) => new() { Id = tag.Id, Label = tag.Label };
}

public sealed record TagDetailsResource(int Id, string Label, IReadOnlyList<int> SourceIds, IReadOnlyList<int> SeriesIds)
{
    public static TagDetailsResource From(TagDetails details) => new(details.Id, details.Label, details.SourceIds, details.SeriesIds);
}

public static partial class TagEndpoints
{
    public static void MapTagEndpoints(this IEndpointRouteBuilder api)
    {
        var tags = api.MapGroup("/tag");

        tags.MapGet("/", async (TagService service) => (await service.AllAsync()).Select(TagResource.From));
        tags.MapGet("/{id:int}", Get);
        tags.MapPost("/", Create);
        tags.MapPut("/{id:int}", Update);
        tags.MapDelete("/{id:int}", Delete);
        tags.MapGet("/detail", async (TagService service) => (await service.DetailsAsync()).Select(TagDetailsResource.From));
        tags.MapGet("/detail/{id:int}", GetDetails);
    }

    /// <summary>Validation failures for <c>tags</c> ids that don't exist.</summary>
    public static async Task<ValidationFailure?> ValidateTagIdsAsync(TagService service, IEnumerable<int>? ids)
    {
        var missing = await service.MissingAsync(ids ?? []);
        return missing.Count == 0 ? null : new ValidationFailure("tags", $"Unknown tag id(s): {string.Join(", ", missing)}");
    }

    private static async Task<IResult> Get(int id, TagService service) =>
        await service.GetAsync(id) is { } tag ? Results.Ok(TagResource.From(tag)) : Results.NotFound();

    private static async Task<IResult> GetDetails(int id, TagService service) =>
        (await service.DetailsAsync()).SingleOrDefault(d => d.Id == id) is { } details ? Results.Ok(TagDetailsResource.From(details)) : Results.NotFound();

    private static async Task<IResult> Create(TagResource resource, TagService service)
    {
        if (Validate(resource) is { } failure)
        {
            return ValidationFailure.BadRequest(failure);
        }

        var tag = await service.AddAsync(resource.Label!);
        return Results.Created($"tag/{tag.Id}", TagResource.From(tag));
    }

    private static async Task<IResult> Update(int id, TagResource resource, TagService service)
    {
        if (Validate(resource) is { } failure)
        {
            return ValidationFailure.BadRequest(failure);
        }

        if (await service.FindByLabelAsync(resource.Label!) is { } existing && existing.Id != id)
        {
            return ValidationFailure.BadRequest(new ValidationFailure("label", "Should be unique"));
        }

        var tag = await service.UpdateAsync(id, resource.Label!);
        return tag is null ? Results.NotFound() : Results.Accepted($"tag/{tag.Id}", TagResource.From(tag));
    }

    private static async Task<IResult> Delete(int id, TagService service) =>
        await service.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();

    private static ValidationFailure? Validate(TagResource resource)
    {
        if (string.IsNullOrEmpty(resource.Label))
        {
            return new ValidationFailure("label", "'Label' must not be empty.");
        }

        return LabelPattern().IsMatch(resource.Label) ? null : new ValidationFailure("label", "Allowed characters a-z, 0-9 and -");
    }

    [GeneratedRegex("^[a-z0-9-]+$", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPattern();
}
