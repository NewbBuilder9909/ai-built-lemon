using ProgrammePulse.Models.Programme;

namespace ProgrammePulse.Services.Integrations.ClickUp;

public interface IClickUpStatusMapper
{
    WorkItemLifecycleStage Map(string? rawStatus);
}
