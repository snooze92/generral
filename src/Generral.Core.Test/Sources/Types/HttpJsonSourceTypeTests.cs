using System.Text.Json.Nodes;
using Generral.Core.Sources.Types;

namespace Generral.Core.Test.Sources.Types;

public class HttpJsonSourceTypeTests
{
    [Test]
    public async Task Runs_the_extraction_definition_with_headers()
    {
        using var host = new SourceTestHost();
        host.Handler.Add("https://api.example.org/events?year=2026", SourceTestHost.Fixture("http-json-events.json"));

        var result = await host.FetchAsync("http-json", new JsonObject
        {
            ["requests"] = """
                - url: "https://api.example.org/events?year={{ year }}"
                  foreach: { year: "2026" }
                """,
            ["items"] = "$.data[*]",
            ["fields"] = new JsonObject
            {
                ["key"] = new JsonObject { ["path"] = "$.id" },
                ["title"] = new JsonObject { ["path"] = "$.name" },
                ["start"] = new JsonObject { ["path"] = "$.starts_at" },
                ["poster"] = new JsonObject { ["path"] = "$.images.poster", ["optional"] = true },
            },
            ["headers"] = new JsonObject { ["Authorization"] = "Bearer secret" },
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Events.Select(e => e.Key), Is.EqualTo(new[] { "1", "2" }));
            Assert.That(result.Events.First().Start, Is.EqualTo(new DateTime(2026, 3, 8, 5, 0, 0, DateTimeKind.Utc)));
            Assert.That(result.Events.First().Images.Poster, Is.EqualTo("https://api.example.org/img/1.jpg"));
            Assert.That(result.ItemErrors.Single().Errors, Is.EqualTo(new[] { "title: no value" }));
            Assert.That(result.Responses.Single().Url.AbsoluteUri, Is.EqualTo("https://api.example.org/events?year=2026"));
            Assert.That(host.Handler.Requests.Single().Headers.Authorization!.ToString(), Is.EqualTo("Bearer secret"));
        });
    }

    [Test]
    public void Reports_definition_errors_against_the_setting()
    {
        using var host = new SourceTestHost();

        var failures = host.Types.Get("http-json").Validate(new JsonObject
        {
            ["requests"] = "https://api.example.org/events",
            ["items"] = "$.data[*]",
            ["fields"] = "key: { path: '$.id' }",
        });

        Assert.That(failures.Single(), Is.EqualTo(new SourceValidationFailure("fields", "fields.title: is required")));
    }

    [Test]
    public void Http_errors_fail_the_fetch()
    {
        using var host = new SourceTestHost();
        host.Handler.Add("https://api.example.org/events", "oops", "text/plain", System.Net.HttpStatusCode.InternalServerError);

        var e = Assert.ThrowsAsync<SourceFetchException>(() => host.FetchAsync("http-json", new JsonObject
        {
            ["requests"] = "https://api.example.org/events",
            ["items"] = "$[*]",
            ["fields"] = "{ key: { path: '$.id' }, title: { path: '$.name' } }",
        }));

        Assert.Multiple(() =>
        {
            Assert.That(e!.Message, Is.EqualTo("https://api.example.org/events returned HTTP 500"));
            Assert.That(e.Responses.Single().Status, Is.EqualTo(500));
            Assert.That(e.Responses.Single().Body, Is.EqualTo("oops"));
        });
    }
}
