using ProgrammePulse.Models.Erp;
using ProgrammePulse.Services.Transformation;

namespace ProgrammePulse.Tests.Transformation;

public class ErpRecordValidatorTests
{
    private readonly ErpRecordValidator _sut = new();

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
    public void Validate_complete_record_has_no_messages()
    {
        var result = _sut.Validate(CompleteValidRecord());

        Assert.True(result.IsValid);
        Assert.False(result.HasWarnings);
        Assert.Empty(result.Messages);
    }

    [Fact]
    public void Validate_missing_customer_name_adds_warning_but_stays_valid()
    {
        var record = CompleteValidRecord() with { CustomerName = null };

        var result = _sut.Validate(record);

        Assert.True(result.IsValid);
        Assert.True(result.HasWarnings);
    }

    [Fact]
    public void Validate_missing_status_adds_warning()
    {
        var record = CompleteValidRecord() with { RawStatus = null };

        var result = _sut.Validate(record);

        Assert.True(result.IsValid);
        Assert.Contains(result.Messages, m => m.Message.Contains("status is missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_unrecognised_status_adds_warning_naming_the_value()
    {
        var record = CompleteValidRecord() with { RawStatus = "CANCELLED" };

        var result = _sut.Validate(record);

        Assert.True(result.IsValid);
        Assert.Contains(result.Messages, m => m.Message.Contains("CANCELLED"));
    }

    [Fact]
    public void Validate_unparseable_amount_is_an_error()
    {
        var record = CompleteValidRecord() with { RawAmount = "not-a-number" };

        var result = _sut.Validate(record);

        Assert.False(result.IsValid);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Validate_missing_amount_is_only_a_warning()
    {
        var record = CompleteValidRecord() with { RawAmount = null };

        var result = _sut.Validate(record);

        Assert.True(result.IsValid);
        Assert.True(result.HasWarnings);
    }

    [Fact]
    public void Validate_unparseable_date_is_an_error()
    {
        var record = CompleteValidRecord() with { RawSourceDate = "not-a-date" };

        var result = _sut.Validate(record);

        Assert.False(result.IsValid);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void Validate_missing_date_is_only_a_warning()
    {
        var record = CompleteValidRecord() with { RawSourceDate = null };

        var result = _sut.Validate(record);

        Assert.True(result.IsValid);
        Assert.True(result.HasWarnings);
    }
}
