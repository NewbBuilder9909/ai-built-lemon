using ProgrammePulse.Models.Erp;
using ProgrammePulse.Services.Transformation;

namespace ProgrammePulse.Tests.Transformation;

public class StatusMapperTests
{
    private readonly StatusMapper _sut = new();

    [Theory]
    [InlineData("OPEN", NormalisedStatus.New)]
    [InlineData("open", NormalisedStatus.New)]
    [InlineData("  OPEN  ", NormalisedStatus.New)]
    [InlineData("WIP", NormalisedStatus.InProgress)]
    [InlineData("wip", NormalisedStatus.InProgress)]
    [InlineData("DONE", NormalisedStatus.Complete)]
    [InlineData("done", NormalisedStatus.Complete)]
    public void Map_recognised_status_returns_expected_value(string raw, NormalisedStatus expected)
    {
        Assert.Equal(expected, _sut.Map(raw));
    }

    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("unknown")]
    [InlineData("")]
    [InlineData(null)]
    public void Map_unrecognised_or_missing_status_returns_exception(string? raw)
    {
        Assert.Equal(NormalisedStatus.Exception, _sut.Map(raw));
    }
}
