using System.Text.Json.Nodes;
using Generral.Core.DecisionEngine;
using Generral.Core.Indexers;
using Generral.Core.Languages;
using Generral.Core.Profiles;
using Generral.Core.Qualities;
using Generral.Core.Test.Sources;
using Generral.Core.Tv;

namespace Generral.Core.Test.DecisionEngine;

public class DownloadDecisionMakerTests
{
    private static readonly DateTime Now = new(2026, 4, 14, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Ufc314 = new(2026, 4, 12, 22, 0, 0, DateTimeKind.Utc);

    private readonly DownloadDecisionMaker _maker = new(new FakeTime(Now));
    private readonly QualityProfile _profile = QualityProfileService.GetDefaultProfile("HD", Quality.DefaultQualityDefinitions, Quality.WEBDL1080p,
        Quality.HDTV720p, Quality.WEBDL720p, Quality.HDTV1080p, Quality.WEBDL1080p, Quality.WEBRip1080p);

    private readonly Series _ufc = new() { Id = 1, Title = "UFC", SortTitle = "ufc", CleanTitle = "ufc", TitleSlug = "ufc", Path = "/tv/UFC", RootFolderPath = "/tv", QualityProfileId = 1 };

    /// <summary>The terms the UFC numbered-parts mapping (spec 05 worked example) renders for UFC 314's parts, and UFC 31 to check that 314 doesn't match it.</summary>
    private List<Episode> UfcEpisodes()
    {
        Episode Part(int id, int number, string part, string[] must, string[] mustNot, int? ufc = 314, DateTime? air = null) => new()
        {
            Id = id,
            SeriesId = 1,
            Series = _ufc,
            SeasonNumber = 2026,
            EpisodeNumber = number,
            Title = $"UFC {ufc} - {part}",
            AirDateUtc = air ?? Ufc314,
            Runtime = 120,
            SearchTerms = new JsonObject
            {
                ["queries"] = new JsonArray($"UFC {ufc}"),
                ["must"] = new JsonArray([$@"\bUFC ?{ufc}\b", .. must.Select(m => (JsonNode)m)]),
                ["mustNot"] = new JsonArray([@"\bfight ?night\b", .. mustNot.Select(m => (JsonNode)m)]),
                ["dateWindow"] = new JsonObject { ["before"] = "6h", ["after"] = "90d" },
            },
        };

        return
        [
            Part(11, 11, "Early Prelims", [@"\bearly ?prelims?\b"], []),
            Part(12, 12, "Prelims", [@"\bprelims?\b"], [@"\bearly ?prelims?\b"]),
            Part(13, 13, "Main Card", [], [@"\bprelims?\b"]),
            Part(1, 1, "Main Card", [], [@"\bprelims?\b"], ufc: 31, air: new DateTime(2001, 5, 4, 2, 0, 0, DateTimeKind.Utc)),
        ];
    }

    private DecisionContext Context(IEnumerable<Episode> episodes, int[]? requested = null, bool userInvoked = false) => new()
    {
        Scope = episodes.Select(e => new SearchCandidate(e, SearchTerms.From(e.SearchTerms))).ToList(),
        Requested = requested?.ToHashSet(),
        QualityProfiles = new Dictionary<int, QualityProfile> { [1] = _profile },
        UserInvoked = userInvoked,
    };

    private static ReleaseInfo Release(string title, DateTime? published = null, long size = 6L << 30, int seeders = 50) => new()
    {
        Guid = title,
        Title = title,
        Size = size,
        Seeders = seeders,
        Peers = seeders,
        PublishDate = published ?? Ufc314.AddHours(4),
        IndexerId = 1,
        Indexer = "Tracker",
    };

    [TestCase("UFC 314 Volkanovski vs Lopes 1080p WEB h264-GRP", 13)]
    [TestCase("UFC.314.Main.Card.720p.HDTV.x264-GRP", 13)]
    [TestCase("UFC_314_Prelims_1080p_WEB-DL", 12)]
    [TestCase("UFC314 Early Prelims 1080p WEB h264", 11)]
    [TestCase("ufc 31 Locked & Loaded 720p WEB h264", 1)]
    public void Ufc_titles_match_their_part(string title, int episodeId)
    {
        var decision = _maker.GetDecision(Release(title), Context(UfcEpisodes(), userInvoked: true));

        Assert.That(decision.RemoteEpisode.Episodes.Select(e => e.Id), Is.EqualTo(new[] { episodeId }), string.Join("; ", decision.Rejections));
    }

    [TestCase("UFC Fight Night 314 1080p WEB h264", "Does not match any wanted episode")]
    [TestCase("UFC 3145 1080p WEB h264", "Does not match any wanted episode")]
    [TestCase("UFC 314 Prelims 480p HDTV x264", "SDTV is not wanted in profile")]
    public void Ufc_rejections(string title, string reason)
    {
        var decision = _maker.GetDecision(Release(title), Context(UfcEpisodes(), userInvoked: true));

        Assert.That(decision.Rejections, Does.Contain(reason));
    }

    [TestCase("UFC 314 1080p WEB h264", 1, null)]
    [TestCase("UFC 314 MULTi 1080p WEB h264", 1, null)]
    [TestCase("UFC 314 TRUEFRENCH 1080p WEB h264", 1, "Language French is not wanted (profile: English)")]
    [TestCase("UFC 314 RUS 1080p WEB h264", -2, "Language Russian is not wanted (profile: Original)")]
    [TestCase("UFC 314 RUS 1080p WEB h264", -1, null)]
    [TestCase("UFC 314 TRUEFRENCH 1080p WEB h264", 2, null)]
    [TestCase("UFC 314 1080p WEB h264", 2, "Language Unknown is not wanted (profile: French)")]
    public void Language_must_be_the_profile_one(string title, int language, string? reason)
    {
        var profile = QualityProfileService.GetDefaultProfile("HD", Quality.DefaultQualityDefinitions, Quality.WEBDL1080p, Quality.WEBDL1080p);
        profile.Language = Language.Find(language)!;
        var context = Context(UfcEpisodes(), userInvoked: true) with { QualityProfiles = new Dictionary<int, QualityProfile> { [1] = profile } };

        var decision = _maker.GetDecision(Release(title), context);

        Assert.That(decision.Rejections.Where(r => r.StartsWith("Language", StringComparison.Ordinal)), reason is null ? Is.Empty : Is.EqualTo(new[] { reason }));
    }

    [Test]
    public void Requested_episode_explains_why_it_did_not_match_and_others_are_not_requested()
    {
        var context = Context(UfcEpisodes(), requested: [13]);

        var prelims = _maker.GetDecision(Release("UFC 314 Prelims 1080p WEB h264"), context);
        var other = _maker.GetDecision(Release("UFC 999 1080p WEB h264"), context with { Requested = new HashSet<int> { 13 } });

        Assert.Multiple(() =>
        {
            Assert.That(prelims.RemoteEpisode.Episodes.Single().Id, Is.EqualTo(12));
            Assert.That(prelims.Rejections, Is.EqualTo(new[] { "Matches S2026E12, which wasn't requested" }));
            Assert.That(other.Rejections.Single(), Does.StartWith(@"Does not match S2026E13: Does not match required rule '\bUFC ?314\b'"));
        });
    }

    [Test]
    public void Rwc_team_names_in_any_order_within_the_date_window()
    {
        var rwc = new Series { Id = 2, Title = "Rugby World Cup", SortTitle = "rugby world cup", CleanTitle = "rugbyworldcup", TitleSlug = "rwc", Path = "/tv/RWC", RootFolderPath = "/tv", QualityProfileId = 1 };
        Episode Match(int id, string home, string homeCode, string away, string awayCode, DateTime air) => new()
        {
            Id = id,
            SeriesId = 2,
            Series = rwc,
            SeasonNumber = 2027,
            EpisodeNumber = id,
            Title = $"Pool - {home} vs {away}",
            AirDateUtc = air,
            SearchTerms = new JsonObject
            {
                ["must"] = new JsonArray(@"\b(rugby ?world ?cup|rwc)\b", @"\b2027\b", $@"\b({home}|{homeCode})\b", $@"\b({away}|{awayCode})\b"),
                ["mustNot"] = new JsonArray(@"\b(highlights?|extended ?highlights|preview|review)\b"),
                ["dateWindow"] = new JsonObject { ["before"] = "3h", ["after"] = "120d" },
            },
        };
        var opener = new DateTime(2027, 10, 1, 19, 0, 0, DateTimeKind.Utc);
        var episodes = new[]
        {
            Match(1, "New Zealand", "NZL", "France", "FRA", opener),
            Match(2, "Ireland", "IRL", "South Africa", "RSA", opener.AddDays(1)),
        };
        var maker = new DownloadDecisionMaker(new FakeTime(opener.AddDays(3)));
        var context = Context(episodes, userInvoked: true);

        var reversed = maker.GetDecision(Release("Rugby.World.Cup.2027.France.vs.New.Zealand.1080p.WEB.h264-GRP", opener.AddHours(4)), context);
        var codes = maker.GetDecision(Release("RWC 2027 IRL v RSA 720p HDTV x264", opener.AddDays(1).AddHours(3)), context);
        var early = maker.GetDecision(Release("RWC 2027 NZL v FRA 1080p WEB h264", opener.AddHours(-5)), context);
        var highlights = maker.GetDecision(Release("RWC 2027 NZL v FRA Highlights 1080p WEB h264", opener.AddHours(4)), context);
        var ambiguous = maker.GetDecision(Release("Rugby World Cup 2027 New Zealand France Ireland South Africa 1080p WEB h264", opener.AddDays(2)), context);

        Assert.Multiple(() =>
        {
            Assert.That(reversed.Approved, Is.True, string.Join("; ", reversed.Rejections));
            Assert.That(reversed.RemoteEpisode.Episodes.Single().Id, Is.EqualTo(1));
            Assert.That(codes.Approved, Is.True, string.Join("; ", codes.Rejections));
            Assert.That(codes.RemoteEpisode.Episodes.Single().Id, Is.EqualTo(2));
            Assert.That(early.Rejections.Single(), Does.StartWith("Published 2027-10-01, outside the date window of S2027E01"));
            Assert.That(highlights.Rejections, Is.EqualTo(new[] { "Does not match any wanted episode" }));
            Assert.That(ambiguous.Rejections, Is.EqualTo(new[] { "Ambiguous: matches S2027E01, S2027E02" }));
        });
    }

    [Test]
    public void Standard_specifications_reject_with_reasons()
    {
        var episodes = UfcEpisodes();
        episodes[2].Monitored = false;
        var context = Context(episodes) with
        {
            ReleaseProfiles = [new ReleaseProfile { Required = ["WEB"], Ignored = ["/\\bcam\\b/i"] }],
            Indexers = new Dictionary<int, Indexer> { [1] = new() { Id = 1, Name = "Tracker", Implementation = "Torznab", Settings = new JsonObject { ["minimumSeeders"] = 5 } } },
        };

        var decision = _maker.GetDecision(Release("UFC 314 1080p HDTV CAM x264", size: 100L << 30, seeders: 2) with { DownloadProtocol = DownloadProtocol.Usenet }, context);
        var notAired = new DownloadDecisionMaker(new FakeTime(Ufc314.AddHours(-7))).GetDecision(Release("UFC 314 1080p WEB h264", Ufc314.AddHours(-7)), Context(episodes, userInvoked: true));

        Assert.Multiple(() =>
        {
            Assert.That(decision.Rejections, Is.EqualTo(new[]
            {
                "Usenet is not supported",
                "100.0 GB is larger than maximum allowed 29.3 GB (for 120min)",
                "Not enough seeders: 2. Minimum seeders: 5",
                "Does not contain one of the required terms: WEB",
                "Episode is not monitored",
            }));
            Assert.That(notAired.Rejections.Single(), Does.StartWith("S2026E13 has not aired yet"));
        });
    }

    [TestCase(new int[0], new int[0], true)]
    [TestCase(new[] { 7 }, new int[0], false)]
    [TestCase(new[] { 7 }, new[] { 7 }, true)]
    [TestCase(new[] { 7 }, new[] { 8 }, false)]
    [TestCase(new int[0], new[] { 8 }, true)]
    public void Tagged_indexers_only_serve_series_sharing_a_tag(int[] indexerTags, int[] seriesTags, bool approved)
    {
        var series = new Series { Id = 1, Title = "UFC", SortTitle = "ufc", CleanTitle = "ufc", TitleSlug = "ufc", Path = "/tv/UFC", RootFolderPath = "/tv", QualityProfileId = 1, Tags = [.. seriesTags] };
        var episodes = UfcEpisodes();
        episodes.ForEach(e => e.Series = series);
        var context = Context(episodes, userInvoked: true) with
        {
            Indexers = new Dictionary<int, Indexer> { [1] = new() { Id = 1, Name = "Tracker", Implementation = "Torznab", Tags = [.. indexerTags] } },
        };

        var decision = _maker.GetDecision(Release("UFC 314 1080p WEB h264"), context);

        Assert.That(decision.Rejections.Contains("Series tags do not match any of the indexer tags"), Is.EqualTo(!approved));
    }

    [Test]
    public void Comparer_prefers_quality_then_revision_priority_seeders_size_age()
    {
        var episode = UfcEpisodes()[2];
        DownloadDecision D(string title, int seeders = 50, long size = 6L << 30, int priority = 25, int ageHours = 4)
        {
            var remote = new RemoteEpisode
            {
                Release = Release(title, Ufc314.AddHours(ageHours), size, seeders) with { IndexerPriority = priority },
                Quality = QualityParser.ParseQuality(title),
                Series = _ufc,
                QualityProfile = _profile,
                Episodes = [episode],
            };
            return new DownloadDecision(remote, []);
        }

        var hdtv = D("UFC 314 720p HDTV x264");
        var web = D("UFC 314 1080p WEB-DL h264");
        var proper = D("UFC 314 1080p WEB-DL PROPER h264");
        var lowPriority = D("UFC 314 1080p WEB-DL PROPER h264 b", priority: 50);
        var fewSeeders = D("UFC 314 1080p WEB-DL PROPER h264 c", seeders: 3);
        var huge = D("UFC 314 1080p WEB-DL PROPER h264 d", size: 40L << 30);
        var older = D("UFC 314 1080p WEB-DL PROPER h264 e", ageHours: 1);

        var sorted = DownloadDecisionComparer.Prioritize([hdtv, older, fewSeeders, web, huge, lowPriority, proper]);

        Assert.That(sorted, Is.EqualTo(new[] { proper, older, huge, fewSeeders, lowPriority, web, hdtv }));
    }

    [Test]
    public void Interactive_order_is_approved_then_quality_then_seeders()
    {
        var episode = UfcEpisodes()[2];
        DownloadDecision D(string title, int seeders, params string[] rejections) => new(new RemoteEpisode
        {
            Release = Release(title, seeders: seeders),
            Quality = QualityParser.ParseQuality(title),
            Series = _ufc,
            QualityProfile = _profile,
            Episodes = rejections.Contains("unmatched") ? [] : [episode],
        }, rejections);

        var webFew = D("UFC 314 1080p WEB-DL h264 a", 12);
        var webMany = D("UFC 314 1080p WEB-DL h264 b", 900);
        var hdtvMost = D("UFC 314 720p HDTV x264", 5000);
        var rejectedBest = D("UFC 314 2160p WEB-DL h265", 9000, "Too big");
        var rejectedWorse = D("UFC 314 720p HDTV x264 c", 9000, "Too big");
        var unmatched = D("UFC 314 1080p WEB-DL h264 d", 9999, "unmatched");

        var sorted = DownloadDecisionComparer.InteractiveOrder([unmatched, rejectedWorse, hdtvMost, webFew, rejectedBest, webMany]);

        Assert.That(sorted, Is.EqualTo(new[] { webMany, webFew, hdtvMost, rejectedBest, rejectedWorse, unmatched }));
    }

    [Test]
    public void Title_normalisation()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ReleaseTitleMatcher.Normalize("Rugby.World_Cup+2027  Côte.d'Ivoire"), Is.EqualTo("Rugby World Cup 2027 Cote d'Ivoire"));
            Assert.That(ReleaseTitleMatcher.Check(SearchTerms.From(new JsonObject { ["must"] = new JsonArray(@"raw:UFC\.314") }), "UFC.314"), Is.Null);
            Assert.That(ReleaseTitleMatcher.Check(SearchTerms.From(new JsonObject { ["must"] = new JsonArray(@"raw:UFC\.314") }), "UFC 314"), Is.Not.Null);
            Assert.That(ReleaseTitleMatcher.Check(SearchTerms.From(new JsonObject { ["must"] = new JsonArray("(") }), "UFC"), Is.EqualTo("Invalid search rule '('"));
        });
    }
}
