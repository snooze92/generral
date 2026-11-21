using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Mappings;
using Generral.Core.Sources;
using Generral.Core.Tags;
using Generral.Core.Tv;
using Ical.Net.CalendarComponents;
using Microsoft.Extensions.DependencyInjection;
using IcalCalendar = Ical.Net.Calendar;
using IcalEventStatus = Ical.Net.EventStatus;

namespace Generral.Integration.Test;

public class CalendarApiTests
{
    private const string FeedPath = "/feed/v1/calendar/generral.ics";

    private GenerralTestHost _host = null!;
    private HttpClient _client = null!;
    private Seed _seed = null!;

    [SetUp]
    public async Task SetUp()
    {
        _host = await GenerralTestHost.StartAsync();
        _client = _host.CreateClient();
        _client.DefaultRequestHeaders.Add("X-Api-Key", _host.ApiKey);
        _seed = await SeedAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        _client.Dispose();
        await _host.DisposeAsync();
    }

    [Test]
    public async Task The_calendar_defaults_to_today_and_the_next_two_days_and_hides_unmonitored_and_orphaned_episodes()
    {
        var episodes = await CalendarAsync("/api/v1/calendar");
        var withUnmonitored = await CalendarAsync("/api/v1/calendar?unmonitored=true");

        Assert.Multiple(() =>
        {
            Assert.That(Ids(episodes), Is.EqualTo(new[] { _seed.Soon }));
            Assert.That(Ids(withUnmonitored), Is.EqualTo(new[] { _seed.Soon, _seed.Unmonitored, _seed.Daily }));
            Assert.That((string)episodes[0]!["title"]!, Is.EqualTo("Final"));
            Assert.That((int)episodes[0]!["seriesId"]!, Is.EqualTo(_seed.CupId));
            Assert.That((string)episodes[0]!["status"]!, Is.EqualTo("active"));
            Assert.That(episodes[0]!["series"], Is.Null);
        });
    }

    [Test]
    public async Task The_calendar_filters_by_range_and_tags_and_can_include_the_series()
    {
        var start = Uri.EscapeDataString(_seed.Now.AddDays(-4).ToString("O"));
        var end = Uri.EscapeDataString(_seed.Now.AddDays(6).ToString("O"));
        var range = $"/api/v1/calendar?start={start}&end={end}&unmonitored=true";

        var all = await CalendarAsync(range);
        var byLabel = await CalendarAsync(range + "&tags=sport&includeSeries=true");
        var byId = await CalendarAsync(range + $"&tags=999,{_seed.NewsTagId}");
        var unknown = await CalendarAsync(range + "&tags=nope");

        Assert.Multiple(() =>
        {
            Assert.That(Ids(all), Is.EqualTo(new[] { _seed.Past, _seed.Soon, _seed.Unmonitored, _seed.Daily, _seed.Later }));
            Assert.That(Ids(byLabel), Is.EqualTo(new[] { _seed.Past, _seed.Soon, _seed.Unmonitored, _seed.Later }));
            Assert.That((string)byLabel[0]!["series"]!["title"]!, Is.EqualTo("Cup"));
            Assert.That((int)byLabel[0]!["series"]!["statistics"]!["totalEpisodeCount"]!, Is.EqualTo(7));
            Assert.That(Ids(byId), Is.EqualTo(new[] { _seed.Daily }));
            Assert.That(unknown, Is.Empty);
        });
    }

