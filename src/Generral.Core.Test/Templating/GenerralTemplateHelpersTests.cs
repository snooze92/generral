using Generral.Core.Templating;

namespace Generral.Core.Test.Templating;

public class GenerralTemplateHelpersTests
{
    private static readonly TemplateRenderer Renderer = new();
    private static readonly DateTime Start = new(2025, 4, 12, 22, 0, 0, DateTimeKind.Utc);

    private static object? Evaluate(string template, object? start = null) =>
        Renderer.Compile(template).Evaluate(new Dictionary<string, object?> { ["start"] = start ?? Start });

    [TestCase("-2h", 2025, 4, 12, 20, 0)]
    [TestCase("3d", 2025, 4, 15, 22, 0)]
    [TestCase("1h30m", 2025, 4, 12, 23, 30)]
    [TestCase("-1w", 2025, 4, 5, 22, 0)]
    [TestCase("", 2025, 4, 12, 22, 0)]
    public void Date_add(string duration, int year, int month, int day, int hour, int minute)
    {
        Assert.That(Evaluate($"{{{{ start | date.add '{duration}' }}}}"), Is.EqualTo(new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc)));
    }

    [Test]
    public void Date_add_is_null_safe_and_rejects_bad_durations()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Renderer.Compile("{{ nothing | date.add '-2h' }}").Evaluate(new Dictionary<string, object?>()), Is.Null);
            Assert.That(Evaluate("{{ start | date.add nothing }}"), Is.EqualTo(Start));
            Assert.That(() => Evaluate("{{ start | date.add '2 hours' }}"), Throws.TypeOf<TemplateException>().With.Message.Contains("Invalid duration"));
        });
    }

    [Test]
    public void Date_year()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Evaluate("{{ start | date.year }}"), Is.EqualTo(2025));
            Assert.That(Renderer.Compile("{{ nothing | date.year }}").Evaluate(new Dictionary<string, object?>()), Is.Null);
            Assert.That(Evaluate("{{ date.now | date.year > 2000 }}"), Is.EqualTo(true), "builtins are kept");
        });
    }

    [TestCase("XXV", 25)]
    [TestCase("lix", 59)]
    [TestCase("MCMXCIV", 1994)]
    [TestCase("IIII", null)]
    [TestCase("IC", null)]
    [TestCase("Super", null)]
    [TestCase("", null)]
    public void String_romanize(string text, int? expected)
    {
        Assert.That(Renderer.Compile("{{ text | string.romanize }}").Evaluate(new Dictionary<string, object?> { ["text"] = text }), Is.EqualTo(expected));
    }

    [TestCase("Brighton & Hove Albion", "Brighton and Hove Albion")]
    [TestCase("St. Helens R.F.C.", "St Helens RFC")]
    [TestCase("Paris Saint-Germain", "Paris Saint Germain")]
    [TestCase("  Wolves  (W)  ", "Wolves W")]
    public void String_normalize_team(string text, string expected)
    {
        Assert.That(Renderer.Render("{{ text | string.normalize_team }}", new Dictionary<string, object?> { ["text"] = text }), Is.EqualTo(expected));
    }
}
