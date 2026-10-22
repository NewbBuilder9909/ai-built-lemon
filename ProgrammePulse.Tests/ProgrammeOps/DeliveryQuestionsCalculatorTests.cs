using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.ProgrammeOps;

namespace ProgrammePulse.Tests.ProgrammeOps;

public class DeliveryQuestionsCalculatorTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Alex = Guid.NewGuid();
    private static readonly Guid Sam = Guid.NewGuid();

    private sealed class Data
    {
        public Project Project { get; } = new() { ProjectKey = Guid.NewGuid(), ProgrammeKey = Guid.NewGuid(), Name = "Alpha", CreatedAtUtc = Now, UpdatedAtUtc = Now };
        public Workstream Stream { get; }
        public List<WorkItem> Items { get; } = [];
        public List<TimeEntry> Time { get; } = [];
        public List<ChangeRequest> Changes { get; } = [];
        public List<WorkstreamBaseline> Baselines { get; } = [];

        public Data() => Stream = new Workstream { WorkstreamKey = Guid.NewGuid(), ProjectKey = Project.ProjectKey, Name = "W", CreatedAtUtc = Now, UpdatedAtUtc = Now };

        public WorkItem Item(string id, WorkItemLifecycleStage stage = WorkItemLifecycleStage.InProgress, decimal? estimate = 8,
            int updatedDaysAgo = 1, int createdDaysAgo = 60, DateTime? due = null)
        {
            var item = new WorkItem
            {
                WorkItemKey = Guid.NewGuid(), WorkstreamKey = Stream.WorkstreamKey, Title = "Item " + id, Stage = stage,
                EstimatedHours = estimate, DueDateUtc = due ?? Now.AddDays(14), ExternalSource = "FileImport", ExternalId = id,
                CreatedAtUtc = Now.AddDays(-createdDaysAgo), UpdatedAtUtc = Now.AddDays(-updatedDaysAgo)
            };
            Items.Add(item);
            return item;
        }

        public void Log(WorkItem? item, decimal hours, Guid? staff = null) => Time.Add(new TimeEntry
        {
            TimeEntryKey = Guid.NewGuid(), WorkItemKey = item?.WorkItemKey, StaffKey = staff ?? Alex, DurationHours = hours,
            CreatedAtUtc = Now, UpdatedAtUtc = Now
        });

        public IReadOnlyList<ProjectDeliveryQuestions> Run() => DeliveryQuestionsCalculator.Calculate(
            new DeliveryQuestionsInput([Project], [Stream], Items, Time, Changes, Baselines), Now);
    }

    [Fact]
    public void A_healthy_project_is_not_flagged()
    {
        var d = new Data();
        var a = d.Item("1");
        d.Log(a, 4);
        Assert.Empty(d.Run());
    }

    [Fact]
    public void Open_work_nobody_has_touched_triggers_a_stalled_question_with_numerator_and_records()
    {
        var d = new Data();
        d.Item("1", updatedDaysAgo: 30);
        d.Item("2", updatedDaysAgo: 40);
        var result = Assert.Single(d.Run());
        Assert.Contains(DeliveryTrigger.Stalled, result.Triggers);
        var q = Assert.Single(result.Questions, x => x.Key == "stalled");
        Assert.Equal(2, q.Numerator);
        Assert.Equal(2, q.Denominator);
        Assert.Equal(2, q.Records.Count);
        Assert.Equal(QuestionWeight.High, q.Weight);
    }

    [Fact]
    public void Hours_over_baseline_trigger_and_late_items_without_change_requests_are_questioned()
    {
        var d = new Data();
        d.Baselines.Add(new WorkstreamBaseline { WorkstreamKey = d.Stream.WorkstreamKey, BaselineHours = 10, LockedAtUtc = Now.AddDays(-30) });
        var old = d.Item("1");
        d.Item("2", createdDaysAgo: 5);
        d.Item("3", createdDaysAgo: 3);
        d.Log(old, 12);
        var result = Assert.Single(d.Run());
        Assert.Contains(DeliveryTrigger.OverBaselineHours, result.Triggers);
        var q = Assert.Single(result.Questions, x => x.Key == "late-scope");
        Assert.Equal(2, q.Numerator);
        Assert.Equal(3, q.Denominator);
    }

    [Fact]
    public void Late_items_covered_by_approved_change_requests_are_not_questioned()
    {
        var d = new Data();
        d.Baselines.Add(new WorkstreamBaseline { WorkstreamKey = d.Stream.WorkstreamKey, BaselineHours = 1, LockedAtUtc = Now.AddDays(-30) });
        var old = d.Item("1");
        d.Item("2", createdDaysAgo: 5);
        d.Changes.Add(new ChangeRequest { ChangeRequestKey = Guid.NewGuid(), ProjectKey = d.Project.ProjectKey, Title = "CR", Status = ChangeRequestStatus.Approved, CreatedAtUtc = Now, UpdatedAtUtc = Now });
        d.Log(old, 5);
        var result = Assert.Single(d.Run());
        Assert.DoesNotContain(result.Questions, x => x.Key == "late-scope");
    }

    [Fact]
    public void Items_far_over_estimate_and_items_with_no_estimate_are_asked_about_separately()
    {
        var d = new Data();
        var over = d.Item("1", estimate: 4);
        var fine = d.Item("2", estimate: 2);
        var none = d.Item("3", estimate: null);
        d.Log(over, 8);
        d.Log(fine, 2);
        d.Log(none, 3);
        d.Item("4", estimate: null, updatedDaysAgo: 40);
        var result = Assert.Single(d.Run());
        Assert.Contains(DeliveryTrigger.HoursExceedEstimateWithoutCompletion, result.Triggers);
        var overQ = Assert.Single(result.Questions, x => x.Key == "over-estimate");
        Assert.Equal(1, overQ.Numerator);
        var unQ = Assert.Single(result.Questions, x => x.Key == "unestimated-work");
        Assert.Equal(1, unQ.Numerator);
        Assert.Equal(3, unQ.Denominator);
    }

    [Fact]
    public void Past_due_open_work_triggers_a_flag()
    {
        var d = new Data();
        d.Item("1", due: Now.AddDays(-3));
        d.Item("2", due: Now.AddDays(10));
        var result = Assert.Single(d.Run());
        Assert.Contains(DeliveryTrigger.OpenWorkPastDue, result.Triggers);
    }

    [Fact]
    public void Concentration_question_never_names_a_person()
    {
        var d = new Data();
        var a = d.Item("1", updatedDaysAgo: 40, estimate: 1);
        d.Log(a, 9, Alex);
        d.Log(a, 1, Sam);
        var q = Assert.Single(Assert.Single(d.Run()).Questions, x => x.Key == "concentration");
        Assert.DoesNotContain(Alex.ToString(), q.Question);
        Assert.Empty(q.Records);
    }

    [Fact]
    public void At_most_five_questions_are_returned_high_weight_first()
    {
        var d = new Data();
        d.Baselines.Add(new WorkstreamBaseline { WorkstreamKey = d.Stream.WorkstreamKey, BaselineHours = 1, LockedAtUtc = Now.AddDays(-30) });
        var a = d.Item("1", estimate: 1, updatedDaysAgo: 40);
        d.Item("2", estimate: null, createdDaysAgo: 2, updatedDaysAgo: 40);
        d.Log(a, 5);
        d.Log(d.Items[1], 5);
        var qs = Assert.Single(d.Run()).Questions;
        Assert.InRange(qs.Count, 1, DeliveryQuestionsCalculator.MaxQuestionsPerProject);
        Assert.Equal(qs.OrderByDescending(q => q.Weight).Select(q => q.Weight), qs.Select(q => q.Weight));
    }

    [Fact]
    public void Cancelled_items_are_ignored()
    {
        var d = new Data();
        d.Item("1", WorkItemLifecycleStage.Cancelled, updatedDaysAgo: 90);
        Assert.Empty(d.Run());
    }
}
