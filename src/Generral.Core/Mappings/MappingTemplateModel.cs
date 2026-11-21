using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Sources;
using Generral.Core.Tv;

namespace Generral.Core.Mappings;

/// <summary>Builds the template context of a mapping (docs/specs/05-mapping.md § Shape).</summary>
public static class MappingTemplateModel
{
    /// <summary>The variables shared by all events: <c>source.meta</c> and <c>series</c>.</summary>
    public static Dictionary<string, object?> Create(Series series, Source source, JsonObject? sourceMeta) => new()
    {
        ["source"] = new Dictionary<string, object?> { ["name"] = source.Name, ["type"] = source.Type, ["meta"] = sourceMeta ?? [] },
        ["series"] = new Dictionary<string, object?>
        {
            ["title"] = series.Title,
            ["year"] = series.Year,
            ["overview"] = series.Overview,
            ["network"] = series.Network,
            ["genres"] = series.Genres,
        },
    };

    /// <summary>Adds <c>event</c> and an empty <c>vars</c> to a copy of the shared variables.</summary>
    public static Dictionary<string, object?> ForEvent(IReadOnlyDictionary<string, object?> shared, Event ev, Source source) =>
        new(shared)
        {
            ["event"] = new Dictionary<string, object?>
            {
                ["key"] = ev.Key,
                ["title"] = ev.Title,
                ["start"] = ev.Start,
                ["end"] = ev.End,
                ["status"] = ev.Status.ToString().ToLowerInvariant(),
                ["images"] = Images(ev.Images),
                ["fields"] = ev.Fields,
                ["source"] = new Dictionary<string, object?> { ["name"] = source.Name, ["type"] = source.Type },
            },
            ["vars"] = new Dictionary<string, object?>(),
        };

    private static Dictionary<string, object?> Images(ImageSet images) => new()
    {
        ["poster"] = images.Poster,
        ["fanart"] = images.Fanart,
        ["thumb"] = images.Thumb,
        ["banner"] = images.Banner,
        ["logo"] = images.Logo,
    };
}