    [Test]
    public async Task The_feed_needs_the_api_key_or_a_session_and_answers_401_without()
    {
        using var anonymous = _host.CreateClient();
        using var signedIn = await _host.CreateSignedInClientAsync();

        var none = await anonymous.GetAsync(FeedPath);
        var wrong = await anonymous.GetAsync(FeedPath + "?apikey=wrong");
        var cookie = await signedIn.GetAsync(FeedPath);
        var query = await anonymous.GetAsync(FeedPath + "?apikey=" + _host.ApiKey);

        Assert.Multiple(() =>
        {
            Assert.That(none.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(wrong.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(cookie.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(query.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(query.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/calendar"));
        });
    }

    [Test]
    public async Task The_feed_lists_episodes_as_ical_events()
    {
        var calendar = await FeedAsync("");
        var events = calendar.Events.ToList();
        var final = events.Single(e => e.Uid == $"generral_episode_{_seed.Soon}");
        var later = events.Single(e => e.Uid == $"generral_episode_{_seed.Later}");
        var past = events.Single(e => e.Uid == $"generral_episode_{_seed.Past}");
        var daily = (await FeedAsync("unmonitored=true")).Events.Single(e => e.Uid == $"generral_episode_{_seed.Daily}");

        Assert.Multiple(() =>
        {
            Assert.That(calendar.Properties["X-WR-CALNAME"]!.Value, Is.EqualTo("Generral Schedule"));
            Assert.That(events.Select(e => e.Uid), Is.EqualTo(new[] { _seed.Past, _seed.Soon, _seed.Later }.Select(id => $"generral_episode_{id}")));
            Assert.That(final.Summary, Is.EqualTo("Cup - S2026E02 - Final"));
            Assert.That(final.Description, Is.EqualTo("The final."));
            Assert.That(final.Categories, Is.EqualTo(new[] { "Sport TV" }));
            Assert.That(final.Status, Is.EqualTo(IcalEventStatus.Tentative));
            Assert.That(final.Start!.AsUtc, Is.EqualTo(_seed.Now.AddHours(1)));
            Assert.That(final.End!.AsUtc, Is.EqualTo(_seed.Now.AddHours(1).AddMinutes(120)));
            Assert.That(later.End!.AsUtc, Is.EqualTo(_seed.Now.AddDays(5).AddMinutes(90)));
            Assert.That(past.End, Is.Null);
            Assert.That(daily.Summary, Is.EqualTo("News - Morning show"));
            Assert.That(daily.Categories, Is.Empty);
        });
    }

    [Test]
    public async Task The_feed_filters_by_days_tags_and_premieres_and_can_use_all_day_events()
    {
        var narrow = await FeedAsync("pastDays=1&futureDays=3&unmonitored=true");
        var news = await FeedAsync("tags=news&unmonitored=true");
        var premieres = await FeedAsync("premieresOnly=true");
        var allDay = (await FeedAsync("asAllDay=true")).Events.Single(e => e.Uid == $"generral_episode_{_seed.Past}");

        Assert.Multiple(() =>
        {
            Assert.That(Uids(narrow), Is.EqualTo(new[] { _seed.Soon, _seed.Unmonitored, _seed.Daily }));
            Assert.That(Uids(news), Is.EqualTo(new[] { _seed.Daily }));
            Assert.That(Uids(premieres), Is.EqualTo(new[] { _seed.Past }));
            Assert.That(allDay.IsAllDay, Is.True);
            Assert.That(allDay.Start!.Date, Is.EqualTo(new DateOnly(2001, 2, 3)));
        });
    }

    private async Task<JsonArray> CalendarAsync(string url) => (await _client.GetFromJsonAsync<JsonArray>(url))!;

    private async Task<IcalCalendar> FeedAsync(string query)
    {
        using var anonymous = _host.CreateClient();
        var response = await anonymous.GetAsync($"{FeedPath}?apikey={_host.ApiKey}&{query}");
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        return IcalCalendar.Load(await response.Content.ReadAsStringAsync())!;
    }

    private static int[] Ids(JsonArray episodes) => episodes.Select(e => (int)e!["id"]!).ToArray();

    private static int[] Uids(IcalCalendar calendar) =>
        calendar.Events.Select(e => int.Parse(e.Uid!["generral_episode_".Length..], System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    private sealed record Seed(DateTime Now, int CupId, int NewsTagId, int Past, int Soon, int Unmonitored, int Daily, int Later);

    /// <summary>
    /// "Cup" (tag sport, standard) and "News" (tag news, daily), with episodes around now: −3 days, +1 h, +2 h unmonitored, +3 h
    /// (News), +5 days (no runtime, linked event ends 90 min later); and some that never show: an orphaned one, one 10 days ago, one 40 days ahead and one without a date.
    /// </summary>
    private async Task<Seed> SeedAsync()
    {
        using var scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GenerralDbContext>();
        var utcNow = DateTime.UtcNow;
        var now = new DateTime(utcNow.Ticks - (utcNow.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        var sport = new Tag { Label = "sport" };
        var news = new Tag { Label = "news" };
        db.Tags.AddRange(sport, news);
        await db.SaveChangesAsync();

        var cup = NewSeries("Cup", sport.Id);
        cup.Network = "Sport TV";
        var newsSeries = NewSeries("News", news.Id);
        newsSeries.SeriesType = SeriesType.Daily;
        db.Seasons.AddRange(new Season { Series = cup, SeasonNumber = 2026 }, new Season { Series = newsSeries, SeasonNumber = 2026 });

        Episode Cup(int number, DateTime? airDateUtc) => new() { Series = cup, SeasonNumber = 2026, EpisodeNumber = number, Title = $"Cup {number}", AirDateUtc = airDateUtc };

        var past = Cup(1, now.AddDays(-3));
        past.AirDate = new DateOnly(2001, 2, 3);
        var soon = Cup(2, now.AddHours(1));
        soon.Title = "Final";
        soon.Overview = "The final.";
        soon.Runtime = 120;
        var unmonitored = Cup(3, now.AddHours(2));
        unmonitored.Monitored = false;
        var later = Cup(4, now.AddDays(5));
        var orphaned = Cup(5, now.AddHours(1));
        orphaned.Status = EpisodeStatus.Orphaned;
        var daily = new Episode { Series = newsSeries, SeasonNumber = 2026, EpisodeNumber = 1, Title = "Morning show", AirDateUtc = now.AddHours(3), Monitored = false };

        var source = new Source { Name = "Cup source", Type = "static" };
        var mapping = new Mapping { Name = "Cup", Series = cup, Source = source };
        db.MappingVersions.Add(new MappingVersion { Mapping = mapping, Version = 1 });
        db.EpisodeLinks.Add(new EpisodeLink
        {
            Mapping = mapping,
            Episode = later,
            Event = new Event { Source = source, Key = "later", Title = "later", Hash = "later", Start = later.AirDateUtc, End = later.AirDateUtc!.Value.AddMinutes(90) },
        });
        db.Episodes.AddRange(past, soon, unmonitored, later, orphaned, daily, Cup(6, now.AddDays(-10)), Cup(7, now.AddDays(40)), Cup(8, null));
        await db.SaveChangesAsync();

        return new Seed(now, cup.Id, news.Id, past.Id, soon.Id, unmonitored.Id, daily.Id, later.Id);
    }

    private static Series NewSeries(string title, int tagId) => new()
    {
        Title = title,
        SortTitle = title.ToLowerInvariant(),
        CleanTitle = title.ToLowerInvariant(),
        TitleSlug = title.ToLowerInvariant(),
        Path = $"/tv/{title}",
        RootFolderPath = "/tv",
        Tags = [tagId],
    };
}
