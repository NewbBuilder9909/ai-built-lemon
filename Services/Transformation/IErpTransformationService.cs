using ProgrammePulse.Models.Erp;

namespace ProgrammePulse.Services.Transformation;

public interface IErpTransformationService
{
    ErpPipelineItem Process(RawErpRecord raw);

    IReadOnlyList<ErpPipelineItem> ProcessAll(IEnumerable<RawErpRecord> rawRecords);
}
