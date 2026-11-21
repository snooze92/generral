using System.Text.Json.Nodes;
using Generral.Core.Datastore;
using Generral.Core.Sources;
using Generral.Core.Sources.Extraction;
using Generral.Core.Sources.Types;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Generral.Core.Test.Sources;

public class SourceRefreshServiceTests
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _services = null!;
    private FakeSourceType _type = null!;
    private FakeTime _time = null!;
    private int _sourceId;

    private SourceRefreshService Refresh => _services.GetRequiredService<SourceRefreshService>();

    [SetUp]
    public void SetUp()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _type = new FakeSourceType();
        _time = new FakeTime(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc));

        var services = new ServiceCollection().AddLogging();
        services.AddDbContext<GenerralDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<TimeProvider>(_time);
        services.AddSingleton<ISourceType>(_type);
        services.AddSingleton<SourceTypeRegistry>();
        services.AddSingleton<SourceRefreshService>();
        _services = services.BuildServiceProvider();

        using var db = Db();
        db.Database.Migrate();
        var source = new Source { Name = "Fake", Type = FakeSourceType.TypeId };
        db.Sources.Add(source);
        db.SaveChanges();
        _sourceId = source.Id;
    }

    [TearDown]
    public void TearDown()
    {
        _services.Dispose();
        _connection.Dispose();
    }

    [Test]
    public async Task New_events_are_added_and_the_source_records_success()
    {
        _type.Returns(Ev("a", "A"), Ev("b", "B"));

        var result = await Refresh.RefreshAsync(_sourceId);

        using var db = Db();
        var source = db.Sources.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result!.Success, Is.True);
            Assert.That(result.Added, Is.EqualTo(2));
            Assert.That(db.Events.Select(e => e.Key).ToList(), Is.EquivalentTo(new[] { "a", "b" }));
            Assert.That(db.Events.All(e => e.FirstSeen == _time.GetUtcNow().UtcDateTime), Is.True);
            Assert.That(source.LastRefresh, Is.EqualTo(_time.GetUtcNow().UtcDateTime));
            Assert.That(source.LastSuccess, Is.EqualTo(_time.GetUtcNow().UtcDateTime));
            Assert.That(source.LastError, Is.Null);
        });
    }

    [Test]
    public async Task Unchanged_events_keep_their_hash_and_last_changed()
    {
        _type.Returns(Ev("a", "A"));
        await Refresh.RefreshAsync(_sourceId);
        var firstChanged = _time.GetUtcNow().UtcDateTime;
        _time.Advance(TimeSpan.FromHours(12));

        var result = await Refresh.RefreshAsync(_sourceId);

        using var db = Db();
        var ev = db.Events.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result!.Unchanged, Is.EqualTo(1));
            Assert.That(ev.LastChanged, Is.EqualTo(firstChanged));
            Assert.That(ev.LastSeen, Is.EqualTo(_time.GetUtcNow().UtcDateTime));
            Assert.That(db.EventRevisions.Count(), Is.Zero);
        });
    }

    [Test]
    public async Task A_changed_event_is_updated_and_its_previous_version_kept_as_a_revision()
    {
        _type.Returns(Ev("a", "UFC 322: TBD vs TBD"));
        await Refresh.RefreshAsync(_sourceId);
        _time.Advance(TimeSpan.FromHours(12));
        _type.Returns(Ev("a", "UFC 322: Della Maddalena vs Makhachev"));

        var result = await Refresh.RefreshAsync(_sourceId);

        using var db = Db();
        var ev = db.Events.Single();
        var revision = db.EventRevisions.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result!.Changed, Is.EqualTo(1));
            Assert.That(ev.Title, Is.EqualTo("UFC 322: Della Maddalena vs Makhachev"));
            Assert.That(ev.LastChanged, Is.EqualTo(_time.GetUtcNow().UtcDateTime));
            Assert.That(revision.EventId, Is.EqualTo(ev.Id));
            Assert.That(revision.Title, Is.EqualTo("UFC 322: TBD vs TBD"));
            Assert.That(revision.Hash, Is.Not.EqualTo(ev.Hash));
        });
    }

    [Test]
    public async Task Revisions_older_than_30_days_are_pruned()
    {
        _type.Returns(Ev("a", "v1"));
        await Refresh.RefreshAsync(_sourceId);
        _type.Returns(Ev("a", "v2"));
        await Refresh.RefreshAsync(_sourceId);
        _time.Advance(TimeSpan.FromDays(31));
        _type.Returns(Ev("a", "v3"));

        await Refresh.RefreshAsync(_sourceId);

        using var db = Db();
        Assert.That(db.EventRevisions.Select(r => r.Title).ToList(), Is.EqualTo(new[] { "v2" }));
    }

    [Test]
    public async Task An_event_missing_from_three_successful_refreshes_is_gone_and_comes_back_when_returned()
    {
        _type.Returns(Ev("a", "A"), Ev("b", "B"));
        await Refresh.RefreshAsync(_sourceId);
        _type.Returns(Ev("a", "A"));

        await Refresh.RefreshAsync(_sourceId);
        await Refresh.RefreshAsync(_sourceId);
        Assert.That(Event("b").Status, Is.Not.EqualTo(EventStatus.Gone));
        var third = await Refresh.RefreshAsync(_sourceId);

        Assert.Multiple(() =>
        {
            Assert.That(third!.Gone, Is.EqualTo(1));
            Assert.That(Event("b").Status, Is.EqualTo(EventStatus.Gone));
            Assert.That(Event("b").MissedRefreshes, Is.EqualTo(3));
        });

        _type.Returns(Ev("a", "A"), Ev("b", "B"));
        await Refresh.RefreshAsync(_sourceId);
        Assert.Multiple(() =>
        {
            Assert.That(Event("b").Status, Is.EqualTo(EventStatus.Unknown));
            Assert.That(Event("b").MissedRefreshes, Is.Zero);
        });
    }

    [Test]
    public async Task A_failed_fetch_records_the_error_and_never_marks_events_gone()
    {
        _type.Returns(Ev("a", "A"));
        await Refresh.RefreshAsync(_sourceId);
        var success = _time.GetUtcNow().UtcDateTime;
        _type.Throws("https://example.org returned HTTP 503");

        for (var i = 0; i < 4; i++)
        {
            _time.Advance(TimeSpan.FromHours(1));
            var result = await Refresh.RefreshAsync(_sourceId);
            Assert.That(result!.Success, Is.False);
        }

        using var db = Db();
        var source = db.Sources.Single();
        Assert.Multiple(() =>
        {
            Assert.That(source.LastError, Is.EqualTo("https://example.org returned HTTP 503"));
            Assert.That(source.LastSuccess, Is.EqualTo(success));
            Assert.That(source.LastRefresh, Is.EqualTo(_time.GetUtcNow().UtcDateTime));
            Assert.That(Event("a").Status, Is.Not.EqualTo(EventStatus.Gone));
            Assert.That(Event("a").MissedRefreshes, Is.Zero);
        });
    }

    [Test]
    public async Task Secrets_are_removed_from_the_error()
    {
        _type.Throws("https://www.thesportsdb.com/api/v1/json/premiumKey42/lookupleague.php returned HTTP 500");

        await Refresh.RefreshAsync(_sourceId);

        using var db = Db();
        Assert.That(db.Sources.Single().LastError, Does.Not.Contain("premiumKey42"));
    }

    [Test]
    public async Task Zero_items_after_a_non_empty_refresh_is_a_failure()
    {
        _type.Returns(Ev("a", "A"));
        await Refresh.RefreshAsync(_sourceId);
        _type.Returns();

        for (var i = 0; i < 3; i++)
        {
            var result = await Refresh.RefreshAsync(_sourceId);
            Assert.That(result!.Success, Is.False);
        }

        using var db = Db();
        Assert.Multiple(() =>
        {
            Assert.That(db.Sources.Single().LastError, Does.Contain("No events"));
            Assert.That(Event("a").MissedRefreshes, Is.Zero);
        });
    }

    [Test]
    public async Task Zero_items_is_a_success_when_there_were_none_before()
    {
        _type.Returns();

        var result = await Refresh.RefreshAsync(_sourceId);

        Assert.That(result!.Success, Is.True);
    }

    [Test]
    public async Task Clearing_the_error_on_the_next_success()
    {
        _type.Throws("boom");
        await Refresh.RefreshAsync(_sourceId);
        _type.Returns(Ev("a", "A"));

        await Refresh.RefreshAsync(_sourceId);

        using var db = Db();
        Assert.That(db.Sources.Single().LastError, Is.Null);
    }

    [Test]
    public async Task Only_if_due_skips_sources_refreshed_within_their_interval()
    {
        _type.Returns(Ev("a", "A"));
        await Refresh.RefreshAsync(_sourceId);
        _time.Advance(TimeSpan.FromHours(11));

        Assert.That(await Refresh.RefreshAsync(_sourceId, onlyIfDue: true), Is.Null);

        _time.Advance(TimeSpan.FromHours(1));
        Assert.That(await Refresh.RefreshAsync(_sourceId, onlyIfDue: true), Is.Not.Null);
    }

    [Test]
    public async Task Refreshes_of_one_source_do_not_overlap()
    {
        _type.Returns(Ev("a", "A"));
        _type.Delay = TimeSpan.FromMilliseconds(50);

        await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Refresh.RefreshAsync(_sourceId)));

        Assert.That(_type.MaxConcurrent, Is.EqualTo(1));
    }

    private Event Event(string key)
    {
        using var db = Db();
        return db.Events.Single(e => e.Key == key);
    }

    private GenerralDbContext Db() => _services.CreateScope().ServiceProvider.GetRequiredService<GenerralDbContext>();

    private static ExtractedEvent Ev(string key, string title) => new() { Key = key, Title = title, Fields = new JsonObject { ["key"] = key, ["title"] = title } };

    private sealed class FakeSourceType : ISourceType
    {
        public const string TypeId = "fake";

        private Func<SourceFetchResult> _result = () => throw new InvalidOperationException();
        private int _running;

        public string Id => TypeId;

        public string Name => "Fake";

        public IReadOnlyList<SourceField> Fields => [];

        public TimeSpan Delay { get; set; }

        public int MaxConcurrent { get; private set; }

        public void Returns(params ExtractedEvent[] events) => _result = () => new SourceFetchResult
        {
            Events = events,
            ItemErrors = [],
            Responses = [],
            Meta = [],
            Warnings = [],
        };

        public void Throws(string message) => _result = () => throw new SourceFetchException(message);

        public IReadOnlyList<SourceValidationFailure> Validate(JsonObject settings) => [];

        public async Task<SourceFetchResult> FetchAsync(JsonObject settings, SourceFetchOptions? options = null, CancellationToken cancellationToken = default)
        {
            var running = Interlocked.Increment(ref _running);
            MaxConcurrent = Math.Max(MaxConcurrent, running);
            try
            {
                await Task.Delay(Delay, cancellationToken);
                return _result();
            }
            finally
            {
                Interlocked.Decrement(ref _running);
            }
        }
    }
}
