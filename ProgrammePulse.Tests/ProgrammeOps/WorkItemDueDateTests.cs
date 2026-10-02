using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Tests.ProgrammeOps;

/// <summary>
/// The one definition of "overdue" (design and PMO data review, 30 Sep 2026,
/// B3: the Programme Overview said 14 and the Evidence Check said 9 for the
/// same data). Whole days, open items only, and never an item whose status
/// can't be read.
/// </summary>
public class WorkItemDueDateTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);

    private static WorkItem Item(WorkItemLifecycleStage stage, DateTime? due) => new()
    {
        WorkItemKey = Guid.NewGuid(),
        WorkstreamKey = Guid.NewGuid(),
        Title = "Item",
        Stage = stage,
        DueDateUtc = due,
        CreatedAtUtc = Now,
        UpdatedAtUtc = Now
    };

    [Fact]
    public void Open_work_due_yesterday_is_one_day_overdue()
    {
        var item = Item(WorkItemLifecycleStage.InProgress, new DateTime(2026, 9, 29));

        Assert.True(item.IsOverdueOn(Now));
        Assert.Equal(1, item.DaysPastDue(Now));
    }

    [Fact]
    public void Work_due_earlier_today_is_not_late_until_tomorrow()
    {
        var item = Item(WorkItemLifecycleStage.InProgress, new DateTime(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc));

        Assert.False(item.IsOverdueOn(Now));
        Assert.Equal(0, item.DaysPastDue(Now));
    }

    [Theory]
    [InlineData(WorkItemLifecycleStage.Done)]
    [InlineData(WorkItemLifecycleStage.Cancelled)]
    public void Finished_work_is_never_overdue(WorkItemLifecycleStage stage) =>
        Assert.False(Item(stage, new DateTime(2026, 9, 1)).IsOverdueOn(Now));

    [Fact]
    public void Work_whose_status_cant_be_read_is_counted_separately_not_as_overdue()
    {
        var item = Item(WorkItemLifecycleStage.Unmapped, new DateTime(2026, 9, 27));

        Assert.False(item.IsOverdueOn(Now));
        Assert.True(item.IsPastDueWithUnreadableStatus(Now));
    }

    [Fact]
    public void Work_with_no_due_date_can_never_be_overdue() =>
        Assert.False(Item(WorkItemLifecycleStage.Blocked, null).IsOverdueOn(Now));
}
