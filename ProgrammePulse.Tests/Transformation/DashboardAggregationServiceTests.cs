using ProgrammePulse.Models.Erp;
using ProgrammePulse.Services.Transformation;

namespace ProgrammePulse.Tests.Transformation;

public class DashboardAggregationServiceTests
{
    private readonly DashboardAggregationService _sut = new();

    private static ErpPipelineItem CreateItem(
        string erpId,
        ProcessingStage stage,
        NormalisedStatus status = NormalisedStatus.New,
        decimal? amount = null,
        string customerName = "Test Customer",
        string workType = "Consulting",
        DateTime? sourceDateUtc = null,
        bool hasWarning = false,
        bool hasError = false)
    {
        var validation = new ValidationResult();
        if (hasError)
        {
            validation.AddError("Simulated error for test purposes.");
        }
        if (hasWarning)
        {
            validation.AddWarning("Simulated warning for test purposes.");
        }

        return new ErpPipelineItem
        {
            Raw = new RawErpRecord { SourceErpId = erpId },
            Validation = validation,
            Stage = stage,
            Transformed = new TransformedErpRecord
            {
                SourceErpId = erpId,
                CustomerName = customerName,
                WorkType = workType,
                Status = status,
                Amount = amount,
                SourceDateUtc = sourceDateUtc
            }
        };
    }

    [Fact]
    public void BuildDashboard_computes_totals_and_completion_percentage()
    {
        var items = new[]
        {
            CreateItem("ERP-1", ProcessingStage.New),
            CreateItem("ERP-2", ProcessingStage.New),
            CreateItem("ERP-3", ProcessingStage.Validating, hasWarning: true),
            CreateItem("ERP-4", ProcessingStage.Transformed),
            CreateItem("ERP-5", ProcessingStage.Transformed),
            CreateItem("ERP-6", ProcessingStage.Transformed),
            CreateItem("ERP-7", ProcessingStage.Exception, hasError: true)
        };

        var dashboard = _sut.BuildDashboard(items);

        Assert.Equal("7", dashboard.KpiCards[0].Value);
        Assert.Equal("3", dashboard.KpiCards[1].Value);
        Assert.Equal("4", dashboard.KpiCards[2].Value);
        Assert.Equal("43%", dashboard.KpiCards[3].Value);
    }

    [Fact]
    public void BuildDashboard_status_summary_counts_each_stage()
    {
        var items = new[]
        {
            CreateItem("ERP-1", ProcessingStage.New),
            CreateItem("ERP-2", ProcessingStage.Transformed),
            CreateItem("ERP-3", ProcessingStage.Exception, hasError: true)
        };

        var dashboard = _sut.BuildDashboard(items);

        var newCount = dashboard.StatusSummary.Items.Single(i => i.Label == "New").Count;
        var transformedCount = dashboard.StatusSummary.Items.Single(i => i.Label == "Transformed").Count;
        var exceptionCount = dashboard.StatusSummary.Items.Single(i => i.Label == "Exception").Count;

        Assert.Equal(1, newCount);
        Assert.Equal(1, transformedCount);
        Assert.Equal(1, exceptionCount);
    }

    [Fact]
    public void BuildDashboard_on_empty_input_does_not_throw_and_reports_zero()
    {
        var dashboard = _sut.BuildDashboard([]);

        Assert.Equal("0", dashboard.KpiCards[0].Value);
        Assert.Equal("0%", dashboard.KpiCards[3].Value);
    }

    [Fact]
    public void BuildRecordsList_search_matches_customer_name()
    {
        var items = new[]
        {
            CreateItem("ERP-1", ProcessingStage.Transformed, customerName: "Aurora Retail Group"),
            CreateItem("ERP-2", ProcessingStage.Transformed, customerName: "Cobalt Manufacturing")
        };

        var result = _sut.BuildRecordsList(items, "aurora", null, null, null, null);

        Assert.Single(result.Rows);
        Assert.Equal("ERP-1", result.Rows[0].ErpId);
    }

    [Fact]
    public void BuildRecordsList_status_filter_only_returns_matching_status()
    {
        var items = new[]
        {
            CreateItem("ERP-1", ProcessingStage.Transformed, status: NormalisedStatus.Complete),
            CreateItem("ERP-2", ProcessingStage.Transformed, status: NormalisedStatus.InProgress)
        };

        var result = _sut.BuildRecordsList(items, null, "Complete", null, null, null);

        Assert.Single(result.Rows);
        Assert.Equal("ERP-1", result.Rows[0].ErpId);
    }

    [Fact]
    public void BuildRecordsList_validation_filter_error_only_returns_error_records()
    {
        var items = new[]
        {
            CreateItem("ERP-1", ProcessingStage.Exception, hasError: true),
            CreateItem("ERP-2", ProcessingStage.Transformed)
        };

        var result = _sut.BuildRecordsList(items, null, null, "error", null, null);

        Assert.Single(result.Rows);
        Assert.Equal("ERP-1", result.Rows[0].ErpId);
    }

    [Fact]
    public void BuildRecordsList_sorts_by_amount_ascending_by_default_direction()
    {
        var items = new[]
        {
            CreateItem("ERP-1", ProcessingStage.Transformed, amount: 500m),
            CreateItem("ERP-2", ProcessingStage.Transformed, amount: 100m)
        };

        var result = _sut.BuildRecordsList(items, null, null, null, "amount", "asc");

        Assert.Equal(["ERP-2", "ERP-1"], result.Rows.Select(r => r.ErpId));
    }

    [Fact]
    public void BuildRecordsList_sorts_descending_when_requested()
    {
        var items = new[]
        {
            CreateItem("ERP-1", ProcessingStage.Transformed, amount: 500m),
            CreateItem("ERP-2", ProcessingStage.Transformed, amount: 100m)
        };

        var result = _sut.BuildRecordsList(items, null, null, null, "amount", "desc");

        Assert.Equal(["ERP-1", "ERP-2"], result.Rows.Select(r => r.ErpId));
    }

    [Fact]
    public void BuildDetail_returns_matching_record_case_insensitively()
    {
        var items = new[] { CreateItem("ERP-42", ProcessingStage.Transformed) };

        var detail = _sut.BuildDetail(items, "erp-42");

        Assert.NotNull(detail);
        Assert.Equal("ERP-42", detail!.ErpId);
    }

    [Fact]
    public void BuildDetail_returns_null_when_no_record_matches()
    {
        var items = new[] { CreateItem("ERP-42", ProcessingStage.Transformed) };

        var detail = _sut.BuildDetail(items, "ERP-999");

        Assert.Null(detail);
    }
}
