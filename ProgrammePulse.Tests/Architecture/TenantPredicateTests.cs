using System.Text.RegularExpressions;

namespace ProgrammePulse.Tests.Architecture;

/// <summary>
/// Tenant isolation in this codebase is enforced by every repository method
/// putting <c>tenantId</c> into its SQL. That was a convention, backed by
/// per-method tests; nothing stopped the *next* method from leaving the
/// predicate out. This makes the omission a build failure.
///
/// Every method under Services/ that issues SQL with a filter or a mutation
/// (<c>.Where("…")</c>, <c>WHERE</c>, <c>UPDATE</c>, <c>DELETE FROM</c>) must
/// mention <c>tenantId</c> in each such statement, or be listed below with
/// the reason it legitimately does not. The reasons fall into a few kinds,
/// and each entry names its kind so a reviewer can check it:
///
/// - Retention: a platform-wide purge by age, deliberately across tenants.
/// - Erasure: removes one person's rows everywhere (GDPR), keyed by a
///   globally unique staff key that the caller has already proven.
/// - Platform: the table is not tenant data (tenants themselves, member
///   MFA, sign-in challenges, the staff-by-member lookup that tenant
///   resolution itself runs on).
/// - Owned key: keyed by a globally unique key whose tenant the caller has
///   already checked. Each of these is a place where the check lives in the
///   caller rather than the SQL, which is exactly what this list exposes.
///
/// A heuristic over source text, not a SQL parser: statements are split on
/// ';'. It errs towards flagging, which is the safe direction.
/// </summary>
public partial class TenantPredicateTests
{
    private static readonly Dictionary<string, string> Exemptions = new()
    {
        // --- Platform: not tenant data ---
        ["Tenancy/TenantRepository.GetByKeyAsync"] = "Platform: the tenant row itself.",
        ["Tenancy/TenantRepository.GetAllActiveAsync"] = "Platform: tenant list for the operator console.",
        ["Tenancy/TenantRepository.UpdateAsync"] = "Platform: the tenant row itself.",
        ["Security/MfaRepository.GetForMemberAsync"] = "Platform: member MFA belongs to the login identity, not a tenant.",
        ["Security/MfaRepository.StartEnrollmentAsync"] = "Platform: member MFA.",
        ["Security/MfaRepository.TryAcceptTotpAsync"] = "Platform: member MFA.",
        ["Security/MfaRepository.SetRecoveryCodeHashesAsync"] = "Platform: member MFA.",
        ["Security/MfaRepository.RedeemRecoveryCodeAsync"] = "Platform: member MFA.",
        ["Security/MfaRepository.ResetAsync"] = "Platform: member MFA.",
        ["Security/MfaRepository.GetLockedUntilAsync"] = "Platform: member MFA lockout.",
        ["Security/MfaRepository.TryBeginAttemptAsync"] = "Platform: member MFA lockout.",
        ["Security/MfaRepository.ClearFailuresAsync"] = "Platform: member MFA lockout.",
        ["Security/MfaChallengeStore.StartAsync"] = "Platform: short-lived sign-in challenge, keyed by an unguessable challenge key.",
        ["Security/MfaChallengeStore.ValidateAsync"] = "Platform: sign-in challenge.",
        ["Security/MfaChallengeStore.ClearAsync"] = "Platform: sign-in challenge.",
        ["Security/MfaChallengeStore.TryConsumeAsync"] = "Platform: sign-in challenge.",
        ["Security/MfaChallengeStore.RecordFailedAttemptAsync"] = "Platform: sign-in challenge attempt budget, keyed by the challenge and member.",
        ["Commercial/SelfServiceSignupService.CleanupProvisioningAsync"] = "Proven key: undoes a failed sign-up by the tenant and staff keys it created moments before.",
        ["Security/RevokeMfaChallengesOnMemberSaved.HandleAsync"] = "Platform: sign-in challenge.",
        ["Staff/StaffRepository.GetByMemberIdAsync"] = "Platform: tenant resolution itself starts here (member → staff → tenant).",
        ["Staff/StaffRepository.GetAllAsync"] = "Platform: only the Platform Admin console (TenantAdminService) counts staff across tenants.",
        ["Tenancy/TenantRepository.GetAllAsync"] = "Platform: the tenant list for the Platform Admin console.",
        ["ProgrammeOps/SyncRunRepository.FirstAsync"] = "Helper: runs a builder its callers have already scoped (GetLatestAsync et al.) or named AcrossTenants.",

        // --- Owned key: the caller proves the key's tenant first ---
        ["Staff/StaffRepository.GetByStaffKeyAsync"] = "Owned key: callers compare the returned TenantId (e.g. StaffAdminController.GetTenantStaffAsync).",
        ["Staff/StaffRepository.UpdateDefaultWorkHoursAsync"] = "Owned key: staff key proven by the caller.",
        ["Staff/StaffRepository.AnonymizeAsync"] = "Erasure: one person, key proven by the caller.",
        ["Staff/StaffRateRepository.GetCurrentAsync"] = "Owned key: rates are per staff key; StaffRate is intentionally global (docs/tenancy.md matrix).",
        ["Staff/StaffRateRepository.GetHistoryAsync"] = "Owned key: see GetCurrentAsync.",
        ["Staff/StaffRateRepository.SetCurrentRateAsync"] = "Owned key: see GetCurrentAsync.",
        ["Staff/WorkHoursHistoryRepository.GetCurrentAsync"] = "Owned key: per staff key, mirrors StaffRate.",
        ["Staff/WorkHoursHistoryRepository.GetHistoryAsync"] = "Owned key: per staff key.",
        ["Staff/WorkHoursHistoryRepository.SetCurrentHoursAsync"] = "Owned key: per staff key.",
        ["Staff/AvailabilityRepository.GetForStaffAsync"] = "Owned key: availability is global per staff key (docs/tenancy.md matrix).",
        ["ContractOps/ContractRepository.GetInvoiceAsync"] = "Owned key: lines are read by the invoice key after the invoice itself was read with tenantId.",
        ["SkillsEvidence/SkillsEvidenceRepository.AppendAsync"] = "Owned key: reads the superseded row by key, then rejects it unless its TenantId matches.",
        ["ExecutiveReview/ExecutiveDecisionRepository.GetQueueAsync"] = "Owned key: status filter appended to a builder already scoped by d.tenantId.",

        // --- Erasure: one person, everywhere ---
        ["ProgrammeOps/IdentityResolutionRepository.GetLinksForStaffAsync"] = "Erasure/export: one person's identity links.",
        ["ProgrammeOps/IdentityResolutionRepository.DeleteLinksForStaffAsync"] = "Erasure: one person.",
        ["ServiceOps/ServiceOpsRepository.GetAgentLinksForStaffAsync"] = "Erasure/export: one person.",
        ["ServiceOps/ServiceOpsRepository.DeleteAgentLinksForStaffAsync"] = "Erasure: one person.",
        ["ServiceOps/ServiceOpsRepository.DetachParticipationForStaffAsync"] = "Erasure: one person.",
        ["SkillsEvidence/ContinuityRepository.GetBackupsForStaffAsync"] = "Erasure/export: one person.",
        ["SkillsEvidence/ContinuityRepository.DetachStaffAsync"] = "Erasure: one person.",
        ["SkillsEvidence/EngineeringEvidenceRepository.GetActorLinksForStaffAsync"] = "Erasure/export: one person.",
        ["SkillsEvidence/EngineeringEvidenceRepository.DeleteActorLinksForStaffAsync"] = "Erasure: one person.",
        ["SkillsEvidence/EngineeringEvidenceRepository.DeleteEvidenceForStaffAsync"] = "Erasure: one person.",
        ["SkillsEvidence/EngineeringEvidenceRepository.EraseSubjectTracesAsync"] = "Erasure: one person, and the source identities their links held.",
        ["SkillsEvidence/SkillsEvidenceRepository.DeleteAllForStaffAsync"] = "Erasure: one person.",
        ["SkillsEvidence/SuggestionRepository.DeleteForStaffAsync"] = "Erasure: one person.",
        ["ProgrammeOps/EvidenceReviewRepository.GetStaffReferencesAsync"] = "Erasure/export: one person's review owner, decider and recorder references.",
        ["ProgrammeOps/EvidenceReviewRepository.EraseStaffReferencesAsync"] = "Erasure: one person.",

        // --- Retention: platform-wide purge by age ---
        ["ProgrammeOps/ClickUpRawPayloadRepository.DeleteOlderThanAsync"] = "Retention: Bronze purge across tenants.",
        ["ProgrammeOps/HubPlannerRawPayloadRepository.DeleteOlderThanAsync"] = "Retention: Bronze purge across tenants.",
        ["ProgrammeOps/RawConnectorPayloadRepository.DeleteOlderThanAsync"] = "Retention: Bronze purge across tenants.",
        ["ProgrammeOps/ImportStagingRepository.DeleteOlderThanAsync"] = "Retention: unconfirmed staged imports purged across tenants.",
        ["ProgrammeOps/IdentityResolutionRepository.DeleteUnresolvedNotSeenSinceAsync"] = "Retention: stale queue rows across tenants.",
        ["ProgrammeOps/SyncRunRepository.DeleteFinishedOlderThanAsync"] = "Retention: finished run history across tenants.",
        ["ServiceOps/ServiceOpsRepository.PurgeRawBefore"] = "Retention: Bronze purge across tenants.",
        ["SkillsEvidence/EngineeringEvidenceRepository.PurgeRawBefore"] = "Retention: Bronze purge across tenants.",
        ["SecurityAssurance/SecurityAssuranceRepository.PurgeRawBeforeAsync"] = "Retention: Bronze purge across tenants.",

        // --- Sync runs: keyed by the run's own unguessable key ---
        ["ProgrammeOps/SyncRunRepository.HeartbeatAsync"] = "Owned key: runKey issued to this worker by a tenant-scoped lease.",
        ["ProgrammeOps/SyncRunRepository.FailAsync"] = "Owned key: runKey.",
        ["ProgrammeOps/SyncRunRepository.GetLatestAcrossTenantsAsync"] = "Platform: operator console; named AcrossTenants on purpose.",
        ["ProgrammeOps/SyncRunRepository.GetLatestSuccessfulAcrossTenantsAsync"] = "Platform: operator console; named AcrossTenants on purpose.",
    };

