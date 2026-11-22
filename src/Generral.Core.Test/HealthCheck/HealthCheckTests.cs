using Generral.Core.Datastore;
using Generral.Core.HealthCheck;
using Generral.Core.Indexers;
using Generral.Core.Sources;
using Generral.Core.Tv;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Generral.Core.Test.HealthCheck;

public class HealthCheckTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

    private SqliteConnection _connection = null!;
    private GenerralDbContext _db = null!;
    private ProviderStatusTracker _status = null!;

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _db = new GenerralDbContext(new DbContextOptionsBuilder<GenerralDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        _status = new ProviderStatusTracker(new FixedTime(Now));
    }

    [TearDown]
    public void TearDown()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    [Test]
    public async Task Indexer_rss_check_errors_without_rss_indexers_and_warns_when_all_fail()
    {
        var check = new IndexerRssCheck(_db, _status);
        var none = await check.CheckAsync(CancellationToken.None);

        var indexer = new Indexer { Name = "Tracker", Implementation = IndexerImplementations.Torznab };
        _db.Indexers.Add(indexer);
        await _db.SaveChangesAsync();
        var ok = await check.CheckAsync(CancellationToken.None);

        _status.RecordFailure(ProviderKind.Indexer, indexer.Id, indexer.Name, "HTTP 500");
        var failing = await check.CheckAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(none.Type, Is.EqualTo(HealthCheckResult.Error));
            Assert.That(none.Message, Does.StartWith("No indexers available with RSS sync enabled"));
            Assert.That(ok.Type, Is.EqualTo(HealthCheckResult.Ok));
            Assert.That(failing.Type, Is.EqualTo(HealthCheckResult.Warning));
            Assert.That(_status.ConsumeChanged(), Is.True);
            Assert.That(_status.ConsumeChanged(), Is.False);
        });
    }

    [Test]
    public async Task Root_folder_check_reports_missing_series_root_folders()
    {
        using var folders = new TestFolders();
        _db.Series.Add(new Series { Title = "UFC", SortTitle = "ufc", CleanTitle = "ufc", TitleSlug = "ufc", Path = "/nope/ufc", RootFolderPath = "/nope" });
        _db.Series.Add(new Series { Title = "F1", SortTitle = "f1", CleanTitle = "f1", TitleSlug = "f1", Path = Path.Combine(folders.ConfigFolder, "f1"), RootFolderPath = folders.ConfigFolder });
        await _db.SaveChangesAsync();

        var result = await new RootFolderCheck(_db).CheckAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.Type, Is.EqualTo(HealthCheckResult.Error));
            Assert.That(result.Message, Is.EqualTo("Missing root folder: /nope"));
        });
    }

    [Test]
    public async Task Source_refresh_check_warns_then_errors_after_a_day()
    {
        _db.Sources.Add(new Source { Name = "TheSportsDB", Type = "static", LastSuccess = Now.AddHours(-2), LastError = "HTTP 429" });
        await _db.SaveChangesAsync();
        var check = new SourceRefreshCheck(_db, new FixedTime(Now));

        var recent = await check.CheckAsync(CancellationToken.None);
        await _db.Sources.ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSuccess, Now.AddDays(-2)));
        var old = await check.CheckAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(recent.Type, Is.EqualTo(HealthCheckResult.Warning));
            Assert.That(recent.Message, Is.EqualTo("Source TheSportsDB is failing to refresh: HTTP 429"));
            Assert.That(old.Type, Is.EqualTo(HealthCheckResult.Error));
        });
    }

    private sealed class FixedTime(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
