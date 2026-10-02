using ProgrammePulse.Models.Erp;
using ProgrammePulse.Services.Transformation;

namespace ProgrammePulse.Tests.Transformation;

public class ErpTransformationServiceTests
{
    private readonly ErpTransformationService _sut = new(new ErpRecordValidator(), new StatusMapper());

    private static RawErpRecord CompleteValidRecord() => new()
    {
        SourceErpId = "ERP-1",
        CustomerName = "Aurora Retail Group",
        WorkType = "Fulfilment",
        RawStatus = "OPEN",
        RawAmount = "1250.00",
        RawSourceDate = "2026-06-01T09:15:00-04:00"
    };

    [Fact]
    public void Process_clean_record_reaches_transformed_stage()
    {
        var result = _sut.Process(CompleteValidRecord());

        Assert.Equal(ProcessingStage.Transformed, result.Stage);
        Assert.Equal(NormalisedStatus.New, result.Transformed.Status);
        Assert.Equal(1250.00m, result.Transformed.Amount);
    }

    [Fact]
    public void Process_record_missing_status_reaches_new_stage()
    {
        var record = CompleteValidRecord() with { RawStatus = null };

        var result = _sut.Process(record);

        Assert.Equal(ProcessingStage.New, result.Stage);
    }

    [Fact]
    public void Process_record_with_only_warnings_reaches_validating_stage()
    {
        var record = CompleteValidRecord() with { CustomerName = null };

        var result = _sut.Process(record);

        Assert.Equal(ProcessingStage.Validating, result.Stage);
        Assert.Equal("Unknown Customer", result.Transformed.CustomerName);
    }

    [Fact]
    public void Process_record_with_an_error_reaches_exception_stage()
    {
        var record = CompleteValidRecord() with { RawAmount = "not-a-number" };

        var result = _sut.Process(record);

        Assert.Equal(ProcessingStage.Exception, result.Stage);
    }

    [Fact]
    public void Process_preserves_fields_that_parsed_fine_even_when_another_field_errors()
    {
        // The date is unparseable (an error) but the amount and customer name are fine —
        // the transformed record should still carry the good values instead of being blanked out.
        var record = CompleteValidRecord() with { RawSourceDate = "not-a-date" };

        var result = _sut.Process(record);

        Assert.Equal(ProcessingStage.Exception, result.Stage);
        Assert.Null(result.Transformed.SourceDateUtc);
        Assert.Equal("Aurora Retail Group", result.Transformed.CustomerName);
        Assert.Equal(1250.00m, result.Transformed.Amount);
    }

    [Fact]
    public void Process_always_preserves_the_original_source_id()
    {
        var result = _sut.Process(CompleteValidRecord());

        Assert.Equal("ERP-1", result.Raw.SourceErpId);
        Assert.Equal("ERP-1", result.Transformed.SourceErpId);
    }

    [Fact]
    public void Process_collapses_internal_whitespace_in_customer_name()
    {
        var record = CompleteValidRecord() with { CustomerName = "  Bramwell   &   Sons  " };

        var result = _sut.Process(record);

        Assert.Equal("Bramwell & Sons", result.Transformed.CustomerName);
    }

    [Fact]
    public void Process_strips_currency_symbols_and_thousands_separators_from_amount()
    {
        var record = CompleteValidRecord() with { RawAmount = "$3,400.50" };

        var result = _sut.Process(record);

        Assert.Equal(3400.50m, result.Transformed.Amount);
    }

    [Fact]
    public void Process_missing_work_type_defaults_to_unspecified()
    {
        var record = CompleteValidRecord() with { WorkType = null };

        var result = _sut.Process(record);

        Assert.Equal("Unspecified", result.Transformed.WorkType);
    }

    [Fact]
    public void Process_converts_offset_date_to_utc()
    {
        var record = CompleteValidRecord() with { RawSourceDate = "2026-06-01T09:15:00-04:00" };

        var result = _sut.Process(record);

        Assert.Equal(new DateTime(2026, 6, 1, 13, 15, 0, DateTimeKind.Utc), result.Transformed.SourceDateUtc);
    }

    [Fact]
    public void Process_converts_local_date_using_source_time_zone_to_utc()
    {
        var record = CompleteValidRecord() with
        {
            RawSourceDate = "2026-06-09T07:20:00",
            SourceTimeZoneId = "Pacific Standard Time"
        };

        var result = _sut.Process(record);

        // June falls in daylight saving time for the US Pacific zone (UTC-7).
        Assert.Equal(new DateTime(2026, 6, 9, 14, 20, 0, DateTimeKind.Utc), result.Transformed.SourceDateUtc);
    }

    [Fact]
    public void ProcessAll_processes_every_record_independently()
    {
        var records = new[] { CompleteValidRecord(), CompleteValidRecord() with { SourceErpId = "ERP-2", RawStatus = null } };

        var results = _sut.ProcessAll(records);

        Assert.Equal(2, results.Count);
        Assert.Equal(ProcessingStage.Transformed, results[0].Stage);
        Assert.Equal(ProcessingStage.New, results[1].Stage);
    }
}
