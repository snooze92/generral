using System.Text.Json.Nodes;
using Generral.Core.Sources.Types;

namespace Generral.Core.Test.Sources.Types;

public class HttpHtmlSourceTypeTests
{
    [Test]
    public async Task Extracts_table_rows_with_css_selectors()
    {
        using var host = new SourceTestHost();
        host.Handler.Add("https://wiki.example.org/wiki/Events", SourceTestHost.Fixture("http-html/events.html"), "text/html");

        var result = await host.FetchAsync("http-html", new JsonObject
        {
            ["requests"] = "https://wiki.example.org/wiki/Events",
            ["items"] = "table#Scheduled_events tr:has(td)",
            ["fields"] = """
                title:    { selector: "td:nth-child(1)" }
                link:     { selector: "td:nth-child(1) a", attribute: href }
                date:     { selector: "td:nth-child(2)" }
                venue:    { selector: "td:nth-child(3)", remove: sup, optional: true }
                location: { selector: "td:nth-child(4)" }
                key:      { text: "{{ title | string.slugify }}" }
                start:    { text: "{{ date }}", filters: [ { name: dateparse, args: "MMM d, yyyy" } ] }
                number:   { text: "{{ title }}", filters: [ { name: regexp, args: "^XFL (\\d+)$" }, toint ], optional: true }
                """,
        });

        var events = result.Events.ToDictionary(e => e.Key);
        Assert.Multiple(() =>
        {
            Assert.That(result.ItemErrors, Is.Empty);
            Assert.That(events.Keys, Is.EqualTo(new[] { "xfl-12", "xfl-fight-night-doe-vs-roe" }));
            Assert.That(events["xfl-12"].Start, Is.EqualTo(new DateTime(2027, 3, 6, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That((string?)events["xfl-12"].Fields["venue"], Is.EqualTo("Harbour Arena"), "sup removed, whitespace collapsed");
            Assert.That((string?)events["xfl-12"].Fields["location"], Is.EqualTo("Port Elsewhere, Nowhere"));
            Assert.That((string?)events["xfl-12"].Fields["link"], Is.EqualTo("/wiki/XFL_12"));
            Assert.That((long?)events["xfl-12"].Fields["number"], Is.EqualTo(12));
            Assert.That(events["xfl-fight-night-doe-vs-roe"].Fields["venue"], Is.Null);
            Assert.That(events["xfl-fight-night-doe-vs-roe"].Fields["number"], Is.Null);
            Assert.That((string?)events["xfl-12"].Fields["_raw"], Is.EqualTo("XFL 12 Mar 6, 2027 Harbour Arena Port Elsewhere, Nowhere"));
        });
    }

    [Test]
    public async Task Reads_the_html_from_a_json_response_with_html_path()
    {
        using var host = new SourceTestHost();
        host.Handler.Add("https://wiki.example.org/api?section=2", SourceTestHost.Fixture("http-html/parse-section.json"));

        var result = await host.FetchAsync("http-html", new JsonObject
        {
            ["requests"] = """
                - url: "https://wiki.example.org/api?section={{ section }}"
                  foreach: { section: "2" }
                """,
            ["htmlPath"] = "$.parse.text",
            ["items"] = "table tr:has(td)",
            ["fields"] = new JsonObject
            {
                ["number"] = new JsonObject { ["selector"] = "td:nth-child(1)" },
                ["title"] = new JsonObject { ["selector"] = "td:nth-child(2)" },
                ["key"] = new JsonObject { ["text"] = "{{ number }}" },
                ["start"] = new JsonObject { ["selector"] = "td:nth-child(3)", ["filters"] = new JsonArray(new JsonObject { ["name"] = "dateparse", ["args"] = "MMM d, yyyy" }) },
                ["venue"] = new JsonObject { ["selector"] = "td:nth-child(6), td:nth-child(4)" },
            },
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Events.Select(e => (e.Key, e.Title)), Is.EqualTo(new[] { ("2", "XFL 11"), ("1", "XFL 10") }));
            Assert.That(result.Events[1].Start, Is.EqualTo(new DateTime(2026, 12, 12, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That((string?)result.Events[1].Fields["venue"], Is.EqualTo("Hilltop Hall"), "first alternative that matches");
        });
    }

    [Test]
    public void Reports_invalid_selectors_against_the_setting()
    {
        using var host = new SourceTestHost();

        var failures = host.Types.Get("http-html").Validate(new JsonObject
        {
            ["requests"] = "https://wiki.example.org/wiki/Events",
            ["items"] = "tr:has(",
            ["fields"] = "{ key: { selector: td }, title: { path: '$.name' } }",
        });

        Assert.That(failures.Single(), Is.EqualTo(new SourceValidationFailure("items", "items: invalid CSS selector 'tr:has('")));
    }

    [Test]
    public void Html_path_must_yield_a_string()
    {
        using var host = new SourceTestHost();
        host.Handler.Add("https://wiki.example.org/api", """{"parse": {}}""");

        Assert.That(
            () => host.FetchAsync("http-html", new JsonObject
            {
                ["requests"] = "https://wiki.example.org/api",
                ["htmlPath"] = "$.parse.text",
                ["items"] = "tr",
                ["fields"] = "{ key: { selector: td }, title: { selector: td } }",
            }),
            Throws.TypeOf<SourceFetchException>().With.Message.Contains("htmlPath matched no string"));
    }
}
