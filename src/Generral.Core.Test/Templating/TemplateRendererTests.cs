using System.Text.Json.Nodes;
using Generral.Core.Templating;
using Scriban.Runtime;

namespace Generral.Core.Test.Templating;

public class TemplateRendererTests
{
    private static readonly TemplateRenderer Renderer = new();

    private static string Render(string template, Dictionary<string, object?>? model = null) => Renderer.Render(template, model ?? []);

    [Test]
    public void Member_names_are_kept_as_is()
    {
        var model = new Dictionary<string, object?>
        {
            ["event"] = new Dictionary<string, object?>
            {
                ["title"] = "UFC 300",
                ["fields"] = JsonNode.Parse("""{"strVenue":"T-Mobile Arena","intRound":3,"tags":["a","b"]}"""),
            },
        };

        Assert.That(
            Render("{{ event.title }} @ {{ event.fields.strVenue }} r{{ event.fields.intRound + 1 }} {{ event.fields.tags | array.join ',' }}", model),
            Is.EqualTo("UFC 300 @ T-Mobile Arena r4 a,b"));
    }

    [Test]
    public void Missing_members_are_null()
    {
        Assert.That(Render("[{{ event.fields.missing.deep }}]{{ nothing == null }}", new() { ["event"] = new Dictionary<string, object?>() }), Is.EqualTo("[]true"));
    }

    [Test]
    public void Regex_match_returns_groups_or_null()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Render("""{{ ("UFC 300: Pereira vs Hill" | regex.match `^UFC (\d+)`)[1] | string.to_int }}"""), Is.EqualTo("300"));
            Assert.That(Render("""{{ ("UFC Fight Night" | regex.match `^UFC (\d+)`) == null }}"""), Is.EqualTo("true"));
            Assert.That(Render("""{{ ("ufc 12" | regex.match `^UFC (\d+)` "i")[1] }}"""), Is.EqualTo("12"));
            Assert.That(Render("""{{ ("abc" | regex.match "a(x)?(b)") | array.size }}"""), Is.EqualTo("3"));
        });
    }

    [Test]
    public void String_slugify()
    {
        Assert.That(Render("{{ 'UFC 300: Pereira vs. Hill — Été!' | string.slugify }}"), Is.EqualTo("ufc-300-pereira-vs-hill-ete"));
    }

    [Test]
    public void Builtin_functions_still_work()
    {
        Assert.That(Render("{{ 'a-b' | string.replace '-' '+' }} {{ 'x1y' | regex.replace `\\d` '_' }}"), Is.EqualTo("a+b x_y"));
    }

    [Test]
    public void Evaluate_returns_raw_values_for_single_expressions()
    {
        var range = Renderer.Compile("{{ 2024..2026 }}").Evaluate(new Dictionary<string, object?>());
        var text = Renderer.Compile("year {{ 2024 }}").Evaluate(new Dictionary<string, object?>());

        Assert.Multiple(() =>
        {
            Assert.That(range, Is.InstanceOf<ScriptRange>());
            Assert.That(((ScriptRange)range!).Cast<object>().Select(Convert.ToInt32), Is.EqualTo(new[] { 2024, 2025, 2026 }));
            Assert.That(text, Is.EqualTo("year 2024"));
        });
    }

    [Test]
    public void Syntax_errors_are_reported_on_compile()
    {
        Assert.That(() => Renderer.Compile("{{ if }}"), Throws.TypeOf<TemplateException>());
    }

    [TestCase("{{ include 'other' }}")]
    [TestCase("{{ include_join ['a'] '' }}")]
    public void Includes_are_unavailable(string template)
    {
        Assert.That(() => Render(template), Throws.TypeOf<TemplateException>());
    }

    [Test]
    public void Builtins_cannot_be_modified()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Render("{{ regex.match = 1 }}"), Throws.TypeOf<TemplateException>());
            Assert.That(Render("{{ ('a' | regex.match 'a')[0] }}"), Is.EqualTo("a"));
        });
    }

    [Test]
    public void Loop_limit()
    {
        var renderer = new TemplateRenderer(new TemplateLimits { LoopLimit = 10 });
        Assert.Multiple(() =>
        {
            Assert.That(renderer.Render("{{ for i in 1..10; i; end }}", new Dictionary<string, object?>()), Is.EqualTo("12345678910"));
            Assert.That(() => renderer.Render("{{ for i in 1..11; end }}", new Dictionary<string, object?>()), Throws.TypeOf<TemplateException>().With.Message.Contains("LoopLimit"));
            Assert.That(() => renderer.Render("{{ object.eval 'for i in 1..11; end' }}", new Dictionary<string, object?>()), Throws.TypeOf<TemplateException>());
        });
    }

    [Test]
    public void Recursive_limit()
    {
        var renderer = new TemplateRenderer(new TemplateLimits { RecursiveLimit = 20 });
        Assert.That(
            () => renderer.Render("{{ func f(x); ret f(x + 1); end; f(1) }}", new Dictionary<string, object?>()),
            Throws.TypeOf<TemplateException>().With.Message.Contains("recursive"));
    }

    [Test]
    public void Output_limit()
    {
        var renderer = new TemplateRenderer(new TemplateLimits { LimitToString = 100, LoopLimit = 0 });
        Assert.Multiple(() =>
        {
            Assert.That(() => renderer.Render("{{ for i in 1..1000; 'abc'; end }}", new Dictionary<string, object?>()), Throws.TypeOf<TemplateException>());
            Assert.That(() => renderer.Render("{{ 'a' | string.pad_left 1000 }}", new Dictionary<string, object?>()), Throws.TypeOf<TemplateException>());
        });
    }

    [Test]
    public void Timeout()
    {
        var renderer = new TemplateRenderer(new TemplateLimits { LoopLimit = 0, Timeout = TimeSpan.FromMilliseconds(100) });
        Assert.That(
            () => renderer.Render("{{ while true; end }}", new Dictionary<string, object?>()),
            Throws.TypeOf<TemplateException>().With.Message.Contains("timed out"));
    }

    [Test]
    public void Caller_cancellation_is_not_a_timeout()
    {
        var renderer = new TemplateRenderer(new TemplateLimits { LoopLimit = 0 });
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        Assert.That(
            () => renderer.Render("{{ while true; end }}", new Dictionary<string, object?>(), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void Custom_helpers_can_be_registered()
    {
        var renderer = new TemplateRenderer(helpers: [new ShoutHelpers()]);
        Assert.That(renderer.Render("{{ 'hi' | string.shout }} {{ 'a b' | string.slugify }}", new Dictionary<string, object?>()), Is.EqualTo("HI! a-b"));
    }

    private sealed class ShoutHelpers : ITemplateHelpers
    {
        public void Register(ScriptObject builtins) => TemplateRenderer.Import(builtins, "string", typeof(Functions));

        private static class Functions
        {
            public static string Shout(string text) => text.ToUpperInvariant() + "!";
        }
    }
}
