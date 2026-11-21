using Generral.Core.Sources;
using Generral.Core.Sources.Extraction;

namespace Generral.Core.Test.Sources.Extraction;

public class ReservedFieldsTests
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    [TestCase("Cancelled", EventStatus.Cancelled)]
    [TestCase("canceled", EventStatus.Cancelled)]
    [TestCase("Postponed", EventStatus.Postponed)]
    [TestCase("FT", EventStatus.Completed)]
    [TestCase("Match Finished", EventStatus.Completed)]
    [TestCase("match_finished", EventStatus.Completed)]
    [TestCase(" Not  Started ", EventStatus.Scheduled)]
    [TestCase("scheduled", EventStatus.Scheduled)]
    [TestCase("weird", EventStatus.Unknown)]
    [TestCase(null, EventStatus.Unknown)]
    public void Status_normalisation(string? value, EventStatus expected)
    {
        Assert.That(ReservedFields.ParseStatus(value), Is.EqualTo(expected));
    }

    [Test]
    public void Dates_are_converted_to_utc()
    {
        var extracted = ToEvent(new()
        {
            ["key"] = 7L,
            ["title"] = " Title ",
            ["start"] = new DateTime(2026, 7, 1, 20, 0, 0),
            ["end"] = "2026-07-01T22:00:00Z",
        });

        Assert.Multiple(() =>
        {
            Assert.That(extracted!.Key, Is.EqualTo("7"));
            Assert.That(extracted.Title, Is.EqualTo("Title"));
            Assert.That(extracted.Start, Is.EqualTo(new DateTime(2026, 7, 1, 18, 0, 0)));
            Assert.That(extracted.Start!.Value.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(extracted.End, Is.EqualTo(new DateTime(2026, 7, 1, 22, 0, 0)));
            Assert.That(extracted.Timezone, Is.EqualTo("Europe/Paris"));
        });
    }

    [Test]
    public void Unix_timestamps()
    {
        var seconds = ToEvent(new() { ["key"] = "a", ["title"] = "b", ["start"] = 1_767_225_600L });
        var milliseconds = ToEvent(new() { ["key"] = "a", ["title"] = "b", ["start"] = 1_767_225_600_000L });

        Assert.Multiple(() =>
        {
            Assert.That(seconds!.Start, Is.EqualTo(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.That(milliseconds!.Start, Is.EqualTo(seconds.Start));
        });
    }

    [Test]
    public void Images_are_resolved_and_restricted_to_http()
    {
        var extracted = ToEvent(new()
        {
            ["key"] = "a",
            ["title"] = "b",
            ["poster"] = "/p.jpg",
            ["fanart"] = "https://cdn.example.org/f.jpg",
            ["logo"] = "javascript:alert(1)",
        }, new Uri("https://example.org/api/events"));

        Assert.Multiple(() =>
        {
            Assert.That(extracted!.Images.Poster, Is.EqualTo("https://example.org/p.jpg"));
            Assert.That(extracted.Images.Fanart, Is.EqualTo("https://cdn.example.org/f.jpg"));
            Assert.That(extracted.Images.Logo, Is.Null);
            Assert.That(extracted.Images.Thumb, Is.Null);
        });
    }

    [Test]
    public void Missing_required_fields_and_bad_dates_are_errors()
    {
        var errors = new List<string>();
        var extracted = ReservedFields.ToEvent(new Dictionary<string, object?> { ["key"] = " ", ["start"] = "someday" }, [], Paris, null, errors);

        Assert.Multiple(() =>
        {
            Assert.That(extracted, Is.Null);
            Assert.That(errors, Is.EqualTo(new[] { "key: required", "title: required", "start: 'someday' is not a date (use the dateparse filter)" }));
        });
    }

    private static ExtractedEvent? ToEvent(Dictionary<string, object?> values, Uri? baseUrl = null)
    {
        var errors = new List<string>();
        var extracted = ReservedFields.ToEvent(values, [], Paris, baseUrl, errors);
        Assert.That(errors, Is.Empty);
        return extracted;
    }
}
