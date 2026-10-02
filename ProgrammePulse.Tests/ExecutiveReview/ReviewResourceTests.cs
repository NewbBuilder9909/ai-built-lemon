using System.Xml.Linq;
using System.Text.RegularExpressions;
using ProgrammePulse.Models.ExecutiveReview;

namespace ProgrammePulse.Tests.ExecutiveReview;

public class ReviewResourceTests
{
    [Fact]
    public void Selected_review_journey_has_unique_nonempty_English_and_Welsh_resource_keys()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Resources", "SharedResource.resx")))
            directory = directory.Parent;
        var root = directory?.FullName ?? throw new InvalidOperationException("Repository resources not found.");
        Dictionary<string, string> Read(string filename) => XDocument.Load(Path.Combine(root, "Resources", filename))
            .Root!.Elements("data").Where(e => e.Attribute("name")!.Value.StartsWith("Review.", StringComparison.Ordinal)
                || e.Attribute("name")!.Value.StartsWith("Decision.", StringComparison.Ordinal))
            .ToDictionary(e => e.Attribute("name")!.Value, e => e.Element("value")!.Value);
        var english = Read("SharedResource.resx");
        var welsh = Read("SharedResource.cy-GB.resx");
        Assert.NotEmpty(english);
        Assert.Equal(english.Keys.Order(), welsh.Keys.Order());
        foreach (var value in english.Values.Concat(welsh.Values)) Assert.False(string.IsNullOrWhiteSpace(value));
        var expected = Enum.GetNames<DecisionStatus>().Select(name => "Decision.Status." + name)
            .Concat(Enum.GetNames<EvidenceDisposition>().Select(name => "Decision.Evidence." + name))
            .Concat(Enum.GetNames<DecisionAction>().Select(name => "Decision.Action." + name))
            .Concat(Enum.GetNames<DecisionAction>().Select(name => "Decision.Event." + name))
            .Concat(Enum.GetNames<ExecutiveDataOperation>().Select(name => "Review.Data.Operation." + name))
            .Concat(Enum.GetNames<ExecutiveDataReason>().Select(name => "Review.Data.Reason." + name));
        foreach (var key in expected) Assert.True(english.ContainsKey(key), key);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(root, "Views", "StaffOps", "ExecutiveReview"), "*.cshtml"))
            foreach (Match match in Regex.Matches(File.ReadAllText(file), "\"((?:Review|Decision)\\.[A-Za-z.]+)\""))
                Assert.True(english.ContainsKey(match.Groups[1].Value), $"{file}: {match.Groups[1].Value}");
    }
}
