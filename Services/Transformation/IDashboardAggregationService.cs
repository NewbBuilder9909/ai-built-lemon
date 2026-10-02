using ProgrammePulse.Models.Erp;
using ProgrammePulse.Models.ViewModels;

namespace ProgrammePulse.Services.Transformation;

public interface IDashboardAggregationService
{
    OperationsHubDashboardViewModel BuildDashboard(IReadOnlyList<ErpPipelineItem> items);

    ErpRecordsListViewModel BuildRecordsList(
        IReadOnlyList<ErpPipelineItem> items,
        string? search,
        string? statusFilter,
        string? validationFilter,
        string? sortColumn,
        string? sortDirection);

    ErpRecordDetailViewModel? BuildDetail(IReadOnlyList<ErpPipelineItem> items, string erpId);
}
