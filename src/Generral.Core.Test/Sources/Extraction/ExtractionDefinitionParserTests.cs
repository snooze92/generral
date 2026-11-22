using Generral.Core.Sources.Extraction;

namespace Generral.Core.Test.Sources.Extraction;

public class ExtractionDefinitionParserTests
{
    [Test]
    public void Parses_the_spec_example()
    {
        var definition = ExtractionDefinitionParser.FromYaml("""
            type: http-json
            requests:
              - url: "https://api.example.org/events?league=ufc&year={{ year }}"
                foreach:
                  year: "{{ (date.now.year - 1)..(date.now.year + 1) }}"
                pagination:
                  next: "$.links.next"
                  max_pages: 5
            items: "$.data[*]"
            fields:
              key:    { path: "$.id" }
              title:  { path: "$.name" }
              start:  { path: "$.starts_at", filters: [ { name: dateparse, args: "o" } ] }
              number: { path: "$.name", filters: [ { name: regexp, args: "^UFC (\\d+)" } ], optional: true }
              round:  { text: "{{ number }}", filters: [ toint, { name: split, args: [ "-", 0 ] } ], default: 1 }
            """);

        Assert.Multiple(() =>
        {
            Assert.That(definition.Items, Is.EqualTo("$.data[*]"));
            Assert.That(definition.Requests, Has.Count.EqualTo(1));
            Assert.That(definition.Requests[0].Foreach.Single().Key, Is.EqualTo("year"));
            Assert.That(definition.Requests[0].Pagination!.Next, Is.EqualTo("$.links.next"));
            Assert.That(definition.Requests[0].Pagination!.MaxPages, Is.EqualTo(5));
            Assert.That(definition.Fields.Select(f => f.Name), Is.EqualTo(new[] { "key", "title", "start", "number", "round" }));
            Assert.That(definition.Fields[2].Filters.Single(), Is.EqualTo(new FilterDefinition("dateparse", ["o"])).Using<FilterDefinition>(SameFilter));
            Assert.That(definition.Fields[3].Filters.Single().Args, Is.EqualTo(new[] { @"^UFC (\d+)" }));
            Assert.That(definition.Fields[3].Optional, Is.True);
            Assert.That(definition.Fields[4].Filters.Select(f => f.Name), Is.EqualTo(new[] { "toint", "split" }));
            Assert.That(definition.Fields[4].Filters[1].Args, Is.EqualTo(new[] { "-", "0" }));
            Assert.That(definition.Fields[4].Default!.GetValue<long>(), Is.EqualTo(1));
        });
    }

    [Test]
    public void Parses_json_with_page_param_pagination()
    {
        var definition = ExtractionDefinitionParser.FromJson("""
            {
              "requests": [{ "url": "https://x.org/e", "pagination": { "page_param": "p", "start": 0, "until_empty": false } }],
              "items": "$[*]",
              "fields": { "key": { "path": "$.id" }, "title": { "path": "$.t" } }
            }
            """);

        var pagination = definition.Requests[0].Pagination!;
        Assert.Multiple(() =>
        {
            Assert.That(pagination.PageParam, Is.EqualTo("p"));
            Assert.That(pagination.Start, Is.Zero);
            Assert.That(pagination.UntilEmpty, Is.False);
            Assert.That(pagination.MaxPages, Is.EqualTo(PaginationDefinition.DefaultMaxPages));
        });
    }

    [TestCase("requests: []\nitems: $\nfields: { key: { path: $.a } }", "requests")]
    [TestCase("requests: [https://x.org]\nfields: { key: { path: $.a } }", "items")]
    [TestCase("requests: [https://x.org]\nitems: $\nfields: {}", "fields")]
    [TestCase("requests: [https://x.org]\nitems: $\nfields: { key: { xpath: a } }", "unknown property 'xpath'")]
    [TestCase("requests: [https://x.org]\nitems: $\nfields: { key: { path: $.a, text: b } }", "set only one of")]
    [TestCase("requests: [https://x.org]\nitems: $\nfields: { key: { optional: true } }", "fields.key")]
    [TestCase("requests: [https://x.org]\nitems: $\nfields: { key: { path: $.a, optional: maybe } }", "optional")]
    [TestCase("requests: [{ url: https://x.org, pagination: { start: 1 } }]\nitems: $\nfields: { key: { path: $.a } }", "pagination")]
    [TestCase("requests: [{ url: https://x.org, pagination: { next: $.n, max_pages: 1000 } }]\nitems: $\nfields: { key: { path: $.a } }", "max_pages")]
    [TestCase("requests: [https://x.org]\nitems: $\nfields: { key: { path: $.a }, key: { path: $.b } }", "Duplicate key")]
    [TestCase("requests: [https://x.org\nitems: $", "Invalid YAML")]
    public void Rejects_invalid_definitions(string yaml, string message)
    {
        Assert.That(() => ExtractionDefinitionParser.FromYaml(yaml), Throws.TypeOf<ExtractionDefinitionException>().With.Message.Contains(message));
    }

    [Test]
    public void Rejects_invalid_json()
    {
        Assert.That(() => ExtractionDefinitionParser.FromJson("{"), Throws.TypeOf<ExtractionDefinitionException>());
    }

    private static bool SameFilter(FilterDefinition a, FilterDefinition b) => a.Name == b.Name && a.Args.SequenceEqual(b.Args);
}
