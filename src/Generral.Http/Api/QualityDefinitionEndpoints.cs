// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Qualities/QualityDefinitionController.cs, QualityDefinitionResource.cs,
// QualityDefinitionLimitsResource.cs and QualityDefinitionResourceValidator.cs
using Generral.Core.Qualities;
using Microsoft.AspNetCore.Routing;

namespace Generral.Http.Api;

public sealed record QualityDefinitionResource
{
    public int Id { get; init; }

    public Quality? Quality { get; init; }

    public string? Title { get; init; }

    public int Weight { get; init; }

    public double? MinSize { get; init; }

    public double? MaxSize { get; init; }

    public double? PreferredSize { get; init; }

    public static QualityDefinitionResource From(QualityDefinition definition) => new()
    {
        Id = definition.Id,
        Quality = definition.Quality,
        Title = definition.Title,
        Weight = definition.Weight,
        MinSize = definition.MinSize,
        MaxSize = definition.MaxSize,
        PreferredSize = definition.PreferredSize,
    };

    public QualityDefinition ToModel(int id) => new()
    {
        Id = id,
        Title = Title!.Trim(),
        MinSize = MinSize,
        MaxSize = MaxSize,
        PreferredSize = PreferredSize,
    };
}

public sealed record QualityDefinitionLimitsResource(int Min, int Max);

public static class QualityDefinitionEndpoints
{
    public static void MapQualityDefinitionEndpoints(this IEndpointRouteBuilder api)
    {
        var definitions = api.MapGroup("/qualitydefinition");

        definitions.MapGet("/", async (QualityDefinitionService service) => (await service.AllAsync()).Select(QualityDefinitionResource.From));
        definitions.MapGet("/{id:int}", Get);
        definitions.MapPut("/{id:int}", Update);
        definitions.MapPut("/update", UpdateMany);
        definitions.MapGet("/limits", () => new QualityDefinitionLimitsResource(QualityDefinition.MinLimit, QualityDefinition.MaxLimit));
    }

    private static async Task<IResult> Get(int id, QualityDefinitionService service) =>
        await service.GetAsync(id) is { } definition ? Results.Ok(QualityDefinitionResource.From(definition)) : Results.NotFound();

    private static async Task<IResult> Update(int id, QualityDefinitionResource resource, QualityDefinitionService service)
    {
        if (Validate(resource) is { } failure)
        {
            return ValidationFailure.BadRequest(failure);
        }

        var updated = await service.UpdateManyAsync([resource.ToModel(id)]);
        return updated.Count == 0 ? Results.NotFound() : Results.Accepted($"qualitydefinition/{id}", QualityDefinitionResource.From(updated[0]));
    }

    private static async Task<IResult> UpdateMany(List<QualityDefinitionResource> resources, QualityDefinitionService service)
    {
        var failures = resources.Select(Validate).OfType<ValidationFailure>().ToArray();
        if (failures.Length > 0)
        {
            return ValidationFailure.BadRequest(failures);
        }

        await service.UpdateManyAsync(resources.Select(r => r.ToModel(r.Id)).ToList());
        return Results.Accepted("qualitydefinition", (await service.AllAsync()).Select(QualityDefinitionResource.From));
    }

    private static ValidationFailure? Validate(QualityDefinitionResource resource)
    {
        if (string.IsNullOrWhiteSpace(resource.Title))
        {
            return new ValidationFailure("title", "'Title' must not be empty.");
        }

        return QualityProfileEndpoints.SizeError(resource.MinSize, resource.PreferredSize, resource.MaxSize) is { } error
            ? new ValidationFailure("minSize", error)
            : null;
    }
}
