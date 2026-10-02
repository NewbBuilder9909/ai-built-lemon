using ProgrammePulse.Models.Erp;

namespace ProgrammePulse.Services.Transformation;

public interface IErpRecordValidator
{
    ValidationResult Validate(RawErpRecord record);
}
