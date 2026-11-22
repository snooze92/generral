// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Profiles/Quality/QualityProfileController.cs, QualityProfileResource.cs,
// QualityProfileSchemaController.cs, QualityCutoffValidator.cs, QualityItemsValidator.cs and QualityProfileResourceValidator.cs
using Generral.Core.Languages;
using Generral.Core.Profiles;
using Generral.Core.Qualities;
using Microsoft.AspNetCore.Routing;

namespace Generral.Http.Api;

/// <summary>Sonarr's <c>QualityProfileResource</c>; custom format fields are fixed until custom formats land.</summary>
public sealed record QualityProfileResource
{
    public int Id { get; init; }

    public string? Name { get; init; }

    public bool UpgradeAllowed { get; init; }

    public int Cutoff { get; init; }

    public List<QualityProfileQualityItem>? Items { get; init; }

    /// <summary>Radarr's profile language; defaults to English when omitted.</summary>
    public Language? Language { get; init; }

    public int MinFormatScore { get; init; }

    public int CutoffFormatScore { get; init; }

    public int MinUpgradeFormatScore { get; init; } = 1;

    public List<object> FormatItems { get; init; } = [];

    public static QualityProfileResource From(QualityProfile profile) => new()
    {
        Id = profile.Id,
        Name = profile.Name,
        UpgradeAllowed = profile.UpgradeAllowed,
        Cutoff = profile.Cutoff,
        Items = profile.Items,
        Language = profile.Language,
    };

    public QualityProfile ToModel(int id) => new()
    {
        Id = id,
        Name = Name!.Trim(),
        UpgradeAllowed = UpgradeAllowed,
        Cutoff = Cutoff,
        Items = Items!,
        Language = Language ?? Language.English,
    };
}

public static class QualityProfileEndpoints
{
    public static void MapQualityProfileEndpoints(this IEndpointRouteBuilder api)
    {
        var profiles = api.MapGroup("/qualityprofile");

        profiles.MapGet("/", async (QualityProfileService service) => (await service.AllAsync()).Select(QualityProfileResource.From));
        profiles.MapGet("/{id:int}", Get);
        profiles.MapPost("/", Create);
        profiles.MapPut("/{id:int}", Update);
        profiles.MapDelete("/{id:int}", Delete);
        profiles.MapGet("/schema", async (QualityDefinitionService definitions) =>
            QualityProfileResource.From(QualityProfileService.GetDefaultProfile(string.Empty, await definitions.AllAsync())));
    }

    private static async Task<IResult> Get(int id, QualityProfileService service) =>
        await service.GetAsync(id) is { } profile ? Results.Ok(QualityProfileResource.From(profile)) : Results.NotFound();

    private static async Task<IResult> Create(QualityProfileResource resource, QualityProfileService service)
    {
        if (await ValidateAsync(resource, 0, service) is { Length: > 0 } failures)
        {
            return ValidationFailure.BadRequest(failures);
        }

        var profile = await service.AddAsync(resource.ToModel(0));
        return Results.Created($"qualityprofile/{profile.Id}", QualityProfileResource.From(profile));
    }

    private static async Task<IResult> Update(int id, QualityProfileResource resource, QualityProfileService service)
    {
        if (await ValidateAsync(resource, id, service) is { Length: > 0 } failures)
        {
            return ValidationFailure.BadRequest(failures);
        }

        var profile = await service.UpdateAsync(resource.ToModel(id));
        return profile is null ? Results.NotFound() : Results.Accepted($"qualityprofile/{id}", QualityProfileResource.From(profile));
    }

    private static async Task<IResult> Delete(int id, QualityProfileService service)
    {
        try
        {
            return await service.DeleteAsync(id) ? Results.NoContent() : Results.NotFound();
        }
        catch (QualityProfileInUseException e)
        {
            return ValidationFailure.BadRequest(new ValidationFailure(string.Empty, e.Message));
        }
    }

    private static async Task<ValidationFailure[]> ValidateAsync(QualityProfileResource resource, int id, QualityProfileService service)
    {
        var failures = new List<ValidationFailure>();
        if (string.IsNullOrWhiteSpace(resource.Name))
        {
            failures.Add(new ValidationFailure("name", "'Name' must not be empty."));
        }
        else if ((await service.AllAsync()).Any(p => p.Id != id && string.Equals(p.Name, resource.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            failures.Add(new ValidationFailure("name", "Should be unique"));
        }

        var items = resource.Items ?? [];
        if (ItemsError(items) is { } itemsError)
        {
            failures.Add(new ValidationFailure("items", itemsError));
            return [.. failures];
        }

        var cutoff = items.SingleOrDefault(i => (i.Quality is null && i.Id == resource.Cutoff) || i.Quality?.Id == resource.Cutoff);
        if (cutoff is not { Allowed: true })
        {
            failures.Add(new ValidationFailure("cutoff", "Cutoff must be an allowed quality or group"));
        }

        foreach (var item in items.SelectMany(i => i.Quality is null ? i.Items : [i]))
        {
            if (SizeError(item) is { } sizeError)
            {
                failures.Add(new ValidationFailure("items", $"{item.Quality?.Name}: {sizeError}"));
            }
        }

        return [.. failures];
    }

    private static string? ItemsError(List<QualityProfileQualityItem> items)
    {
        if (!items.Any(i => i.Allowed))
        {
            return "Must contain at least one allowed quality";
        }

        if (items.Any(i => i.Quality is not null && !string.IsNullOrWhiteSpace(i.Name)))
        {
            return "Individual qualities should not be named";
        }

        if (items.Any(i => i.Quality is null && string.IsNullOrWhiteSpace(i.Name)))
        {
            return "Groups must have a name";
        }

        if (items.Any(i => i.Quality is null && (i.Items.Count <= 1 || i.Items.Any(g => g.Quality is null))))
        {
            return "Groups must contain multiple qualities";
        }

        if (items.Any(i => i.Quality is null && i.Id == 0))
        {
            return "Groups must have an ID";
        }

        if (items.Where(i => i.Id > 0).GroupBy(i => i.Id).Any(g => g.Count() > 1))
        {
            return "Groups must have a unique ID";
        }

        var qualityIds = items.SelectMany(i => i.GetQualities()).Select(q => q.Id).ToList();
        if (qualityIds.Count != qualityIds.Distinct().Count())
        {
            return "Qualities can only be used once";
        }

        return Quality.All.All(q => qualityIds.Contains(q.Id)) ? null : "Must contain all qualities";
    }

    internal static string? SizeError(QualityProfileQualityItem item) => SizeError(item.MinSize, item.PreferredSize, item.MaxSize);

    internal static string? SizeError(double? min, double? preferred, double? max)
    {
        var values = new[] { min, preferred, max };
        if (values.Any(v => v is < QualityDefinition.MinLimit or > QualityDefinition.MaxLimit))
        {
            return $"Sizes must be between {QualityDefinition.MinLimit} and {QualityDefinition.MaxLimit}";
        }

        var set = values.OfType<double>().ToList();
        return set.Zip(set.Skip(1)).Any(pair => pair.First > pair.Second) ? "Sizes must be ordered min ≤ preferred ≤ max" : null;
    }
}
