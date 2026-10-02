using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using ProgrammePulse.Services.Integrations.FileImport;
using ProgrammePulse.Services.Integrations.Resilience;
using ProgrammePulse.Services.ProgrammeOps;
using ProgrammePulse.Tests.Architecture;
using ProgrammePulse.Tests.ProgrammeOps;
using Xunit.Abstractions;

namespace ProgrammePulse.Tests.Scenarios.Patchwork;

/// <summary>
/// Keeps TestScenarios/Patchwork honest: the committed files are exactly what
/// the generator writes for the recorded date, every answer-key record is in
/// the file it names, the planted problems are the ones the README promises,
/// and nothing in the data is real. To regenerate (for example, to roll the
/// dates forward to this week):
/// <c>PP_UPDATE_PATCHWORK=1 PP_PATCHWORK_AS_OF=yyyy-MM-dd dotnet test --filter Patchwork</c>.
/// </summary>
public partial class PatchworkScenarioTests(ITestOutputHelper output)
{
    private const string UpdateVariable = "PP_UPDATE_PATCHWORK";
    private const string AsOfVariable = "PP_PATCHWORK_AS_OF";

    private static readonly string Folder = Path.Combine(SourceTree.Root, "TestScenarios", "Patchwork");
    private static readonly string KeyPath = Path.Combine(Folder, PatchworkAnswerKey.FileName);

    private static DateOnly CommittedAsOf() => PatchworkAnswerKey.FromJson(File.ReadAllText(KeyPath)).AsOf;

    private static IReadOnlyDictionary<string, byte[]> Generate(DateOnly asOf)
    {
        var world = new PatchworkWorld(asOf);
        var files = new Dictionary<string, byte[]>(PatchworkExports.Write(world))
        {
            [PatchworkAnswerKey.FileName] = Encoding.UTF8.GetBytes(PatchworkAnswerKey.Build(world).ToJson())
        };
        return files;
    }

    /// <summary>The text a file holds: the sheet XML for the workbook, the decoded text for the rest.</summary>
    private static string Text(string name, byte[] bytes) =>
        name.EndsWith(".xlsx", StringComparison.Ordinal) ? Xlsx.ReadSheet(bytes) : Encoding.UTF8.GetString(bytes).ReplaceLineEndings("\r\n");

    [Fact]
    public void The_committed_files_are_what_the_generator_writes()
    {
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            var asOf = Environment.GetEnvironmentVariable(AsOfVariable) is { Length: > 0 } given
                ? DateOnly.ParseExact(given, "yyyy-MM-dd")
                : File.Exists(KeyPath) ? CommittedAsOf() : DateOnly.FromDateTime(DateTime.Today);
            Directory.CreateDirectory(Folder);
            foreach (var (name, bytes) in Generate(asOf))
            {
                File.WriteAllBytes(Path.Combine(Folder, name), bytes);
            }

            return;
        }

