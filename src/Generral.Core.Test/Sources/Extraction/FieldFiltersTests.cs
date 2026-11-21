using System.Text.Json.Nodes;
using Generral.Core.Sources.Extraction;

namespace Generral.Core.Test.Sources.Extraction;

public class FieldFiltersTests
{
    private static readonly DateTime Now = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    private static object? Apply(string name, object? value, params string[] args) =>
        FieldFilters.Create(name, args, new FixedTime(Now))(value);

    [TestCase("replace", "a-b-c", "a+b+c", "-", "+")]
    [TestCase("re_replace", "UFC  300 ", "UFC_300_", @"\s+", "_")]
    [TestCase("re_replace", "2025-01-02", "02/01/2025", @"(\d+)-(\d+)-(\d+)", "$3/$2/$1")]
    [TestCase("regexp", "UFC 300: Pereira", "300", @"^UFC (\d+)")]
    [TestCase("regexp", "UFC 300", "UFC", "^[A-Z]+")]
    [TestCase("split", "a|b|c", "b", "|", "1")]
    [TestCase("split", "a|b|c", "c", "|", "-1")]
    [TestCase("trim", "  x  ", "x")]
    [TestCase("trim", "--x-", "x", "-")]
    [TestCase("prepend", "x", "ax", "a")]
    [TestCase("append", "x", "xa", "a")]
    [TestCase("tolower", "AbC", "abc")]
    [TestCase("toupper", "AbC", "ABC")]
    [TestCase("urldecode", "a%20b+c", "a b c")]
    [TestCase("urlencode", "a b&c", "a+b%26c")]
    [TestCase("htmldecode", "Tom &amp; Jerry&#39;s", "Tom & Jerry's")]
    [TestCase("diacritics", "Crème Brûlée Ñ", "Creme Brulee N")]
    [TestCase("slugify", "UFC 300: Pereira vs. Hill", "ufc-300-pereira-vs-hill")]
    [TestCase("querystring", "https://x.org/e?id=42&name=a%20b", "42", "id")]
    [TestCase("querystring", "https://x.org/e?id=42&name=a%20b#top", "a b", "name")]
    [TestCase("validate", "Soccer, Basketball, MMA", "Soccer, MMA", "soccer,mma")]
    public void String_filters(string name, string input, string expected, params string[] args)
    {
        Assert.That(Apply(name, input, args), Is.EqualTo(expected));
    }

    [TestCase("regexp", "no number", @"(\d+)")]
    [TestCase("split", "a|b", "|", "5")]
    [TestCase("querystring", "https://x.org/?a=1", "b")]
    [TestCase("validate", "Golf", "soccer")]
    public void Filters_yield_null_when_nothing_matches(string name, string input, params string[] args)
    {
        Assert.That(Apply(name, input, args), Is.Null);
    }

