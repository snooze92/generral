using System.Net;
using System.Text.Json.Nodes;
using Generral.Core.Configuration;
using Generral.Core.Datastore;
using Generral.Core.Extras.Metadata;
using Generral.Core.Extras.Metadata.Consumers.PlexNfo;
using Generral.Core.MediaFiles;
using Generral.Core.Sources.Http;
using Generral.Core.Test.Sources;
using Generral.Core.Tv;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Generral.Core.Test.Extras;

public class MetadataArtworkTests
{
    private static readonly byte[] RealJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3];

    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;
    private FakeHandler _handler = null!;
    private string _seriesPath = null!;
    private int _seriesId;

    [SetUp]
    public void SetUp()
    {
        _seriesPath = Path.Combine(Path.GetTempPath(), "generral-art-" + Guid.NewGuid().ToString("N"), "XFL");
        Directory.CreateDirectory(Path.Combine(_seriesPath, "Season 2026"));
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _handler = new FakeHandler();

        var services = new ServiceCollection().AddLogging();
        services.AddDbContext<GenerralDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IUiSettingsService, UiSettingsService>();
        services.AddSingleton<IMetadataConsumer, PlexNfoMetadata>();
        services.AddScoped<MetadataService>();
        services.AddHttpClient(SourceFetcherFactory.ClientName).ConfigurePrimaryHttpMessageHandler(() => _handler);
        _services = services.BuildServiceProvider();

        using var db = Db();
        db.Database.Migrate();
        var series = new Series { Title = "Example Fight League", SortTitle = "x", CleanTitle = "x", TitleSlug = "xfl", Path = _seriesPath, RootFolderPath = Path.GetDirectoryName(_seriesPath)!, Year = 2026 };
        db.Series.Add(series);
        db.SaveChanges();
        _seriesId = series.Id;
        db.Seasons.Add(new Season { SeriesId = series.Id, SeasonNumber = 2026 });
        var file = new EpisodeFile { SeriesId = series.Id, SeasonNumber = 2026, RelativePath = Path.Combine("Season 2026", "XFL - S2026E01.mkv"), Path = "" };
        db.EpisodeFiles.Add(file);
        db.SaveChanges();
        db.Episodes.Add(new Episode
        {
            SeriesId = series.Id,
            SeasonNumber = 2026,
            EpisodeNumber = 1,
            Title = "XFL 1",
            EpisodeFileId = file.Id,
            AirDateUtc = DateTime.UtcNow.AddDays(7),
            Images = new ImageSet { Thumb = "https://img.example.org/e1.jpg" },
        });
        db.Metadata.Add(new MetadataDefinition { Name = "Plex", Implementation = PlexNfoMetadata.Name, Enable = true, Settings = new JsonObject { ["seriesMetadata"] = false, ["episodeMetadata"] = false } });
        db.SaveChanges();
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _connection.Dispose();
        _handler.Dispose();
        Directory.Delete(Path.GetDirectoryName(_seriesPath)!, recursive: true);
    }

    [Test]
    public async Task Generates_missing_posters_then_replaces_them_with_real_ones()
    {
        await SyncAsync();
        var poster = Path.Combine(_seriesPath, "poster.jpg");
        var season = Path.Combine(_seriesPath, "Season 2026", "Season2026.jpg");
        using (var bitmap = SKBitmap.Decode(poster))
        {
            Assert.That((bitmap.Width, bitmap.Height), Is.EqualTo((1000, 1500)));
        }

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(season), "season poster generated too");
            Assert.That(File.Exists(Path.Combine(_seriesPath, "fanart.jpg")), Is.False, "only posters are generated");
            Assert.That(_handler.RequestedUrls, Is.EqualTo(new[] { "https://img.example.org/e1.jpg" }), "episode thumbs are downloaded, never generated");
        });

        using (var db = Db())
        {
            db.Series.Single().Images = new ImageSet { Poster = "https://img.example.org/poster.jpg" };
            db.Episodes.Single().Images = new ImageSet { Thumb = "https://img.example.org/e1-v2.jpg" };
            db.SaveChanges();
        }

        await SyncAsync();
        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(poster), Is.EqualTo(RealJpeg), "a real URL replaces the generated poster");
            Assert.That(_handler.RequestedUrls, Does.Contain("https://img.example.org/e1-v2.jpg"), "a changed thumb URL rewrites the existing file's thumb");
        });

        using (var db = Db())
        {
            db.Series.Single().Images = new ImageSet();
            db.SaveChanges();
        }

        await SyncAsync();
        Assert.That(File.ReadAllBytes(poster), Is.EqualTo(RealJpeg), "a generated poster never overwrites a downloaded one");
    }

    [Test]
    public async Task Hand_placed_posters_are_kept_and_the_setting_turns_generation_off()
    {
        var poster = Path.Combine(_seriesPath, "poster.jpg");
        File.WriteAllBytes(poster, RealJpeg);
        using (var db = Db())
        {
            db.Metadata.Single().Settings = new JsonObject { ["seriesMetadata"] = false, ["episodeMetadata"] = false, ["generatePosters"] = false };
            db.SaveChanges();
        }

        await SyncAsync();

        Assert.Multiple(() =>
        {
            Assert.That(File.ReadAllBytes(poster), Is.EqualTo(RealJpeg));
            Assert.That(File.Exists(Path.Combine(_seriesPath, "Season 2026", "Season2026.jpg")), Is.False);
        });
    }

    [Test]
    public void Long_titles_still_render()
    {
        var jpeg = PosterGenerator.Render(new GeneratedPoster(string.Concat(Enumerable.Repeat("Supercalifragilistic Championship ", 12)), "A caption"));
        using var bitmap = SKBitmap.Decode(jpeg);
        Assert.That(bitmap.Height, Is.EqualTo(PosterGenerator.Height));
    }

    private async Task SyncAsync()
    {
        using var scope = _services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MetadataService>().SyncSeriesAsync(_seriesId);
    }

    private GenerralDbContext Db() => _services.CreateScope().ServiceProvider.GetRequiredService<GenerralDbContext>();

    private sealed class FakeHandler : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(RealJpeg) });
        }
    }
}
