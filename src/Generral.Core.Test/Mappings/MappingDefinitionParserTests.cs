using Generral.Core.Mappings;

namespace Generral.Core.Test.Mappings;

public class MappingDefinitionParserTests
{
    [Test]
    public void Parses_the_ufc_example_without_parts()
    {
        var definition = MappingDefinitionParser.FromYaml("""
            name: UFC numbered events
            filter:
              - '{{ event.title | regex.match "^UFC \\d+\\b" }}'
              - '{{ event.status != "cancelled" }}'
            vars:
              number:   '{{ (event.title | regex.match "^UFC (\\d+)")[1] | string.to_int }}'
              headline: '{{ event.title | regex.replace "^UFC \\d+:?\\s*" "" }}'
            season:  { strategy: year }
            episode: { strategy: sequential, order: '{{ event.start }}', onInsert: renumber-unlocked }
            absolute: '{{ vars.number }}'
            title:    'UFC {{ vars.number }}'
            airDate:  '{{ event.start | date.add "-2h" }}'
            runtime:  180
            images:
              thumb: '{{ event.images.thumb ?? event.images.poster }}'
            search:
              categories: [5000, 5060]
              queries: ['UFC {{ vars.number }}', 'UFC{{ vars.number }}']
              must: ['\bUFC ?{{ vars.number }}\b']
              mustNot: ['\bfight ?night\b']
              dateWindow: { before: 6h, after: 90d }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(definition.Filter, Has.Count.EqualTo(2));
            Assert.That(definition.Vars.Select(v => v.Key), Is.EqualTo(new[] { "number", "headline" }));
            Assert.That(definition.Season.Strategy, Is.EqualTo(SeasonStrategy.Year));
            Assert.That(definition.Episode.Strategy, Is.EqualTo(EpisodeStrategy.Sequential));
            Assert.That(definition.Episode.OnInsert, Is.EqualTo(InsertPolicy.RenumberUnlocked));
            Assert.That(definition.Absolute, Is.EqualTo("{{ vars.number }}"));
            Assert.That(definition.Runtime, Is.EqualTo("180"));
            Assert.That(definition.Thumb, Does.StartWith("{{ event.images.thumb"));
            Assert.That(definition.Search.Queries, Has.Count.EqualTo(2));
            Assert.That(definition.Search.Must.Single(), Is.EqualTo(@"\bUFC ?{{ vars.number }}\b"));
            Assert.That(definition.Search.Settings.Select(s => s.Key), Is.EqualTo(new[] { "categories", "dateWindow" }));
        });
    }

    [Test]
    public void Defaults_apply_to_an_empty_definition()
    {
        var definition = MappingDefinitionParser.FromJson("{}");

        Assert.Multiple(() =>
        {
            Assert.That(definition.Title, Is.EqualTo(MappingDefinition.DefaultTitle));
            Assert.That(definition.AirDate, Is.EqualTo(MappingDefinition.DefaultAirDate));
            Assert.That(definition.Season.Strategy, Is.EqualTo(SeasonStrategy.Year));
            Assert.That(definition.Episode.Order, Is.EqualTo(EpisodeDefinition.DefaultOrder));
            Assert.That(definition.Episode.OnInsert, Is.EqualTo(InsertPolicy.Append));
        });
    }

    [Test]
    public void Parses_other_strategies()
    {
        var definition = MappingDefinitionParser.FromJson("""
            { "season": { "strategy": "sequential", "group": "{{ event.fields.tournament }}", "title": "{{ event.fields.tournament }}" },
              "episode": { "strategy": "field", "value": "{{ event.fields.round }}" } }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(definition.Season.Strategy, Is.EqualTo(SeasonStrategy.Sequential));
            Assert.That(definition.Season.Title, Is.Not.Null);
            Assert.That(definition.Episode.Strategy, Is.EqualTo(EpisodeStrategy.Field));
            Assert.That(MappingDefinitionParser.FromYaml("season: { strategy: fixed, value: 1 }").Season.Value, Is.EqualTo("1"));
            Assert.That(MappingDefinitionParser.FromYaml("episode: { strategy: date }").Episode.Strategy, Is.EqualTo(EpisodeStrategy.Date));
        });
    }

    [TestCase("parts: { from: '{{ event.fields.stages }}' }", "parts: 'from' is not supported yet")]
    [TestCase("parts: [{ key: a }, { key: a }]", "parts[1].key: 'a' is used twice")]
    [TestCase("parts: [{ key: 'a b' }]", "parts[0].key: must be letters")]
    [TestCase("parts: [{ key: a }]\nsearch: { parts: { b: { must: [x] } } }", "search.parts: unknown part 'b'")]
    [TestCase("search: { pack: { must: [x] } }", "search: 'pack' is not supported yet")]
    [TestCase("titel: x", "mapping: unknown property 'titel'")]
    [TestCase("season: { strategy: weekly }", "season.strategy: unknown value 'weekly' (expected year, field, fixed, sequential)")]
    [TestCase("season: { strategy: field }", "season.value: is required with strategy 'field'")]
    [TestCase("season: { strategy: fixed, value: '{{ 1 }}' }", "season.value: must be a season number")]
    [TestCase("season: { strategy: sequential }", "season.group: is required")]
    [TestCase("episode: { strategy: field }", "episode.value: is required")]
    [TestCase("episode: { onInsert: renumber-all }", "episode.onInsert: unknown value 'renumber-all'")]
    [TestCase("vars: { 1x: a }", "vars: '1x' is not a valid name")]
    [TestCase("filter: [{ a: b }]", "filter[0]: must be a string")]
    [TestCase("[1]", "mapping: must be an object")]
    [TestCase("title: [", "Invalid YAML")]
    public void Invalid_definitions_are_rejected(string yaml, string message)
    {
        Assert.That(() => MappingDefinitionParser.FromYaml(yaml), Throws.TypeOf<MappingDefinitionException>().With.Message.StartsWith(message));
    }
}
