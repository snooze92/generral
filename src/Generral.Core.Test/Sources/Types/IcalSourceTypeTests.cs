using System.Text.Json.Nodes;
using Generral.Core.Sources;
using Generral.Core.Sources.Types;

namespace Generral.Core.Test.Sources.Types;

public class IcalSourceTypeTests
{
    [Test]
    public async Task Maps_vevents_to_events()
    {
        using var host = new SourceTestHost();
        host.Handler.Add("https://example.org/cup.ics", SourceTestHost.Fixture("ical/calendar.ics"), "text/calendar");

        var result = await host.FetchAsync("ical", new JsonObject { ["url"] = "webcal://example.org/cup.ics", ["timezone"] = "Europe/London" });

        var events = result.Events.ToDictionary(e => e.Key);
        var opening = events["match-1@example.org"];
        Assert.Multiple(() =>
        {
            Assert.That(events.Keys, Is.EqualTo(new[] { "match-1@example.org", "match-2@example.org", "rest-day@example.org" }));
            Assert.That(opening.Title, Is.EqualTo("France v New Zealand"));
            Assert.That(opening.Start, Is.EqualTo(new DateTime(2027, 9, 10, 19, 0, 0, DateTimeKind.Utc)));
            Assert.That(opening.End, Is.EqualTo(new DateTime(2027, 9, 10, 20, 50, 0, DateTimeKind.Utc)));
            Assert.That(opening.Status, Is.EqualTo(EventStatus.Scheduled));
            Assert.That(opening.Fields["location"]!.GetValue<string>(), Is.EqualTo("Stade de France, Saint-Denis"));
            Assert.That(opening.Fields["description"]!.GetValue<string>(), Is.EqualTo("Pool A\nOpening match"));
            Assert.That(opening.Fields["categories"]!.AsArray().Select(c => c!.GetValue<string>()), Is.EqualTo(new[] { "Pool A", "Opening" }));
            Assert.That(opening.Fields["url"]!.GetValue<string>(), Is.EqualTo("https://example.org/matches/1"));
            Assert.That(opening.Fields["_raw"]!["SUMMARY"]!.GetValue<string>(), Is.EqualTo("France v New Zealand"));
            Assert.That(events["match-2@example.org"].End, Is.EqualTo(new DateTime(2027, 10, 30, 22, 0, 0, DateTimeKind.Utc)));
            Assert.That(events["match-2@example.org"].Status, Is.EqualTo(EventStatus.Cancelled));
            Assert.That(events["rest-day@example.org"].Start, Is.EqualTo(new DateTime(2027, 9, 14, 23, 0, 0, DateTimeKind.Utc)), "all-day dates are in the source timezone");
            Assert.That(result.ItemErrors.Single().Errors, Is.EqualTo(new[] { "key: required (UID)" }));
            Assert.That(result.Meta["name"]!.GetValue<string>(), Is.EqualTo("Rugby Cup"));
            Assert.That(result.Responses.Single().ContentType, Is.EqualTo("text/calendar"));
            Assert.That(host.Handler.RequestedUrls, Is.EqualTo(new[] { "https://example.org/cup.ics" }));
        });
    }

    [Test]
    public void Invalid_calendar_fails_the_fetch()
    {
        using var host = new SourceTestHost();
        host.Handler.Add("https://example.org/bad.ics", "<html>not a calendar</html>", "text/html");

        var e = Assert.ThrowsAsync<SourceFetchException>(() => host.FetchAsync("ical", new JsonObject { ["url"] = "https://example.org/bad.ics" }));

        Assert.Multiple(() =>
        {
            Assert.That(e!.Message, Does.Contain("invalid iCalendar"));
            Assert.That(e.Responses.Single().Body, Does.Contain("not a calendar"));
        });
    }

    [Test]
    public void Requires_an_http_url()
    {
        using var host = new SourceTestHost();

        var failures = host.Types.Get("ical").Validate(new JsonObject { ["url"] = "file:///etc/passwd" });

        Assert.That(failures.Single().PropertyName, Is.EqualTo("url"));
    }
}
