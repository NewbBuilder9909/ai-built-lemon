using ProgrammePulse.Models.ViewModels;
using ProgrammePulse.Models.ViewModels.ProgrammeOverview;
using ProgrammePulse.Services.Reporting;

namespace ProgrammePulse.Tests.Reporting;

/// <summary>
/// The Programme Overview export is how the landing page's figures leave the
/// product. The claim is evidence you can challenge, so provenance travels
/// first, and nothing a spreadsheet would execute travels at all.
/// </summary>
public class ProgrammeOverviewCsvTests
{
    private static readonly DateTime Generated = new(2026, 9, 27, 9, 30, 0, DateTimeKind.Utc);

    private static ProgrammeOverviewPageViewModel Page(string workstream = "Discovery", int unresolved = 1) => new(
        new ProgrammeOverviewViewModel(
            [new KpiCardViewModel("Overdue", "11")],
            [new WorkstreamStatusViewModel("Imported delivery data", "Website rebuild", workstream, 7, 3, 2, 4, 43)],
            [new MilestoneStatusViewModel("Discovery task 1", workstream, new DateTime(2026, 9, 7), "Blocked", true)],
            [new ResourceCapacityViewModel("Alex Morgan", 37.500000000m, 8, 128.000000m, 0)],
            [],
            new PlannedAllocationCoverageViewModel(0, 0, 0, 0, Generated, Generated.AddDays(28)),
            Generated),
        [
            new SourcePublicationStateViewModel("FileImport", "File import", Generated.AddHours(-2), "36 work items imported", false, null, null, false, null, null, null, "2 h ago"),
            new SourcePublicationStateViewModel("ClickUp", "ClickUp", null, null, false, null, null, false, null, null, null, "never")
        ],
        unresolved, false, [], null, null);

    [Fact]
    public void Provenance_comes_before_any_figure()
    {
        var lines = ProgrammeOverviewCsv.Build(Page(), "Northstar", Generated).Split("\r\n");

        Assert.Equal("Organisation,Northstar", lines[1]);
        Assert.Contains("Generated (UTC),2026-09-27 09:30", lines);
        Assert.Contains("People in source data not matched to a staff profile,1", lines);
        Assert.True(Array.FindIndex(lines, l => l.StartsWith("Delivery scope,")) < Array.IndexOf(lines, "Programme health review"));
        Assert.True(Array.IndexOf(lines, "Data sources") < Array.IndexOf(lines, "Workstream status"));
    }

    /// <summary>Same rule as the page: a source that never ran is not a freshness fact.</summary>
    [Fact]
    public void Only_sources_that_have_published_are_listed()
    {
        var csv = ProgrammeOverviewCsv.Build(Page(), "Northstar", Generated);

        Assert.Contains("File import,Published,2026-09-27 07:30,2 h ago,36 work items imported", csv);
        Assert.DoesNotContain("ClickUp", csv);
    }

    [Fact]
    public void Figures_are_formatted_for_a_spreadsheet_not_as_raw_decimals()
    {
        var csv = ProgrammeOverviewCsv.Build(Page(), "Northstar", Generated);

        Assert.Contains("Alex Morgan,37.5,8,128,0", csv);
        Assert.Contains("Discovery task 1,Discovery,2026-09-07,Blocked,Yes", csv);
    }

    /// <summary>Workstream names come from customer uploads; one starting "=" must not run as a formula.</summary>
    [Fact]
    public void A_name_that_looks_like_a_formula_is_neutralised()
    {
        var csv = ProgrammeOverviewCsv.Build(Page(workstream: "=HYPERLINK(\"http://evil\")"), "Northstar", Generated);

        Assert.DoesNotContain(",=HYPERLINK", csv);
        Assert.Contains("'=HYPERLINK", csv);
    }
}
