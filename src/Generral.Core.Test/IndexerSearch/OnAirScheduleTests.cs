using System.Text.Json.Nodes;
using Generral.Core.DecisionEngine;
using Generral.Core.IndexerSearch;

namespace Generral.Core.Test.IndexerSearch;

public class OnAirScheduleTests
{
    private static readonly DateTime Air = new(2026, 1, 24, 3, 0, 0, DateTimeKind.Utc);

    [Test]
    public void Default_schedule_is_hours_then_daily_for_a_week()
    {
        Assert.That(OnAirSchedule.Default.Select(o => o.TotalHours), Is.EqualTo(new double[] { 1, 3, 6, 12, 24, 48, 72, 96, 120, 144, 168, 192, 216 }));
    }

    [TestCase(0.5, null, null)]
    [TestCase(1, null, 1)]
    [TestCase(2.5, null, 1)]
    [TestCase(2.5, 1.2, null)]
    [TestCase(3.1, 1.2, 3)]
    [TestCase(50, 30, 48)]
    [TestCase(216.5, 200, 216)]
    [TestCase(216 + 25, null, null)]
    public void Due_slot_is_the_latest_passed_one_not_yet_searched(double hoursSinceAir, double? searchedHoursAfterAir, double? expectedSlotHours)
    {
        DateTime? searched = searchedHoursAfterAir is { } hoursAfter ? Air.AddHours(hoursAfter) : null;

        var slot = OnAirSchedule.DueSlot(Air, searched, OnAirSchedule.Default, Air.AddHours(hoursSinceAir));

        Assert.That(slot, Is.EqualTo(expectedSlotHours is { } hours ? Air.AddHours(hours) : null));
    }

    [Test]
    public void Search_schedule_setting_overrides_or_disables()
    {
        var custom = SearchTerms.From(new JsonObject(), new JsonObject { ["searchSchedule"] = new JsonArray("2d", "30m") });
        var off = SearchTerms.From(new JsonObject { ["searchSchedule"] = new JsonArray() });

        Assert.Multiple(() =>
        {
            Assert.That(custom.Schedule, Is.EqualTo(new[] { TimeSpan.FromMinutes(30), TimeSpan.FromDays(2) }));
            Assert.That(OnAirSchedule.DueSlot(Air, null, off.Schedule, Air.AddHours(5)), Is.Null);
        });
    }
}
