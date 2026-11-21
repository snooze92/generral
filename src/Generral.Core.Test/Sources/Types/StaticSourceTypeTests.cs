using System.Text.Json.Nodes;
using Generral.Core.Sources;

namespace Generral.Core.Test.Sources.Types;

public class StaticSourceTypeTests
{
    private SourceTestHost _host = null!;

    [SetUp]
    public void SetUp() => _host = new SourceTestHost();

    [TearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public async Task Yaml_events_use_the_reserved_names_directly()
    {
        var result = await _host.FetchAsync("static", new JsonObject
        {
            ["events"] = """
                - key: final
                  title: Final
                  start: 2027-11-13 15:00
                  status: postponed
                  venue: Stade de France
                - key: semi
                  title: Semi-final
                - title: No key
                """,
            ["timezone"] = "Europe/Paris",
        });

        var final = result.Events.First();
        Assert.Multiple(() =>
        {
            Assert.That(result.Events.Select(e => e.Key), Is.EqualTo(new[] { "final", "semi" }));
            Assert.That(final.Start, Is.EqualTo(new DateTime(2027, 11, 13, 14, 0, 0, DateTimeKind.Utc)));
            Assert.That(final.Status, Is.EqualTo(EventStatus.Postponed));
            Assert.That(final.Fields["venue"]!.GetValue<string>(), Is.EqualTo("Stade de France"));
            Assert.That(result.ItemErrors.Single().Errors, Is.EqualTo(new[] { "key: no value" }));
            Assert.That(result.Responses, Is.Empty);
            Assert.That(_host.Handler.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task Csv_with_extraction_fields()
    {
        var result = await _host.FetchAsync("static", new JsonObject
        {
            ["format"] = "csv",
            ["events"] = "Match,Date,Venue\r\n\"France v Italy, Pool A\",2027-09-11T19:00:00Z,Lyon\r\nWales v Fiji,2027-09-12T15:00:00Z,\n",
            ["fields"] = """
                key:   { path: "$.Match", filters: [ { name: re_replace, args: [ ",.*", "" ] } ] }
                title: { path: "$.Match" }
                start: { path: "$.Date" }
                venue: { path: "$.Venue", optional: true }
                """,
        });

        var events = result.Events.ToList();
        Assert.Multiple(() =>
        {
            Assert.That(result.ItemErrors, Is.Empty);
            Assert.That(events.Select(e => e.Key), Is.EqualTo(new[] { "France v Italy", "Wales v Fiji" }));
            Assert.That(events[0].Title, Is.EqualTo("France v Italy, Pool A"));
            Assert.That(events[1].Start, Is.EqualTo(new DateTime(2027, 9, 12, 15, 0, 0, DateTimeKind.Utc)));
            Assert.That(events[1].Fields["venue"], Is.Null);
        });
    }

    [Test]
    public async Task Json_object_with_an_events_list()
    {
        var result = await _host.FetchAsync("static", new JsonObject
        {
            ["format"] = "json",
            ["events"] = """{"events": [{"key": 1, "title": "One", "poster": "https://img.example.org/1.jpg"}]}""",
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Events.Single().Key, Is.EqualTo("1"));
            Assert.That(result.Events.Single().Images.Poster, Is.EqualTo("https://img.example.org/1.jpg"));
        });
    }

    [TestCase("yaml", "- key: [", "events")]
    [TestCase("csv", "a,b\n\"x,y", "events")]
    [TestCase("csv", "a\n1,2", "events")]
    [TestCase("xml", "", "format")]
    public void Invalid_events_are_validation_failures(string format, string events, string property)
    {
        var failures = _host.Types.Get("static").Validate(new JsonObject { ["format"] = format, ["events"] = events });

        Assert.That(failures.Single().PropertyName, Is.EqualTo(property));
    }

    [Test]
    public void Has_no_http_settings()
    {
        Assert.That(_host.Types.Get("static").Fields.Select(f => f.Name), Is.EqualTo(new[] { "format", "events", "items", "fields", "timezone" }));
    }
}
