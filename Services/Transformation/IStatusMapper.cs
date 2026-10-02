using ProgrammePulse.Models.Erp;

namespace ProgrammePulse.Services.Transformation;

public interface IStatusMapper
{
    NormalisedStatus Map(string? rawStatus);
}
