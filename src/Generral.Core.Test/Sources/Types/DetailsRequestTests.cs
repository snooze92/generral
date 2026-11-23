using System.Net;
using System.Text.Json.Nodes;
using Generral.Core.Sources.Types;

namespace Generral.Core.Test.Sources.Types;

public class DetailsRequestTests
{
    private const string List = """
        { "data": [ { "id": 1, "name": "XFL 1", "slug": "XFL_1" }, { "id": 2, "name": "XFL 2", "slug": "XFL_2" }, { "id": 3, "name": "XFL 3", "slug": "XFL_3" } ] }
        """;

    private static JsonObject JsonSource(string details) => new()
    {
        ["requests"] = "https://api.example.org/events",
        ["items"] = "$.data[*]",
        ["fields"] = """
            key:   { path: "$.id" }
            title: { path: "$.name" }
            slug:  { path: "$.slug" }
            """,
        ["details"] = details,
        ["requestDelay"] = "500ms",
    };

    [Test]
    public async Task Json_details_add_and_override_fields_and_resolve_relative_images()
    {
        using var host = new SourceTestHost();
        host.Handler.Add("https://api.example.org/events", List);
        host.Handler.Add("https://wiki.example.org/summary/XFL_1", """{ "title": "XFL 1: Doe vs Roe", "thumbnail": { "source": "/img/xfl1.jpg" } }""");
        host.Handler.Add("https://wiki.example.org/summary/XFL_2", """{ "title": "XFL 2", "thumbnail": { "source": "https://cdn.example.org/xfl2.jpg" } }""");
        host.Handler.Add("https://wiki.example.org/summary/XFL_3", "{}", status: HttpStatusCode.NotFound);

        var result = await host.FetchAsync("http-json", JsonSource("""
            url: "https://wiki.example.org/summary/{{ fields.slug }}"
            fields:
              title:  { path: "$.title" }
              poster: { path: "$.thumbnail.source", optional: true }
              label:  { text: "{{ title }} ({{ slug }})" }
            """));

        var events = result.Events.ToDictionary(e => e.Key);
        Assert.Multiple(() =>
        {
            Assert.That(events.Keys, Is.EquivalentTo(new[] { "1", "2" }));
            Assert.That(events["1"].Title, Is.EqualTo("XFL 1: Doe vs Roe"), "details override the item's fields");
            Assert.That(events["1"].Images.Poster, Is.EqualTo("https://wiki.example.org/img/xfl1.jpg"), "relative to the details URL");
            Assert.That(events["2"].Images.Poster, Is.EqualTo("https://cdn.example.org/xfl2.jpg"));
            Assert.That((string?)events["1"].Fields["label"], Is.EqualTo("XFL 1: Doe vs Roe (XFL_1)"), "details text sees the details fields first");
            Assert.That(result.ItemErrors.Single().Errors.Single(), Does.StartWith("details: ").And.Contain("404"), "a failed details request is an item error");
            Assert.That(result.Responses, Has.Count.EqualTo(4));
            Assert.That(host.Time.Delays, Is.EqualTo(Enumerable.Repeat(TimeSpan.FromMilliseconds(500), 3)), "requests stay spaced by the request delay");
        });
    }

    [Test]
    public async Task Optional_details_keep_the_item_and_unchanged_items_reuse_cached_details()
    {
        using var host = new SourceTestHost();
        host.Handler.Add("https://api.example.org/events", List);
        foreach (var n in new[] { 1, 2 })
        {
            host.Handler.Add($"https://wiki.example.org/summary/XFL_{n}", $$"""{ "thumbnail": { "source": "https://cdn.example.org/{{n}}.jpg" } }""");
        }

        var settings = JsonSource("""
            url: "https://wiki.example.org/summary/{{ slug }}"
            optional: true
            fields:
              poster: { path: "$.thumbnail.source" }
            """);
        var first = await host.FetchAsync("http-json", settings, new SourceFetchOptions { BypassCache = true });
        var requests = host.Handler.Requests.Count;
        var second = await host.FetchAsync("http-json", settings, new SourceFetchOptions { BypassCache = true });

        Assert.Multiple(() =>
        {
            Assert.That(first.Events, Has.Count.EqualTo(3), "XFL 3 has no details page but is kept");
            Assert.That(first.Events.Single(e => e.Key == "3").Images.Poster, Is.Null);
            Assert.That(first.Warnings.Single(), Does.StartWith("Details failed for 1 item(s) (optional)"));
            Assert.That(second.Events.Select(e => e.Images.Poster), Is.EqualTo(first.Events.Select(e => e.Images.Poster)));
            Assert.That(host.Handler.Requests.Count - requests, Is.EqualTo(2), "the list and the failed details page; cached details are reused");
        });
    }

    [Test]
    public async Task Html_details_read_the_item_page_with_css_selectors()
    {
        using var host = new SourceTestHost();
        host.Handler.Add("https://wiki.example.org/List", """
            <table><tr><td><a href="/wiki/XFL_9">XFL 9</a></td></tr></table>
            """, "text/html");
        host.Handler.Add("https://wiki.example.org/wiki/XFL_9", """
            <html><body><table class="infobox"><tr><td class="infobox-image"><img src="//upload.example.org/xfl9.png"></td></tr>
            <tr><th>Venue</th><td>Harbour Arena</td></tr></table></body></html>
            """, "text/html");

        var result = await host.FetchAsync("http-html", new JsonObject
        {
            ["requests"] = "https://wiki.example.org/List",
            ["items"] = "tr:has(td)",
            ["fields"] = """
                title: { selector: "td" }
                link:  { selector: "td a", attribute: href }
                key:   { text: "{{ title | string.slugify }}" }
                """,
            ["details"] = """
                url: "{{ link }}"
                fields:
                  poster: { selector: ".infobox-image img", attribute: src }
                  venue:  { selector: "tr:nth-child(2) td" }
                """,
        });

        var ev = result.Events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result.ItemErrors, Is.Empty);
            Assert.That(ev.Images.Poster, Is.EqualTo("https://upload.example.org/xfl9.png"));
            Assert.That((string?)ev.Fields["venue"], Is.EqualTo("Harbour Arena"));
            Assert.That(host.Handler.RequestedUrls, Does.Contain("https://wiki.example.org/wiki/XFL_9"), "relative details URLs resolve against the list page");
        });
    }

    [Test]
    public void Invalid_details_are_reported_against_the_setting()
    {
        using var host = new SourceTestHost();
        var type = host.Types.Get("http-json");

        var failures = type.Validate(JsonSource("""
            url: "https://x.org/{{ slug }}"
            fields: { key: { path: "$.id" } }
            """));

        Assert.That(failures.Single().PropertyName, Is.EqualTo("details"));
        Assert.That(failures.Single().ErrorMessage, Does.Contain("details.fields.key"));
    }
}
