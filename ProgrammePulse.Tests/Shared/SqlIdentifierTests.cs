using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.Shared;

/// <summary>Only plain identifiers reach SQL text, bracket-quoted (Aikido: SQL built by string concatenation).</summary>
public sealed class SqlIdentifierTests
{
    [Theory]
    [InlineData("ProgrammeOps_WorkItem", "[ProgrammeOps_WorkItem]")]
    [InlineData("workItemKey", "[workItemKey]")]
    [InlineData("_x1", "[_x1]")]
    public void A_plain_identifier_is_bracket_quoted(string name, string expected)
    {
        Assert.Equal(expected, SqlIdentifier.Quote(name));
    }

    [Theory]
    [InlineData("x; DROP TABLE y")]
    [InlineData("x]")]
    [InlineData("[x]")]
    [InlineData("x--")]
    [InlineData("1x")]
    [InlineData("")]
    [InlineData("a b")]
    public void Anything_else_is_refused(string name)
    {
        Assert.Throws<ArgumentException>(() => SqlIdentifier.Quote(name));
    }
}