    [Fact]
    public void Every_sql_predicate_is_tenant_scoped_or_has_a_documented_reason()
    {
        var unscoped = Scan()
            .Where(m => !m.AllStatementsScoped)
            .Select(m => m.Key)
            .Where(key => !Exemptions.ContainsKey(key))
            .Distinct()
            .Order()
            .ToList();

        Assert.True(unscoped.Count == 0,
            "These methods filter or change rows without tenantId in the SQL. Add the predicate, or — only if the "
            + "method is platform-scoped, an erasure, a retention purge, or keyed by something the caller has already "
            + "proven — add an exemption with its reason:" + Environment.NewLine + string.Join(Environment.NewLine, unscoped));
    }

    [Fact]
    public void Every_exemption_still_names_an_unscoped_method()
    {
        var methods = Scan().ToLookup(m => m.Key);

        var stale = Exemptions.Keys
            .Where(key => !methods.Contains(key) || methods[key].All(m => m.AllStatementsScoped))
            .Order()
            .ToList();

        Assert.True(stale.Count == 0,
            "These exemptions no longer match an unscoped method (renamed, deleted, or now scoped) — remove them: "
            + string.Join(", ", stale));
    }

    [Fact]
    public void The_scan_finds_scoped_sql_too()
    {
        // Vacuous-pass guard: the overwhelming majority of methods are scoped.
        Assert.True(Scan().Count(m => m.AllStatementsScoped) > 100);
    }

