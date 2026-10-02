using ProgrammePulse.Models.Programme;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.Shared;

public class DisplayTextTests
{
    [Theory]
    [InlineData("UpcomingOverAllocation", "Upcoming over allocation")]
    [InlineData("InProgress", "In progress")]
    [InlineData("Done", "Done")]
    [InlineData("", "")]
    public void Identifiers_read_as_sentence_case_words(string identifier, string expected) =>
        Assert.Equal(expected, DisplayText.Words(identifier));

    [Fact]
    public void Enum_values_use_their_name() =>
        Assert.Equal("In review", DisplayText.Words(WorkItemLifecycleStage.InReview));

    [Fact]
    public void Audit_json_reads_as_labelled_fields_with_ids_shortened()
    {
        var fields = DisplayText.Fields("{\"what\":\"time entries\",\"rows\":1832,\"ignoredColumns\":[],\"runKey\":\"22d654a3-b37f-4c11-9333-a55cb58de453\",\"ok\":true}");

        Assert.Equal(
            [("What", "time entries"), ("Rows", "1832"), ("Ignored columns", "none"), ("Run key", "22d654a3…"), ("Ok", "yes")],
            fields);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Empty_detail_has_no_fields(string? json) => Assert.Empty(DisplayText.Fields(json));

    [Fact]
    public void Text_that_is_not_a_json_object_is_shown_as_it_is() =>
        Assert.Equal([("Detail", "not json")], DisplayText.Fields("not json"));
}
