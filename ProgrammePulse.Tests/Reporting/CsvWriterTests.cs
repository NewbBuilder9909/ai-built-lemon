using ProgrammePulse.Services.Reporting;

namespace ProgrammePulse.Tests.Reporting;

public class CsvWriterTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://evil.example\",\"Click\")", "\"'=HYPERLINK(\"\"http://evil.example\"\",\"\"Click\"\")\"")]
    [InlineData("+cmd|' /C calc'!A0", "'+cmd|' /C calc'!A0")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("  =1+1", "'  =1+1")]
    public void Text_a_spreadsheet_would_execute_is_neutralised(string field, string expected)
    {
        Assert.Equal(expected + "\r\n", CsvWriter.WriteRow(field));
    }

    [Theory]
    [InlineData("-12.5")]
    [InlineData("-1,200.00")]
    [InlineData("42")]
    [InlineData("Checkout flow")]
    public void Numbers_including_negative_variances_and_plain_text_are_unchanged(string field)
    {
        var written = CsvWriter.WriteRow(field);
        Assert.DoesNotContain("'", written);
    }
}
