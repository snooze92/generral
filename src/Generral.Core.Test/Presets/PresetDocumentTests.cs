using System.Text.Json.Nodes;
using Generral.Core.Extensions;
using Generral.Core.Presets;

namespace Generral.Core.Test.Presets;

public class PresetDocumentTests
{
    private const string Preset = """
        schema: generral/preset@1
        id: cup
        name: Cup
        parameters:
          - { name: year, label: Year, default: "2027" }
          - { name: token, secret: true }
        source:
          name: "Cup {{ year }}"
          type: static
          settings: { events: "[]", headers: { Authorization: "Bearer {{ secret.token }}" } }
          refreshInterval: 6h
        series: { title: Cup, genres: [Sport], monitorNewItems: none }
        mapping:
          name: "Cup {{ year }}"
          season: { strategy: fixed, value: '{{ year }}' }
          title: '{{ event.title }} {{ secret.other }}'
        """;

    [Test]
    public void Replaces_placeholders_and_leaves_other_templates()
    {
        var document = PresetDocument.Parse(Preset);
        var resolved = document.Resolve(new Dictionary<string, string?> { ["token"] = "s3cret", ["other"] = "x" });

        Assert.Multiple(() =>
        {
            Assert.That(document.RequiredSecrets, Is.EqualTo(new[] { "token", "other" }), "declared and undeclared secrets");
            Assert.That(resolved.Sources.Single().Name, Is.EqualTo("Cup 2027"));
            Assert.That(resolved.Sources.Single().Settings["headers"]!["Authorization"]!.GetValue<string>(), Is.EqualTo("Bearer s3cret"));
            Assert.That(resolved.Sources.Single().RefreshInterval, Is.EqualTo(TimeSpan.FromHours(6)));
            Assert.That(resolved.Series.MonitorNewItems, Is.EqualTo(Core.Tv.NewItemMonitorType.None));
            Assert.That(resolved.Mappings.Single().Name, Is.EqualTo("Cup 2027"));
            Assert.That(resolved.Mappings.Single().Parsed.Season.Value, Is.EqualTo("2027"));
            Assert.That(resolved.Mappings.Single().Parsed.Title, Is.EqualTo("{{ event.title }} x"));
            Assert.That(resolved.Mappings.Single().Definition.ContainsKey("name"), Is.False);
        });
    }

    [TestCase("schema: other\nid: a\nname: A", "schema: must be 'generral/preset@1'")]
    [TestCase("schema: generral/preset@1\nid: A B\nname: A", "id: must be")]
    [TestCase("schema: generral/preset@1\nid: a\nname: A\nfoo: 1", "unknown property 'foo'")]
    public void Rejects_invalid_documents(string yaml, string error)
    {
        var e = Assert.Throws<PresetException>(() => PresetDocument.Parse(yaml));

        Assert.That(e!.Message, Does.Contain(error));
    }

    [Test]
    public void Requires_secrets_without_a_default()
    {
        var e = Assert.Throws<PresetException>(() => PresetDocument.Parse(Preset).Resolve(new Dictionary<string, string?>()));

        Assert.That(e!.Message, Is.EqualTo("secrets.token: is required"));
    }

    [Test]
    public void Yaml_writer_round_trips()
    {
        var node = JsonNode.Parse("""{ "a": "123", "b": 123, "c": [true, "true", null, "null", ""], "d": {}, "e": "multi\nline: x", "f": "{{ x }}", "g": 1.5 }""");

        Assert.That(JsonNode.DeepEquals(YamlJson.Parse(YamlJson.ToYaml(node)), node), Is.True, YamlJson.ToYaml(node));
    }
}
