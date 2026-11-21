using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Mappings;
using Generral.Core.Sources;
using Generral.Core.Templating;
using Generral.Core.Tv;

namespace Generral.Core.Test.Mappings;

public class MappingTemplateModelTests
{
    [Test]
    public void Exposes_event_source_and_series()
    {
        var source = new Source { Name = "TheSportsDB – UFC", Type = "thesportsdb" };
        var series = new Series { Title = "UFC", SortTitle = "ufc", CleanTitle = "ufc", TitleSlug = "ufc", Path = "/tv/UFC", RootFolderPath = "/tv", Year = 1993 };
        var ev = new Event
        {
            Key = "e1",
            Title = "UFC 314",
            Hash = "",
            Status = EventStatus.Scheduled,
            Start = new DateTime(2025, 4, 12, 22, 0, 0, DateTimeKind.Utc),
            Fields = new JsonObject { ["strVenue"] = "Kaseya Center" },
            Images = new ImageSet { Poster = "p.jpg" },
        };
        var shared = MappingTemplateModel.Create(series, source, new JsonObject { ["strPoster"] = "league.jpg" });

        var rendered = new TemplateRenderer().Render(
            "{{ event.key }}|{{ event.title }}|{{ event.status }}|{{ event.start | date.year }}|{{ event.fields.strVenue }}|{{ event.images.thumb ?? event.images.poster }}|"
            + "{{ event.source.type }}|{{ source.name }}|{{ source.meta.strPoster }}|{{ series.title }} ({{ series.year }})",
            MappingTemplateModel.ForEvent(shared, ev, source));

        Assert.That(rendered, Is.EqualTo("e1|UFC 314|scheduled|2025|Kaseya Center|p.jpg|thesportsdb|TheSportsDB – UFC|league.jpg|UFC (1993)"));
    }
}
