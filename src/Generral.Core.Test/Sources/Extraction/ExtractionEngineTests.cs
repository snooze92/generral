using Generral.Core.Sources;
using Generral.Core.Sources.Extraction;
using Generral.Core.Templating;

namespace Generral.Core.Test.Sources.Extraction;

public class ExtractionEngineTests
{
    private const string Fields = """
        fields:
          key:    { path: "$.id" }
          title:  { path: "$.name" }
        """;

    private static readonly ExtractionEngine Engine = new(new TemplateRenderer(), new FixedTime(new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc)));

    private static Task<ExtractionResult> Extract(string yaml, InMemoryDocumentFetcher fetcher, ExtractionOptions? options = null) =>
        Engine.ExtractAsync(ExtractionDefinitionParser.FromYaml(yaml), fetcher, options);

    [Test]
    public async Task Extracts_fields_with_paths_templates_and_filters()
    {
        var fetcher = new InMemoryDocumentFetcher().Add("https://api.example.org/events", """
            {"data": [
              {"id": 300, "name": "UFC 300: Pereira vs. Hill", "starts_at": "2024-04-13T22:00:00-07:00",
               "venue": {"name": "T-Mobile Arena"}, "images": {"poster": "/img/300.jpg"}, "status": "FT", "tags": ["ppv", "vegas"]},
              {"id": 301, "name": "UFC Fight Night", "starts_at": "2024-04-20 19:00", "venue": {"name": "APEX"}, "status": "NS"}
            ]}
            """);

        var result = await Extract("""
            requests: [ "https://api.example.org/events" ]
            items: "$.data[*]"
            fields:
              key:    { path: "$.id" }
              title:  { path: "$.name" }
              start:  { path: "$.starts_at", filters: [ { name: dateparse, args: "o" } ] }
              venue:  { path: "$.venue.name" }
              number: { path: "$.name", filters: [ { name: regexp, args: "^UFC (\\d+)" }, toint ], optional: true }
              slug:   { text: "{{ venue | string.slugify }}-{{ number ?? 'fn' }}" }
              status: { path: "$.status" }
              poster: { path: "$.images.poster", optional: true }
              tags:   { path: "$.tags[*]", optional: true }
              kind:   { path: "$.kind", default: "event" }
            """, fetcher, new ExtractionOptions { Timezone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York") });

        var events = result.Events.ToList();
        Assert.That(result.Items.SelectMany(i => i.Errors), Is.Empty);
        Assert.Multiple(() =>
        {
            Assert.That(events.Select(e => e.Key), Is.EqualTo(new[] { "300", "301" }));
            Assert.That(events[0].Title, Is.EqualTo("UFC 300: Pereira vs. Hill"));
            Assert.That(events[0].Start, Is.EqualTo(new DateTime(2024, 4, 14, 5, 0, 0, DateTimeKind.Utc)));
            Assert.That(events[1].Start, Is.EqualTo(new DateTime(2024, 4, 20, 23, 0, 0, DateTimeKind.Utc)), "offset-less dates use the source timezone");
            Assert.That(events[1].Timezone, Is.EqualTo("America/New_York"));
            Assert.That(events[0].Status, Is.EqualTo(EventStatus.Completed));
            Assert.That(events[1].Status, Is.EqualTo(EventStatus.Scheduled));
            Assert.That(events[0].Images.Poster, Is.EqualTo("https://api.example.org/img/300.jpg"));
            Assert.That(events[1].Images.Poster, Is.Null);
            Assert.That(events[0].Fields["number"]!.GetValue<long>(), Is.EqualTo(300));
            Assert.That(events[1].Fields["number"], Is.Null);
            Assert.That(events[0].Fields["slug"]!.GetValue<string>(), Is.EqualTo("t-mobile-arena-300"));
            Assert.That(events[1].Fields["slug"]!.GetValue<string>(), Is.EqualTo("apex-fn"));
            Assert.That(events[0].Fields["start"]!.GetValue<string>(), Is.EqualTo("2024-04-14T05:00:00Z"));
            Assert.That(events[0].Fields["tags"]!.ToJsonString(), Is.EqualTo("""["ppv","vegas"]"""));
            Assert.That(events[0].Fields["kind"]!.GetValue<string>(), Is.EqualTo("event"));
            Assert.That(events[0].Fields["_raw"]!["venue"]!["name"]!.GetValue<string>(), Is.EqualTo("T-Mobile Arena"));
        });
    }

    [Test]
    public async Task Foreach_expands_url_templates()
    {
        var fetcher = new InMemoryDocumentFetcher();
        foreach (var year in new[] { 2025, 2026 })
        {
            foreach (var league in new[] { "ufc", "pfl" })
            {
                fetcher.Add($"https://x.org/{league}?year={year}", $$"""[{"id": "{{league}}-{{year}}", "name": "{{league}} {{year}}"}]""");
            }
        }

        var result = await Extract($$$"""
            requests:
              - url: "https://x.org/{{ league }}?year={{ year }}"
                foreach:
                  year: "{{ 2025..2026 }}"
                  league: "{{ ['ufc', 'pfl'] }}"
            items: "$[*]"
            fields:
              key:    { path: "$.id" }
              title:  { path: "$.name" }
              season: { text: "{{ vars.year }}", filters: [ toint ] }
            """, fetcher);

        Assert.Multiple(() =>
        {
            Assert.That(result.Events.Select(e => e.Key), Is.EqualTo(new[] { "ufc-2025", "pfl-2025", "ufc-2026", "pfl-2026" }));
            Assert.That(result.Events.Select(e => e.Fields["season"]!.GetValue<long>()), Is.EqualTo(new long[] { 2025, 2025, 2026, 2026 }));
            Assert.That(result.Urls, Has.Count.EqualTo(4));
        });
    }

    [Test]
    public async Task Next_url_pagination()
    {
        var fetcher = new InMemoryDocumentFetcher()
            .Add("https://x.org/e", """{"data": [{"id": 1, "name": "a"}], "links": {"next": "/e?cursor=2"}}""")
            .Add("https://x.org/e?cursor=2", """{"data": [{"id": 2, "name": "b"}], "links": {"next": "https://x.org/e?cursor=3"}}""")
            .Add("https://x.org/e?cursor=3", """{"data": [{"id": 3, "name": "c"}], "links": {"next": null}}""");

        var result = await Extract($"""
            requests: [ {"{"} url: "https://x.org/e", pagination: {"{"} next: "$.links.next" {"}"} {"}"} ]
            items: "$.data[*]"
            {Fields}
            """, fetcher);

        Assert.Multiple(() =>
        {
            Assert.That(result.Events.Select(e => e.Key), Is.EqualTo(new[] { "1", "2", "3" }));
            Assert.That(result.Warnings, Is.Empty);
        });
    }

    [Test]
    public async Task Next_url_pagination_stops_at_max_pages_and_on_loops()
    {
        var fetcher = new InMemoryDocumentFetcher()
            .Add("https://x.org/a", """{"data": [{"id": 1, "name": "a"}], "next": "/b"}""")
            .Add("https://x.org/b", """{"data": [{"id": 2, "name": "b"}], "next": "/c"}""")
            .Add("https://x.org/c", """{"data": [{"id": 3, "name": "c"}], "next": "/a"}""");

        var capped = await Extract($"""
            requests: [ {"{"} url: "https://x.org/a", pagination: {"{"} next: "$.next", max_pages: 2 {"}"} {"}"} ]
            items: "$.data[*]"
            {Fields}
            """, fetcher);
        var looping = await Extract($"""
            requests: [ {"{"} url: "https://x.org/a", pagination: {"{"} next: "$.next" {"}"} {"}"} ]
            items: "$.data[*]"
            {Fields}
            """, fetcher);

        Assert.Multiple(() =>
        {
            Assert.That(capped.Events.Count(), Is.EqualTo(2));
            Assert.That(capped.Warnings.Single(), Does.Contain("max_pages"));
            Assert.That(looping.Events.Count(), Is.EqualTo(3));
            Assert.That(looping.Urls, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task Page_param_pagination_until_empty()
    {
        var fetcher = new InMemoryDocumentFetcher()
            .Add("https://x.org/e?league=1&page=1", """{"data": [{"id": 1, "name": "a"}, {"id": 2, "name": "b"}]}""")
            .Add("https://x.org/e?league=1&page=2", """{"data": [{"id": 3, "name": "c"}]}""")
            .Add("https://x.org/e?league=1&page=3", """{"data": []}""");

        var result = await Extract($"""
            requests: [ {"{"} url: "https://x.org/e?league=1", pagination: {"{"} page_param: page {"}"} {"}"} ]
            items: "$.data[*]"
            {Fields}
            """, fetcher);

        Assert.Multiple(() =>
        {
            Assert.That(result.Events.Select(e => e.Key), Is.EqualTo(new[] { "1", "2", "3" }));
            Assert.That(fetcher.Requested.Select(u => u.Query), Is.EqualTo(new[] { "?league=1&page=1", "?league=1&page=2", "?league=1&page=3" }));
        });
    }

    [Test]
    public async Task Page_param_pagination_with_fixed_page_count()
    {
        var fetcher = new InMemoryDocumentFetcher()
            .Add("https://x.org/e?p=0", """[{"id": 1, "name": "a"}]""")
            .Add("https://x.org/e?p=1", """[]""")
            .Add("https://x.org/e?p=2", """[{"id": 3, "name": "c"}]""");

        var result = await Extract($"""
            requests: [ {"{"} url: "https://x.org/e", pagination: {"{"} page_param: p, start: 0, until_empty: false, max_pages: 3 {"}"} {"}"} ]
            items: "$[*]"
            {Fields}
            """, fetcher);

        Assert.Multiple(() =>
        {
            Assert.That(result.Events.Select(e => e.Key), Is.EqualTo(new[] { "1", "3" }));
            Assert.That(result.Warnings, Is.Empty);
        });
    }

    [Test]
    public async Task Item_errors_are_collected()
    {
        var fetcher = new InMemoryDocumentFetcher().Add("https://x.org/e", """
            [
              {"id": 1, "name": "ok", "date": "2026-01-01"},
              {"name": "no key", "date": "2026-01-01"},
              {"id": 3, "name": "bad date", "date": "soon"},
              {"id": 1, "name": "duplicate", "date": "2026-01-02"},
              {"id": 5, "name": "  ", "date": "2026-01-02"}
            ]
            """);

        var result = await Extract("""
            requests: [ "https://x.org/e" ]
            items: "$[*]"
            fields:
              key:   { path: "$.id" }
              title: { path: "$.name" }
              start: { path: "$.date", filters: [ { name: dateparse, args: "yyyy-MM-dd" } ] }
            """, fetcher);

        var errors = result.Items.Select(i => string.Join("; ", i.Errors)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(result.Events.Select(e => e.Title), Is.EqualTo(new[] { "ok" }));
            Assert.That(errors[0], Is.Empty);
            Assert.That(errors[1], Is.EqualTo("key: no value"));
            Assert.That(errors[2], Does.StartWith("start: dateparse: 'soon'"));
            Assert.That(errors[3], Is.EqualTo("key: duplicate key '1'"));
            Assert.That(errors[4], Is.EqualTo("title: no value"));
            Assert.That(result.Items[2].Fields["title"]!.GetValue<string>(), Is.EqualTo("bad date"), "fields are kept for the preview");
        });
    }

    [Test]
    public async Task Optional_fields_swallow_errors()
    {
        var fetcher = new InMemoryDocumentFetcher().Add("https://x.org/e", """[{"id": 1, "name": "a", "n": "x"}]""");

        var result = await Extract("""
            requests: [ "https://x.org/e" ]
            items: "$[*]"
            fields:
              key:   { path: "$.id" }
              title: { path: "$.name" }
              n:     { path: "$.n", filters: [ toint ], optional: true }
              m:     { path: "$.n", filters: [ toint ], default: 0 }
            """, fetcher);

        var item = result.Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That(item.Errors, Is.Empty);
            Assert.That(item.Fields["n"], Is.Null);
            Assert.That(item.Fields["m"]!.GetValue<long>(), Is.Zero);
        });
    }

    [Test]
    public async Task Template_errors_are_item_errors()
    {
        var fetcher = new InMemoryDocumentFetcher().Add("https://x.org/e", """[{"id": 1, "name": "a"}]""");
        var engine = new ExtractionEngine(new TemplateRenderer(new TemplateLimits { LoopLimit = 5 }));

        var result = await engine.ExtractAsync(ExtractionDefinitionParser.FromYaml("""
            requests: [ "https://x.org/e" ]
            items: "$[*]"
            fields:
              key:   { path: "$.id" }
              title: { text: "{{ for i in 1..10; i; end }}" }
            """), fetcher);

        Assert.That(result.Items.Single().Errors.Single(), Does.StartWith("title:").And.Contains("LoopLimit"));
    }

    [Test]
    public async Task Document_and_url_errors_fail_the_extraction()
    {
        var fetcher = new InMemoryDocumentFetcher().Add("https://x.org/e", "<html>");

        Assert.Multiple(() =>
        {
            Assert.That(() => Extract($"requests: [ \"https://x.org/e\" ]\nitems: \"$[*]\"\n{Fields}", fetcher), Throws.TypeOf<ExtractionException>().With.Message.Contains("invalid JSON"));
            Assert.That(() => Extract($"requests: [ \"file:///etc/passwd\" ]\nitems: \"$[*]\"\n{Fields}", fetcher), Throws.TypeOf<ExtractionException>().With.Message.Contains("Invalid request URL"));
        });
    }

    [Test]
    public async Task Request_cap()
    {
        var fetcher = new InMemoryDocumentFetcher();
        for (var i = 1; i <= 3; i++)
        {
            fetcher.Add($"https://x.org/{i}", "[]");
        }

        var yaml = "requests:\n  - url: \"https://x.org/{{ i }}\"\n    foreach: { i: \"{{ 1..3 }}\" }\nitems: \"$[*]\"\n" + Fields;

        Assert.That(() => Extract(yaml, fetcher, new ExtractionOptions { MaxRequests = 2 }), Throws.TypeOf<ExtractionException>());
    }

    [TestCase("fields:\n  title: { path: $.a }", "fields.key: is required")]
    [TestCase("fields:\n  key: { path: $.a }\n  title: { path: $.b, filters: [ nope ] }", "Unknown filter 'nope'")]
    [TestCase("fields:\n  key: { path: \"$[\" }\n  title: { path: $.b }", "invalid JSONPath")]
    [TestCase("fields:\n  key: { text: \"{{ if }}\" }\n  title: { path: $.b }", "fields.key.text")]
    [TestCase("fields:\n  key: { path: $.a }\n  title: { path: $.b }\n  _raw: { path: $.c }", "reserved")]
    public void Compile_rejects_invalid_definitions(string fields, string message)
    {
        var definition = ExtractionDefinitionParser.FromYaml($"requests: [ \"https://x.org\" ]\nitems: \"$[*]\"\n{fields}");
        Assert.That(() => Engine.Compile(definition), Throws.TypeOf<ExtractionDefinitionException>().With.Message.Contains(message));
    }
}
