using System.Text.Json;
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Extensions;
using Generral.Core.Mappings;
using Generral.Core.Messaging;
using Generral.Core.Presets;
using Generral.Core.Sources;
using Generral.Core.Sources.Http;
using Generral.Core.Sources.Types;
using Generral.Core.Tags;
using Generral.Core.Tv;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Generral.Http.Api;

public sealed record PresetParameterResource(string Name, string Label, string? Default, bool Secret);

public sealed record PresetResource(
    string Id,
    string Name,
    string? Description,
    string? SeriesTitle,
    IReadOnlyList<string> Tags,
    string SourceType,
    IReadOnlyList<string> SourceTypes,
    IReadOnlyList<string> RequiredSecrets,
    IReadOnlyList<PresetParameterResource> Parameters,
    bool Shipped);

public sealed record PresetImportRequest(string? Url, string? Yaml);

public sealed record PresetInstantiateRequest
{
    public string? Title { get; init; }

    public string? RootFolderPath { get; init; }

    public string? Path { get; init; }

    public bool? Monitored { get; init; }

    public NewItemMonitorType? MonitorNewItems { get; init; }

    public bool? SeasonFolder { get; init; }

    public int QualityProfileId { get; init; }

    public List<int> Tags { get; init; } = [];

    public Dictionary<string, string?> Parameters { get; init; } = [];

    public Dictionary<string, string?> Secrets { get; init; } = [];
}

public static class PresetEndpoints
{
    public static void MapPresetEndpoints(this IEndpointRouteBuilder api)
    {
        var presets = api.MapGroup("/preset");

        presets.MapGet("/", (PresetStore store) => store.All().Select(p => ToResource(p.Document, p.Shipped)));
        presets.MapPost("/import", Import);
        presets.MapPost("/{id}/instantiate", Instantiate);
    }

