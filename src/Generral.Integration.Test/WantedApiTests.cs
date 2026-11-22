using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Tv;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Integration.Test;

public class WantedApiTests
{
    [Test]
    public async Task Missing_lists_aired_episodes_without_a_file_and_cutoff_is_empty()
    {
        await using var host = await GenerralTestHost.StartAsync();
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", host.ApiKey);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
            var profileId = await db.QualityProfiles.Select(p => p.Id).FirstAsync();
            var series = new Series { Title = "UFC", SortTitle = "ufc", CleanTitle = "ufc", TitleSlug = "ufc", Path = "/tv/UFC", RootFolderPath = "/tv", QualityProfileId = profileId };
            Episode Episode(int number, int daysAgo, bool monitored = true) => new()
            {
                Series = series,
                SeasonNumber = 2026,
                EpisodeNumber = number,
                Title = $"UFC {number}",
                AirDateUtc = DateTime.UtcNow.AddDays(-daysAgo),
                Monitored = monitored,
            };
            db.Episodes.AddRange(Episode(1, 10), Episode(2, 3), Episode(3, -5), Episode(4, 1, monitored: false));
            await db.SaveChangesAsync();
        }

        var missing = (await client.GetFromJsonAsync<JsonObject>("/api/v1/wanted/missing?includeSeries=true&sortDirection=descending&pageSize=1"))!;
        var unmonitored = (await client.GetFromJsonAsync<JsonObject>("/api/v1/wanted/missing?monitored=false"))!;
        var cutoff = (await client.GetFromJsonAsync<JsonObject>("/api/v1/wanted/cutoff"))!;
        var search = await client.PostAsJsonAsync("/api/v1/command", new JsonObject { ["name"] = "CutoffUnmetEpisodeSearch" });

        Assert.Multiple(() =>
        {
            Assert.That((int)missing["totalRecords"]!, Is.EqualTo(2));
            Assert.That((int)missing["pageSize"]!, Is.EqualTo(1));
            Assert.That((string)missing["sortKey"]!, Is.EqualTo("episodes.airDateUtc"));
            Assert.That((string)missing["records"]![0]!["title"]!, Is.EqualTo("UFC 2"));
            Assert.That((string)missing["records"]![0]!["series"]!["title"]!, Is.EqualTo("UFC"));
            Assert.That(unmonitored["records"]!.AsArray().Select(r => (string)r!["title"]!), Is.EqualTo(new[] { "UFC 4" }));
            Assert.That((int)cutoff["totalRecords"]!, Is.EqualTo(0));
            Assert.That(search.IsSuccessStatusCode, Is.True);
        });
    }
}
