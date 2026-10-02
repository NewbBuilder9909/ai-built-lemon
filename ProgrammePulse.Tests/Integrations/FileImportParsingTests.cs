using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Integrations.FileImport;

namespace ProgrammePulse.Tests.Integrations;

public class FileImportParsingTests
{
    [Fact]
    public void Parses_quotes_escaped_quotes_embedded_commas_and_line_breaks()
    {
        var rows = DelimitedTextParser.Parse("﻿a,b,c\r\n\"x, y\",\"say \"\"hi\"\"\",\"two\nlines\"\r\n", 10);

        Assert.Equal(2, rows.Count);
        Assert.Equal(["a", "b", "c"], rows[0]);
        Assert.Equal(["x, y", "say \"hi\"", "two\nlines"], rows[1]);
    }

    [Fact]
    public void Blank_lines_and_a_missing_final_newline_are_tolerated()
    {
        var rows = DelimitedTextParser.Parse("a,b\n\n1,2\n\n3,4", 10);

        Assert.Equal(3, rows.Count);
        Assert.Equal(["3", "4"], rows[2]);
    }

    [Fact]
    public void Empty_trailing_fields_are_kept()
    {
        var rows = DelimitedTextParser.Parse("a,b,c\n1,,\n", 10);

        Assert.Equal(["1", "", ""], rows[1]);
    }

    [Theory]
    [InlineData("a,\"unterminated\n")]
    [InlineData("a,\"closed\"then text\n")]
    public void Malformed_quoting_is_refused_not_guessed(string text)
    {
        Assert.Throws<FileImportFormatException>(() => DelimitedTextParser.Parse(text, 10));
    }

    [Fact]
    public void Row_limit_is_enforced()
    {
        var ex = Assert.Throws<FileImportFormatException>(() => DelimitedTextParser.Parse("h\n1\n2\n3\n", 3));
        Assert.Contains("more than 2", ex.Message);
    }

    [Theory]
    [InlineData("Done", WorkItemLifecycleStage.Done)]
    [InlineData("Closed", WorkItemLifecycleStage.Done)]
    [InlineData("COMPLETE", WorkItemLifecycleStage.Done)]
    [InlineData("In Progress", WorkItemLifecycleStage.InProgress)]
    [InlineData("in-development", WorkItemLifecycleStage.InProgress)]
    [InlineData("To Do", WorkItemLifecycleStage.Backlog)]
    [InlineData("Not started", WorkItemLifecycleStage.Backlog)]
    [InlineData("Ready for QA", WorkItemLifecycleStage.InReview)]
    [InlineData("Code review", WorkItemLifecycleStage.InReview)]
    [InlineData("Blocked", WorkItemLifecycleStage.Blocked)]
    [InlineData("In progress - blocked", WorkItemLifecycleStage.Blocked)]
    [InlineData("On hold", WorkItemLifecycleStage.Blocked)]
    [InlineData("Won't do", WorkItemLifecycleStage.Cancelled)]
    [InlineData("InReview", WorkItemLifecycleStage.InReview)]
    [InlineData("Waiting on client", WorkItemLifecycleStage.Unmapped)]
    [InlineData("Sprint 4", WorkItemLifecycleStage.Unmapped)]
    [InlineData("Cancelled then reopened", WorkItemLifecycleStage.Unmapped)]
    [InlineData("", WorkItemLifecycleStage.Unmapped)]
    [InlineData("3", WorkItemLifecycleStage.Unmapped)]
    public void Status_text_maps_conservatively(string raw, WorkItemLifecycleStage expected)
    {
        Assert.Equal(expected, ImportedStatusMapper.Map(raw));
    }
}