    [Test]
    public void Null_passes_through()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Apply("append", null, "x"), Is.Null);
            Assert.That(Apply("dateparse", null), Is.Null);
            Assert.That(Apply("toint", null), Is.Null);
        });
    }

    [Test]
    public void Non_string_values_are_converted_to_text()
    {
        Assert.That(Apply("prepend", 42L, "#"), Is.EqualTo("#42"));
    }

    [Test]
    public void Toint_and_tofloat()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Apply("toint", " 1,234 "), Is.EqualTo(1234L));
            Assert.That(Apply("toint", 3.9), Is.EqualTo(3L));
            Assert.That(Apply("tofloat", "1.5"), Is.EqualTo(1.5));
            Assert.That(Apply("tofloat", 2L), Is.EqualTo(2.0));
            Assert.That(() => Apply("toint", "abc"), Throws.TypeOf<FieldException>());
            Assert.That(() => Apply("tofloat", "x"), Throws.TypeOf<FieldException>());
        });
    }

    [Test]
    public void Dateparse_with_formats()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Apply("dateparse", "Mar 5, 2026", "MMM d, yyyy"), Is.EqualTo(new DateTime(2026, 3, 5)).And.Property("Kind").EqualTo(DateTimeKind.Unspecified));
            Assert.That(Apply("dateparse", "5/3/2026", "MMM d, yyyy", "d/M/yyyy"), Is.EqualTo(new DateTime(2026, 3, 5)));
            Assert.That(() => Apply("dateparse", "March 5", "d/M/yyyy"), Throws.TypeOf<FieldException>());
        });
    }

    [Test]
    public void Dateparse_iso_keeps_offsets_as_utc()
    {
        var withOffset = (DateTime)Apply("dateparse", "2026-03-05T20:00:00+02:00", "o")!;
        var zulu = (DateTime)Apply("dateparse", "2026-03-05T20:00:00Z")!;
        var local = (DateTime)Apply("dateparse", "2026-03-05 20:00", "iso")!;

        Assert.Multiple(() =>
        {
            Assert.That(withOffset, Is.EqualTo(new DateTime(2026, 3, 5, 18, 0, 0)));
            Assert.That(withOffset.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(zulu, Is.EqualTo(new DateTime(2026, 3, 5, 20, 0, 0)));
            Assert.That(zulu.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(local.Kind, Is.EqualTo(DateTimeKind.Unspecified));
        });
    }

    [Test]
    public void Timezone_converts_local_dates_to_utc()
    {
        var summer = (DateTime)Apply("timezone", new DateTime(2026, 7, 1, 20, 0, 0), "Europe/Paris")!;
        var winter = (DateTime)Apply("timezone", "2026-01-01 20:00", "Europe/Paris")!;
        var alreadyUtc = (DateTime)Apply("timezone", new DateTime(2026, 7, 1, 20, 0, 0, DateTimeKind.Utc), "Europe/Paris")!;

        Assert.Multiple(() =>
        {
            Assert.That(summer, Is.EqualTo(new DateTime(2026, 7, 1, 18, 0, 0)));
            Assert.That(summer.Kind, Is.EqualTo(DateTimeKind.Utc));
            Assert.That(winter, Is.EqualTo(new DateTime(2026, 1, 1, 19, 0, 0)));
            Assert.That(alreadyUtc, Is.EqualTo(new DateTime(2026, 7, 1, 20, 0, 0)));
            Assert.That(() => FieldFilters.Create("timezone", ["Mars/Olympus"]), Throws.ArgumentException);
        });
    }

    [TestCase("now", 0)]
    [TestCase("2 hours ago", -2)]
    [TestCase("1 day 3 hours ago", -27)]
    [TestCase("an hour ago", -1)]
    [TestCase("in 3 days", 72)]
    [TestCase("2 weeks from now", 336)]
    public void Timeago(string input, int hours)
    {
        Assert.That(Apply("timeago", input), Is.EqualTo(Now.AddHours(hours)));
    }

    [Test]
    public void Timeago_and_fuzzytime()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Apply("timeago", "yesterday"), Is.EqualTo(Now.Date.AddDays(-1)));
            Assert.That(Apply("timeago", "3 months ago"), Is.EqualTo(Now.AddMonths(-3)));
            Assert.That(() => Apply("timeago", "2026-01-01"), Throws.TypeOf<FieldException>());
            Assert.That(Apply("fuzzytime", "2026-01-01"), Is.EqualTo(new DateTime(2026, 1, 1)));
            Assert.That(Apply("fuzzytime", "5 mins ago"), Is.EqualTo(Now.AddMinutes(-5)));
        });
    }

    [Test]
    public void Jsonjoinarray()
    {
        var json = JsonNode.Parse("""{"genres":[{"name":"MMA"},{"name":"Boxing"}],"tags":["a","b"]}""");
        Assert.Multiple(() =>
        {
            Assert.That(Apply("jsonjoinarray", json, "$.genres[*].name", ", "), Is.EqualTo("MMA, Boxing"));
            Assert.That(Apply("jsonjoinarray", json, "$.tags", "/"), Is.EqualTo("a/b"));
            Assert.That(Apply("jsonjoinarray", """["x","y"]""", "$[*]", "+"), Is.EqualTo("x+y"));
            Assert.That(() => Apply("jsonjoinarray", "not json", "$[*]", ","), Throws.TypeOf<FieldException>());
        });
    }

    [Test]
    public void Definition_errors()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => FieldFilters.Create("nope", []), Throws.ArgumentException.With.Message.Contains("Unknown filter"));
            Assert.That(() => FieldFilters.Create("replace", ["a"]), Throws.ArgumentException);
            Assert.That(() => FieldFilters.Create("regexp", ["("]), Throws.ArgumentException);
            Assert.That(() => FieldFilters.Create("split", [",", "x"]), Throws.ArgumentException);
            Assert.That(() => FieldFilters.Create("tolower", ["x"]), Throws.ArgumentException);
            Assert.That(() => FieldFilters.Create("validate", []), Throws.ArgumentException);
            Assert.That(() => FieldFilters.Create("jsonjoinarray", ["$[", ","]), Throws.ArgumentException);
        });
    }

    [Test]
    public void Names_are_case_insensitive()
    {
        Assert.That(Apply("ToUpper", "a"), Is.EqualTo("A"));
    }
}

internal sealed class FixedTime(DateTime utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
}