    /// <summary>The body is the YAML document, or JSON <c>{ url }</c> / <c>{ yaml }</c>.</summary>
    private static async Task<IResult> Import(HttpRequest request, PresetStore store, SourceFetcherFactory fetchers, CancellationToken cancellationToken)
    {
        string text;
        using (var reader = new StreamReader(request.Body))
        {
            text = await reader.ReadToEndAsync(cancellationToken);
        }

        if (request.HasJsonContentType())
        {
            PresetImportRequest? body;
            try
            {
                body = JsonSerializer.Deserialize<PresetImportRequest>(text, JsonSerializerOptions.Web);
            }
            catch (JsonException e)
            {
                return ValidationFailure.BadRequest(new ValidationFailure("body", e.Message));
            }

            if (!string.IsNullOrWhiteSpace(body?.Url))
            {
                if (!Uri.TryCreate(body.Url.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https"))
                {
                    return ValidationFailure.BadRequest(new ValidationFailure("url", "Must be an http(s) URL"));
                }

                try
                {
                    text = (await fetchers.Create(new SourceRequestSettings { BypassCache = true }).FetchAsync(url, cancellationToken)).Content;
                }
                catch (Exception e) when (e is HttpRequestException or TimeoutException)
                {
                    return ValidationFailure.BadRequest(new ValidationFailure("url", e.Message));
                }
            }
            else
            {
                text = body?.Yaml ?? "";
            }
        }

        try
        {
            var document = store.Import(text);
            return Results.Created($"preset/{document.Id}", ToResource(document, shipped: false));
        }
        catch (PresetException e)
        {
            return ValidationFailure.BadRequest(new ValidationFailure("preset", e.Message));
        }
    }

    /// <summary>
    /// Creates the preset's sources (reusing an existing source of the same type with identical settings), the series and its
    /// mappings in one transaction, then queues a refresh of the sources (which applies the mappings).
    /// </summary>
    private static async Task<IResult> Instantiate(
        string id,
        PresetInstantiateRequest request,
        PresetStore store,
        GenerralDbContext db,
        SourceTypeRegistry types,
        MappingEngine engine,
        TagService tags,
        CommandQueue queue,
        TimeProvider time)
    {
        if (store.Find(id) is not { } stored)
        {
            return Results.NotFound();
        }

        var values = new Dictionary<string, string?>(request.Parameters ?? []);
        foreach (var (name, value) in request.Secrets ?? [])
        {
            values[name] = value;
        }

        ResolvedPreset preset;
        try
        {
            preset = stored.Document.Resolve(values);
            foreach (var mapping in preset.Mappings)
            {
                engine.Validate(mapping.Parsed);
            }
        }
        catch (Exception e) when (e is PresetException or MappingDefinitionException)
        {
            return ValidationFailure.BadRequest(new ValidationFailure("preset", e.Message));
        }

        var failures = new List<ValidationFailure>();
        foreach (var source in preset.Sources)
        {
            if (types.Find(source.Type) is not { } type)
            {
                failures.Add(new ValidationFailure("source.type", $"Unknown source type '{source.Type}'"));
                continue;
            }

            failures.AddRange(type.Validate(source.Settings).Select(f => new ValidationFailure($"source.settings.{f.PropertyName}", f.ErrorMessage)));
        }

        var series = await SeriesEndpoints.WithDefaultQualityProfileAsync(SeriesResource(preset.Series, request), db);
        failures.AddRange(await SeriesEndpoints.ValidateAsync(series, null, db, tags));
        if (failures.Count > 0)
        {
            return ValidationFailure.BadRequest([.. failures]);
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        var sources = new Dictionary<PresetSource, Source>();
        foreach (var source in preset.Sources)
        {
            sources[source] = await FindOrAddSourceAsync(db, types, source);
        }

        var created = await SeriesEndpoints.AddAsync(series, db, time);
        foreach (var definition in preset.Mappings)
        {
            var mapping = new Mapping
            {
                Name = definition.Name,
                SeriesId = created.Id,
                SourceId = sources[definition.Source].Id,
                Priority = definition.Priority,
                Definition = YamlJson.ToYaml(definition.Definition),
            };
            db.Mappings.Add(mapping);
            await MappingHistory.RecordAsync(db, mapping);
        }

        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        foreach (var source in sources.Values.DistinctBy(s => s.Id))
        {
            queue.Enqueue(RefreshSourceHandler.CommandName, new JsonObject { ["sourceId"] = source.Id });
        }

        return Results.Created($"series/{created.Id}", await SeriesEndpoints.ToResourceAsync(db, created, time));
    }

    /// <summary>The series as a preset: password settings and header values become <c>{{ secret.NAME }}</c> placeholders.</summary>
    internal static async Task<IResult> Export(int id, GenerralDbContext db, SourceTypeRegistry types)
    {
        var series = await db.Series.AsNoTracking().SingleOrDefaultAsync(s => s.Id == id);
        if (series is null)
        {
            return Results.NotFound();
        }

        var mappings = await db.Mappings.AsNoTracking().Include(m => m.Source).Where(m => m.SeriesId == id).OrderBy(m => m.Priority).ThenBy(m => m.Id).ToListAsync();
        var secrets = new List<string>();
        var sources = mappings.Select(m => m.Source!).DistinctBy(s => s.Id).Select(s => ExportSource(s, types.Find(s.Type), secrets)).ToList();
        var exported = new JsonObject
        {
            ["schema"] = PresetDocument.SchemaName,
            ["id"] = series.TitleSlug,
            ["name"] = series.Title,
            ["description"] = series.Overview,
            ["parameters"] = new JsonArray([.. secrets.Select(s => new JsonObject { ["name"] = s, ["label"] = s, ["secret"] = true })]),
        };
        exported[sources.Count == 1 ? "source" : "sources"] = sources.Count == 1 ? sources[0] : new JsonArray([.. sources]);
        exported["series"] = ExportSeries(series);

        var definitions = new JsonArray();
        foreach (var mapping in mappings)
        {
            JsonObject definition;
            try
            {
                definition = YamlJson.Parse(mapping.Definition) as JsonObject ?? [];
            }
            catch (FormatException)
            {
                definition = [];
            }

            var item = new JsonObject { ["name"] = mapping.Name, ["priority"] = mapping.Priority };
            if (sources.Count > 1)
            {
                item["source"] = mapping.Source!.Name;
            }

            foreach (var key in new[] { "name", "priority", "source", "series" })
            {
                definition.Remove(key);
            }

            foreach (var (key, value) in definition)
            {
                item[key] = value?.DeepClone();
            }

            definitions.Add(item);
        }

        exported[definitions.Count == 1 ? "mapping" : "mappings"] = definitions.Count == 1 ? definitions[0]!.DeepClone() : definitions;
        foreach (var key in exported.Where(p => p.Value is null).Select(p => p.Key).ToList())
        {
            exported.Remove(key);
        }

        return Results.Text(YamlJson.ToYaml(exported), "application/yaml");
    }

    private static PresetResource ToResource(PresetDocument document, bool shipped) => new(
        document.Id,
        document.Name,
        document.Description,
        document.SeriesTitle,
        document.Tags,
        document.SourceTypes[0],
        document.SourceTypes,
        document.RequiredSecrets,
        document.Parameters.Select(p => new PresetParameterResource(p.Name, p.Label, p.Default, p.Secret)).ToList(),
        shipped);

    private static SeriesResource SeriesResource(PresetSeries preset, PresetInstantiateRequest request) => new()
    {
        Title = string.IsNullOrWhiteSpace(request.Title) ? preset.Title : request.Title.Trim(),
        RootFolderPath = request.RootFolderPath,
        Path = request.Path,
        Overview = preset.Overview,
        Network = preset.Network,
        Genres = preset.Genres,
        Year = preset.Year ?? 0,
        SeriesType = preset.SeriesType ?? SeriesType.Standard,
        SeasonFolder = request.SeasonFolder ?? preset.SeasonFolder ?? true,
        Monitored = request.Monitored ?? preset.Monitored ?? true,
        MonitorNewItems = request.MonitorNewItems ?? preset.MonitorNewItems ?? NewItemMonitorType.All,
        QualityProfileId = request.QualityProfileId,
        Tags = request.Tags ?? [],
        SearchSettings = preset.SearchSettings,
    };

    private static async Task<Source> FindOrAddSourceAsync(GenerralDbContext db, SourceTypeRegistry types, PresetSource preset)
    {
        var type = types.Get(preset.Type).Id;
        var candidates = await db.Sources.Where(s => s.Type == type).ToListAsync();
        if (candidates.FirstOrDefault(s => JsonNode.DeepEquals(s.Settings, preset.Settings)) is { } existing)
        {
            return existing;
        }

        var names = (await db.Sources.Select(s => s.Name).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = preset.Name;
        for (var i = 2; names.Contains(name); i++)
        {
            name = $"{preset.Name} ({i})";
        }

        var source = new Source
        {
            Name = name,
            Type = type,
            Settings = (JsonObject)preset.Settings.DeepClone(),
            RefreshInterval = preset.RefreshInterval is { } interval && interval >= SourceRefreshService.MinRefreshInterval ? interval : Source.DefaultRefreshInterval,
        };
        db.Sources.Add(source);
        await db.SaveChangesAsync();
        return source;
    }

    private static JsonObject ExportSource(Source source, ISourceType? type, List<string> secrets)
    {
        var settings = (JsonObject)source.Settings.DeepClone();
        string Secret(string name)
        {
            var unique = name;
            for (var i = 2; secrets.Contains(unique); i++)
            {
                unique = $"{name}{i}";
            }

            secrets.Add(unique);
            return $"{{{{ secret.{unique} }}}}";
        }

        foreach (var field in type?.Fields.Where(f => f.Type == SourceFieldType.Password) ?? [])
        {
            if (settings[field.Name] is not null)
            {
                settings[field.Name] = Secret(field.Name);
            }
        }

        switch (settings[SourceSettings.HeadersField])
        {
            case JsonObject headers:
                foreach (var key in headers.Select(p => p.Key).ToList())
                {
                    headers[key] = Secret("header" + new string(key.Where(char.IsAsciiLetterOrDigit).ToArray()));
                }

                break;
            case JsonArray headers:
                foreach (var header in headers.OfType<JsonObject>())
                {
                    header["value"] = Secret("header" + new string((header["key"]?.ToString() ?? "").Where(char.IsAsciiLetterOrDigit).ToArray()));
                }

                break;
        }

        return new JsonObject
        {
            ["name"] = source.Name,
            ["type"] = source.Type,
            ["settings"] = settings,
            ["refreshInterval"] = $"{(int)source.RefreshInterval.TotalMinutes}m",
        };
    }

    private static JsonObject ExportSeries(Series series)
    {
        var result = new JsonObject
        {
            ["title"] = series.Title,
            ["overview"] = series.Overview,
            ["network"] = series.Network,
            ["genres"] = new JsonArray([.. series.Genres.Select(g => (JsonNode)g)]),
            ["year"] = series.Year,
            ["seriesType"] = JsonNamingPolicy.CamelCase.ConvertName(series.SeriesType.ToString()),
            ["seasonFolder"] = series.SeasonFolder,
            ["monitored"] = series.Monitored,
            ["monitorNewItems"] = JsonNamingPolicy.CamelCase.ConvertName(series.MonitorNewItems.ToString()),
            ["searchSettings"] = series.SearchSettings.Count > 0 ? series.SearchSettings.DeepClone() : null,
        };
        foreach (var key in result.Where(p => p.Value is null).Select(p => p.Key).ToList())
        {
            result.Remove(key);
        }

        return result;
    }
}
