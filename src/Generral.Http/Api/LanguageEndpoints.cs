// SPDX-FileCopyrightText: 2010-2026 Sonarr Team and contributors
// SPDX-FileCopyrightText: The Generral contributors
// SPDX-License-Identifier: GPL-3.0-or-later
//
// Ported from Sonarr@51a2412af235 src/Sonarr.Api.V3/Profiles/Languages/LanguageController.cs
using Generral.Core.Languages;
using Microsoft.AspNetCore.Routing;

namespace Generral.Http.Api;

public static class LanguageEndpoints
{
    public static void MapLanguageEndpoints(this IEndpointRouteBuilder api)
    {
        var languages = api.MapGroup("/language");

        // Like Sonarr: non-positive ids (Any, Original, Unknown) first, then by name.
        languages.MapGet("/", () => Language.All.Prepend(Language.Any).OrderBy(l => l.Id > 0).ThenBy(l => l.Name, StringComparer.Ordinal).ToList());
        languages.MapGet("/{id:int}", (int id) => Language.Find(id) is { } language ? Results.Ok(language) : Results.NotFound());
    }
}
