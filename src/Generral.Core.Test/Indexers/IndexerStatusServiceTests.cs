using System.Globalization;
using Generral.Core.HealthCheck;
using Generral.Core.Indexers;
using Generral.Core.Indexers.Torznab;
using Generral.Core.Lifecycle;
using Generral.Core.Test.Sources;

namespace Generral.Core.Test.Indexers;

public class IndexerStatusServiceTests
{
    private static readonly DateTime Start = new(2026, 11, 23, 8, 0, 0, DateTimeKind.Utc);
    private static readonly Indexer Tracker = new() { Id = 3, Name = "LimeTorrents", Implementation = IndexerImplementations.Torznab };

    private FakeTime _time = null!;
    private ProviderStatusTracker _tracker = null!;
    private IndexerStatusService _status = null!;

    [SetUp]
    public void SetUp()
    {
        _time = new FakeTime(Start.AddHours(1));
        _tracker = new ProviderStatusTracker(_time);
        _status = new IndexerStatusService(_tracker, new StartedAt(Start), _time);
    }

    [Test]
    public void Failures_escalate_the_back_off_and_successes_step_it_down()
    {
        var periods = new List<TimeSpan>();
        for (var i = 0; i < 4; i++)
        {
            periods.Add(_status.RecordFailure(Tracker, "HTTP 500").DisabledTill!.Value - _time.GetUtcNow().UtcDateTime);
        }

        var disabled = _status.IsDisabled(Tracker.Id);
        _time.Advance(TimeSpan.FromMinutes(31));
        var expired = _status.IsDisabled(Tracker.Id);
        _status.RecordSuccess(Tracker);
        var afterSuccess = _status.RecordFailure(Tracker, "HTTP 500");

        Assert.Multiple(() =>
        {
            Assert.That(periods, Is.EqualTo(new[] { TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(30) }));
            Assert.That(disabled, Is.True);
            Assert.That(expired, Is.False);
            Assert.That(afterSuccess.EscalationLevel, Is.EqualTo(4), "one success steps down from 4 to 3, the failure escalates again");
            Assert.That(_tracker.Failures(ProviderKind.Indexer).Single().Name, Is.EqualTo("LimeTorrents"));
        });
    }

    [Test]
    public void Startup_grace_period_caps_the_back_off_unless_the_indexer_asks_for_longer()
    {
        _time = new FakeTime(Start.AddMinutes(1));
        _status = new IndexerStatusService(_tracker, new StartedAt(Start), _time);

        var first = _status.RecordFailure(Tracker, "HTTP 500");
        var second = _status.RecordFailure(Tracker, "HTTP 500");
        var limited = _status.RecordFailure(Tracker, "Request limit", TimeSpan.FromHours(2));
        var connection = new IndexerStatusService(_tracker, new StartedAt(Start.AddDays(-1)), _time);
        connection.RecordFailure(Tracker, "Connection refused");
        var notEscalated = connection.RecordFailure(Tracker, "Connection refused", escalate: false);

        Assert.Multiple(() =>
        {
            Assert.That(first.EscalationLevel, Is.EqualTo(1));
            Assert.That(second.EscalationLevel, Is.EqualTo(1), "no escalation during the grace period");
            Assert.That(limited.DisabledTill, Is.EqualTo(_time.GetUtcNow().UtcDateTime.AddHours(2)));
            Assert.That(notEscalated.EscalationLevel, Is.EqualTo(1));
        });
    }

    [Test]
    public void Prowlarr_disabled_till_and_429_are_request_limits()
    {
        var till = new DateTime(2026, 11, 23, 14, 30, 0, DateTimeKind.Local);
        var xml = $"""<error code="429" description="Indexer is disabled till {till.ToString(CultureInfo.CurrentCulture)} due to recent failures." />""";

        var e = Assert.Throws<TorznabException>(() => TorznabParser.ParseReleases(xml, new Uri("http://prowlarr.lan/3/api")))!;

        Assert.Multiple(() =>
        {
            Assert.That(e.RequestLimit, Is.True);
            Assert.That(e.DisabledTill, Is.EqualTo(till.ToUniversalTime()));
        });
    }

    private sealed class StartedAt(DateTime startTime) : IRuntimeInfo
    {
        public DateTime StartTime => startTime;

        public bool IsContainerized => false;
    }
}
