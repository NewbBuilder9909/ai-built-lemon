using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Integrations.ClickUp;

namespace ProgrammePulse.Tests.Integrations;

public class ClickUpStatusMapperTests
{
    private readonly ClickUpStatusMapper _sut = new();

    [Theory]
    [InlineData("to do", WorkItemLifecycleStage.Backlog)]
    [InlineData("Open", WorkItemLifecycleStage.Backlog)]
    [InlineData("in progress", WorkItemLifecycleStage.InProgress)]
    [InlineData("Blocked", WorkItemLifecycleStage.Blocked)]
    [InlineData("in review", WorkItemLifecycleStage.InReview)]
    [InlineData("done", WorkItemLifecycleStage.Done)]
    [InlineData("cancelled", WorkItemLifecycleStage.Cancelled)]
    public void Map_recognised_status_returns_expected_stage(string raw, WorkItemLifecycleStage expected)
    {
        Assert.Equal(expected, _sut.Map(raw));
    }

    [Theory]
    [InlineData("some custom status")]
    [InlineData("")]
    [InlineData(null)]
    public void Map_unrecognised_or_missing_status_returns_unmapped(string? raw)
    {
        Assert.Equal(WorkItemLifecycleStage.Unmapped, _sut.Map(raw));
    }
}
