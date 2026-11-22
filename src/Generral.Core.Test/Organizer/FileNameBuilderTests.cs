using System.Text.Json.Nodes;
using Generral.Core.MediaFiles;
using Generral.Core.Organizer;
using Generral.Core.Qualities;
using Generral.Core.Tv;

namespace Generral.Core.Test.Organizer;

/// <summary>The spec 08 § Resulting layout examples (UC1/UC2/UC3) with the default naming settings.</summary>
public class FileNameBuilderTests
{
    private static readonly NamingConfig Defaults = new();

    [Test]
    public void UC1_UC2_event_with_part_and_colon()
    {
        var series = Series("UFC", "/data/media/generral/UFC");
        var episode = Episode(2025, 4, "UFC 314: Volkanovski vs. Lopes - Main Card");
        var input = new NamingInput(series, [episode], File(Quality.WEBDL1080p));

        Assert.Multiple(() =>
        {
            Assert.That(FileNameBuilder.GetSeriesFolder(series, Defaults), Is.EqualTo("UFC"));
            Assert.That(FileNameBuilder.BuildFilePath(input, Defaults, ".mkv"),
                Is.EqualTo("/data/media/generral/UFC/Season 2025/UFC - S2025E04 - UFC 314 - Volkanovski vs. Lopes - Main Card [WEBDL-1080p].mkv"));
        });
    }

    [Test]
    public void UC3_tournament_and_series_year_folder()
    {
        var series = Series("Rugby World Cup", "/data/media/generral/Rugby World Cup");
        var input = new NamingInput(series, [Episode(2027, 1, "Pool A - Australia vs Hong Kong China")], File(Quality.HDTV1080p));
        var withYear = Series("Rugby World Cup", "/x", year: 2027);

        Assert.Multiple(() =>
        {
            Assert.That(FileNameBuilder.BuildFileName(input, Defaults, ".mkv"), Is.EqualTo("Rugby World Cup - S2027E01 - Pool A - Australia vs Hong Kong China [HDTV-1080p].mkv"));
            Assert.That(FileNameBuilder.GetSeriesFolder(withYear, Defaults), Is.EqualTo("Rugby World Cup (2027)"));
            Assert.That(FileNameBuilder.GetSeasonFolder(series, 0, null, Defaults), Is.EqualTo("Specials"));
        });
    }

    [Test]
    public void Multi_episode_file_uses_prefixed_range_and_joins_titles()
    {
        var series = Series("UFC", "/tv/UFC");
        var episodes = new List<Episode> { Episode(2026, 13, "UFC 322 - Main Card"), Episode(2026, 11, "UFC 322 - Early Prelims"), Episode(2026, 12, "UFC 322 - Prelims") };
        var name = FileNameBuilder.BuildFileName(new NamingInput(series, episodes, File(Quality.WEBDL1080p)), Defaults);

        Assert.That(name, Is.EqualTo("UFC - S2026E11-E13 - UFC 322 - Early Prelims + UFC 322 - Prelims + UFC 322 - Main Card [WEBDL-1080p]"));
    }

    [Test]
    public void Generral_tokens_daily_format_and_proper()
    {
        var series = Series("F1", "/tv/F1");
        series.SeriesType = SeriesType.Daily;
        var episode = Episode(2026, 5, "Monaco GP - Race");
        episode.Id = 42;
        episode.AirDate = new DateOnly(2026, 5, 24);
        var file = File(Quality.WEBDL1080p);
        file.Quality.Revision.Version = 2;
        file.ReleaseGroup = "GRP";
        var config = Defaults with { DailyEpisodeFormat = "{Series Title} - {Air-Date} - {Part Title} - {Event:circuit_name} {Generral Id} {Event Key} [{Quality Full}]-{Release Group}" };
        var input = new NamingInput(series, [episode], file)
        {
            Info = new Dictionary<int, EpisodeNamingInfo> { [42] = new("2026-monaco", "race", "Race", new JsonObject { ["circuit_name"] = "Monte Carlo" }) },
        };

        Assert.That(FileNameBuilder.BuildFileName(input, config, ".mkv"), Is.EqualTo("F1 - 2026-05-24 - Race - Monte Carlo generral-ep-42 2026-monaco [WEBDL-1080p Proper]-GRP.mkv"));
    }

    [Test]
    public void Rename_off_keeps_the_release_name()
    {
        var file = File(Quality.WEBDL1080p);
        file.SceneName = "UFC.314.1080p.WEB-DL.h264-GRP";
        var input = new NamingInput(Series("UFC", "/tv/UFC"), [Episode(2025, 4, "x")], file);

        Assert.That(FileNameBuilder.BuildFileName(input, Defaults with { RenameEpisodes = false }, ".mkv"), Is.EqualTo("UFC.314.1080p.WEB-DL.h264-GRP.mkv"));
    }

    private static Series Series(string title, string path, int? year = null) =>
        new() { Title = title, SortTitle = title, CleanTitle = title, TitleSlug = title, Path = path, RootFolderPath = Path.GetDirectoryName(path)!, Year = year };

    private static Episode Episode(int season, int number, string title) => new() { SeasonNumber = season, EpisodeNumber = number, Title = title };

    private static EpisodeFile File(Quality quality) => new() { RelativePath = "", Path = "/downloads/x.mkv", Quality = new QualityModel { Quality = quality } };
}
