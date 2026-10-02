using ProgrammePulse.Models.Erp;

namespace ProgrammePulse.Services.Transformation;

/// <summary>
/// Maps domain enums to the label/CSS-class pairs the views render. Keeping this
/// here (not in Razor) is what lets the views stay free of branching logic.
/// </summary>
internal static class StatusPresentation
{
    public static (string Label, string CssClass) For(NormalisedStatus status) => status switch
    {
        NormalisedStatus.New => ("New", "chip-status--new"),
        NormalisedStatus.InProgress => ("In Progress", "chip-status--in-progress"),
        NormalisedStatus.Complete => ("Complete", "chip-status--complete"),
        NormalisedStatus.Exception => ("Exception", "chip-status--exception"),
        _ => (status.ToString(), "chip-status--exception")
    };

    public static (string Label, string CssClass) For(ProcessingStage stage) => stage switch
    {
        ProcessingStage.New => ("New", "chip-stage--new"),
        ProcessingStage.Validating => ("Validating", "chip-stage--validating"),
        ProcessingStage.Transformed => ("Transformed", "chip-stage--transformed"),
        ProcessingStage.Exception => ("Exception", "chip-stage--exception"),
        _ => (stage.ToString(), "chip-stage--exception")
    };
}