        Assert.True(File.Exists(KeyPath), $"No scenario at {Folder}; run with {UpdateVariable}=1 to create it.");
        foreach (var (name, bytes) in Generate(CommittedAsOf()))
        {
            var path = Path.Combine(Folder, name);
            Assert.True(File.Exists(path), $"{name} is missing; run with {UpdateVariable}=1.");
            Assert.True(Text(name, bytes) == Text(name, File.ReadAllBytes(path)),
                $"{name} differs from what the generator writes; run with {UpdateVariable}=1 and review the diff.");
        }
    }

    [Fact]
    public void Every_answer_key_record_appears_in_a_file_it_names()
    {
        var files = Generate(CommittedAsOf());
        var key = PatchworkAnswerKey.FromJson(Encoding.UTF8.GetString(files[PatchworkAnswerKey.FileName]));

        foreach (var finding in key.Findings)
        {
            Assert.All(finding.Files, f => Assert.True(files.ContainsKey(f), $"{finding.Id} names {f}, which isn't in the scenario."));
            foreach (var record in finding.Records)
            {
                Assert.True(finding.Files.Any(f => Text(f, files[f]).Contains(record, StringComparison.Ordinal)),
                    $"{finding.Id}: \"{record}\" is in none of {string.Join(", ", finding.Files)}.");
            }
        }
    }

    [Fact]
    public void The_planted_problems_are_the_ones_the_readme_describes()
    {
        var key = PatchworkAnswerKey.Build(new PatchworkWorld(CommittedAsOf()));
        IReadOnlyList<string> Records(string id) => key.Findings.Single(f => f.Id == id).Records;

        Assert.Equal(["KEL-9", "KEL-15", "KEL-19"], Records("F01"));
        Assert.Equal(["KEL-14", "KEL-16", "KEL-18", "KEL-19"], Records("F02"));
        Assert.Equal(["KEL-15", "KEL-17", "KEL-21", "KEL-24"], Records("F03"));
        Assert.Equal(3, Records("F04").Count);
        Assert.Equal(6, Records("F05").Count);
        Assert.Equal(13, Records("F06").Count);
        Assert.Contains("OMB-4", Records("F09"));
        Assert.Equal(13, key.Findings.Count);
    }

    [Fact]
    public void Nothing_in_the_data_is_real()
    {
        var files = Generate(CommittedAsOf());
        foreach (var (name, bytes) in files)
        {
            var text = Text(name, bytes);
            Assert.True(ForbiddenNames.CountIn(text) == 0, $"{name} names an employer on the forbidden list.");

            var domains = EmailPattern().Matches(text).Select(m => m.Groups[1].Value).Distinct().ToList();
            Assert.All(domains, d => Assert.True(d.EndsWith(".example", StringComparison.Ordinal), $"{name} uses a real-looking email domain: {d}"));
        }
    }

    /// <summary>
    /// What today's importer makes of each native export, through the real
    /// parser and validation with persistence faked. This is a measurement, not
    /// a goal: it records how far the single generic CSV import is from
    /// reading other organisations' files as they arrive. Update it, and the
    /// README table, when an importer changes.
    /// </summary>
    [Fact]
    public async Task What_the_current_importer_accepts_unedited()
    {
        var files = Generate(CommittedAsOf());
        var expected = new Dictionary<string, (DeliveryExportKind Kind, bool Accepted)>
        {
            [PatchworkExports.MilestonePlan] = (DeliveryExportKind.WorkItems, false),
            [PatchworkExports.KelvaroJira] = (DeliveryExportKind.WorkItems, false),
            [PatchworkExports.OmbretonJira] = (DeliveryExportKind.WorkItems, false),
            [PatchworkExports.BrantoftTasks] = (DeliveryExportKind.WorkItems, false),
            [PatchworkExports.KelvaroTempo] = (DeliveryExportKind.TimeEntries, false),
            [PatchworkExports.BrantoftTime] = (DeliveryExportKind.TimeEntries, false),
            [PatchworkExports.Planner] = (DeliveryExportKind.TimeEntries, false),
            [PatchworkExports.DunmarrowHarvest] = (DeliveryExportKind.TimeEntries, true),
        };

        var actual = new Dictionary<string, bool>();
        foreach (var (name, (kind, _)) in expected)
        {
            var result = await ImportHarness.ImportAsync(kind, Encoding.UTF8.GetString(files[name]));
            var accepted = result.Imported || result.AwaitingConfirmation;
            actual[name] = accepted;
            output.WriteLine($"{name}: {(accepted ? "accepted" : "rejected")}. {result.Summary} " +
                string.Join(" | ", result.Errors.Take(3).Select(e => $"{e.Column}: {e.Message}")));
        }

        Assert.Equal(expected.ToDictionary(e => e.Key, e => e.Value.Accepted), actual);
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@([A-Za-z0-9.-]+\.[A-Za-z]{2,})")]
    private static partial Regex EmailPattern();

    private static class ImportHarness
    {
        private static readonly Guid Tenant = Guid.Parse("5e1f0000-0000-4000-8000-00000000c0de");

        public static Task<FileImportResult> ImportAsync(DeliveryExportKind kind, string csv)
        {
            var time = new FixedTime(new DateTimeOffset(CommittedAsOf().ToDateTime(new TimeOnly(18, 0)), TimeSpan.Zero));
            var options = Options.Create(new ProgrammeOpsOptions());
            var service = new DeliveryExportImportService(
                new FakeProgrammeRepository(), new NoRaw(), new StaffIdentityResolver(new FakeIdentityResolutionRepository(), new FakeStaffRepository(), time),
                new NoAudit(), new SyncRunCoordinator(new SyncRunGuard(), new FakeSyncRunRepository(), options, time), options, time,
                new FakeImportStagingRepository());
            return service.ImportAsync(Tenant, kind, csv, triggeredByMemberId: 1);
        }

        private sealed class FixedTime(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }

        private sealed class NoRaw : IRawConnectorPayloadRepository
        {
            public Task SaveAsync(Guid tenantId, string source, string sourceAccountId, string entityType, string externalId, string payloadJson, DateTime fetchedAtUtc) =>
                Task.CompletedTask;

            public Task<int> DeleteOlderThanAsync(DateTime cutoffUtc) => Task.FromResult(0);
        }

        private sealed class NoAudit : IAuditLogRepository
        {
            public Task LogAsync(string entityType, string entityId, string action, int? actorMemberId, string? detailJson, DateTime timestampUtc, Guid tenantId) =>
                Task.CompletedTask;

            public Task<IReadOnlyList<Models.Programme.AuditLog>> GetRecentAsync(int take, Guid tenantId) =>
                Task.FromResult<IReadOnlyList<Models.Programme.AuditLog>>([]);
        }
    }
}