    private sealed record SqlMethod(string Key, bool AllStatementsScoped);

    private static IEnumerable<SqlMethod> Scan()
    {
        foreach (var file in Directory.GetFiles(SourceTree.ServicesDirectory, "*.cs", SearchOption.AllDirectories))
        {
            var code = SourceTree.CodeOf(file);
            if (!code.Contains("Database.", StringComparison.Ordinal) && !code.Contains("Sql.Builder", StringComparison.Ordinal))
            {
                continue;
            }

            var relative = Path.GetRelativePath(SourceTree.ServicesDirectory, file).Replace('\\', '/');
            var prefix = relative[..relative.LastIndexOf('/')] + "/" + Path.GetFileNameWithoutExtension(file);

            var members = MemberStart().Matches(code).ToArray();
            for (var i = 0; i < members.Length; i++)
            {
                var start = members[i].Index;
                var end = i + 1 < members.Length ? members[i + 1].Index : code.Length;
                var body = code[start..end];

                var predicates = body.Split(';').Where(IsPredicate).ToArray();

                // A read with no WHERE at all is the widest query there is,
                // and the first version of this test missed it:
                // StaffAuditLogRepository.GetRecentAsync returned the whole
                // platform's staff audit trail to any tenant's Admin. A
                // method that reads must mention tenantId somewhere, since
                // its builder can span several statements.
                var reads = ReadCall().IsMatch(body);
                if (predicates.Length == 0 && !reads)
                {
                    continue;
                }

                yield return new SqlMethod(
                    $"{prefix}.{members[i].Groups["name"].Value}",
                    predicates.All(s => s.Contains("tenantId", StringComparison.OrdinalIgnoreCase))
                        && (!reads || body.Contains("tenantId", StringComparison.OrdinalIgnoreCase)));
            }
        }
    }

    private static bool IsPredicate(string statement) =>
        StringWhere().IsMatch(statement) || SqlVerb().IsMatch(statement);

    /// <summary>A class member at the usual 4-space indent: its name is the method name.</summary>
    [GeneratedRegex(@"^    (?:public|private|internal|protected)[^\n=;{]*?\b(?<name>[A-Z][A-Za-z0-9_]*)\s*(?:<[^>\n]*>)?\s*\(", RegexOptions.Multiline)]
    private static partial Regex MemberStart();

    /// <summary>An NPoco read: Fetch/Page/First/Single/Query/ExecuteScalar on a database.</summary>
    [GeneratedRegex(@"Database\.(?:Fetch|Page|FirstOrDefault|First|SingleOrDefault|Single|Query|ExecuteScalar)\w*(?:<|\()")]
    private static partial Regex ReadCall();

    [GeneratedRegex(@"\.Where\(\$?""")]
    private static partial Regex StringWhere();

    [GeneratedRegex(@"""[^""]*\b(?:WHERE|DELETE FROM|UPDATE\s+\[?\{?)")]
    private static partial Regex SqlVerb();
}
