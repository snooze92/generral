using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Generral.Integration.Test;

public class MediaManagementApiTests
{
    [Test]
    public async Task Naming_defaults_examples_and_validation()
    {
        await using var host = await GenerralTestHost.StartAsync();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", host.ApiKey);

        var naming = (await client.GetFromJsonAsync<JsonObject>("/api/v1/config/naming"))!;
        var examples = (await client.GetFromJsonAsync<JsonObject>("/api/v1/config/naming/examples"))!;
        var invalid = naming.DeepClone().AsObject();
        invalid["standardEpisodeFormat"] = "{Series Title}";
        var rejected = await client.PutAsJsonAsync("/api/v1/config/naming", invalid);
        naming["multiEpisodeStyle"] = 0;
        var saved = await client.PutAsJsonAsync("/api/v1/config/naming", naming);
        var extend = (await client.GetFromJsonAsync<JsonObject>("/api/v1/config/naming/examples"))!;
        var badRoot = await client.PostAsJsonAsync("/api/v1/rootfolder", new JsonObject { ["path"] = "/does/not/exist" });
        var noSeries = await client.GetAsync("/api/v1/episodefile");

        Assert.Multiple(() =>
        {
            Assert.That((bool)naming["renameEpisodes"]!, Is.True);
            Assert.That((int)naming["colonReplacementFormat"]!, Is.EqualTo(4));
            Assert.That((string)naming["standardEpisodeFormat"]!, Is.EqualTo("{Series Title} - S{season:00}E{episode:00} - {Episode Title} [{Quality Full}]"));
            Assert.That((string)examples["singleEpisodeExample"]!, Is.EqualTo("UFC - S2025E04 - UFC 314 - Volkanovski vs. Lopes - Main Card [WEBDL-1080p].mkv"));
            Assert.That((string)examples["multiEpisodeExample"]!, Does.StartWith("UFC - S2025E04-E06 - "));
            Assert.That((string)examples["dailyEpisodeExample"]!, Does.StartWith("Formula 1 - 2025-04-12 - "));
            Assert.That((string)examples["seriesFolderExample"]!, Is.EqualTo("UFC (1993)"));
            Assert.That((string)examples["seasonFolderExample"]!, Is.EqualTo("Season 2025"));
            Assert.That(rejected.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That((string)extend["multiEpisodeExample"]!, Does.StartWith("UFC - S2025E04-05-06 - "));
            Assert.That(badRoot.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(noSeries.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        });
    }
}
