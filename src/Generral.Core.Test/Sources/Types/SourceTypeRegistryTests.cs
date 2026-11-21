using System.Text.Json.Nodes;
using Generral.Core.Sources.Types;

namespace Generral.Core.Test.Sources.Types;

public class SourceTypeRegistryTests
{
    [Test]
    public void Registers_the_built_in_types()
    {
        using var host = new SourceTestHost();

        Assert.Multiple(() =>
        {
            Assert.That(host.Types.All.Select(t => t.Id), Is.EquivalentTo(new[] { "thesportsdb", "http-json", "ical", "static" }));
            Assert.That(host.Types.Find("HTTP-JSON"), Is.Not.Null);
            Assert.That(host.Types.Find("rss"), Is.Null);
        });
    }

    [Test]
    public void Http_types_share_the_common_settings()
    {
        using var host = new SourceTestHost();

        var fields = host.Types.Get("ical").Fields;

        Assert.Multiple(() =>
        {
            Assert.That(fields.Select(f => f.Name), Is.EqualTo(new[] { "url", "requestDelay", "headers", "timezone" }));
            Assert.That(fields.Single(f => f.Name == "requestDelay").Value!.GetValue<double>(), Is.EqualTo(1));
            Assert.That(fields.Single(f => f.Name == "headers").Type, Is.EqualTo(SourceFieldType.KeyValueList));
        });
    }

    [TestCase("500ms", null)]
    [TestCase("1.5", null)]
    [TestCase("2s", null)]
    [TestCase("2h", "requestDelay")]
    [TestCase("120", "requestDelay")]
    public void Validates_the_request_delay(string delay, string? failure)
    {
        using var host = new SourceTestHost();

        var failures = host.Types.Get("ical").Validate(new JsonObject { ["url"] = "https://example.org/a.ics", ["requestDelay"] = delay });

        Assert.That(failures.Select(f => f.PropertyName), Is.EqualTo(failure is null ? Array.Empty<string>() : new[] { failure }));
    }

    [Test]
    public void Validates_headers_and_timezone()
    {
        using var host = new SourceTestHost();

        var failures = host.Types.Get("ical").Validate(new JsonObject
        {
            ["url"] = "https://example.org/a.ics",
            ["headers"] = new JsonArray(new JsonObject { ["key"] = "Bad Header", ["value"] = "x" }, new JsonObject { ["key"] = "X-Ok", ["value"] = "y" }),
            ["timezone"] = "Mars/Olympus",
        });

        Assert.That(failures.Select(f => f.PropertyName), Is.EqualTo(new[] { "headers", "timezone" }));
    }
}
