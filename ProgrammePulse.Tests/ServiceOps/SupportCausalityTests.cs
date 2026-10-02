using System.Reflection;
using ProgrammePulse.Models.Integrations.Freshdesk.Raw;
using ProgrammePulse.Models.ServiceOps;
using ProgrammePulse.Services.ServiceOps;
using static ProgrammePulse.Tests.ServiceOps.FakeFreshdeskClient;
using ProgrammePulse.Services.Shared;

namespace ProgrammePulse.Tests.ServiceOps;

/// <summary>
/// The rule that matters most in Slice 3: a matching issue key is a
/// **relationship**, not proof that a change caused a case.
///
/// The failure mode these prevent is a product that tells a manager
/// which developer caused which outage on evidence that amounts to two
/// strings matching. Several of these are structural — they fail the
/// build if a field or an enum value appears that would make blame
/// expressible at all — because a rule enforced only by a code reviewer
/// is a rule that eventually lapses.
/// </summary>
public class SupportCausalityTests
{
    private static readonly DateTime Sept1 = new(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc);

    private static async Task<(ServiceOpsTestContext Ctx, SupportCodeLink Link)> WithSuggestedLinkAsync()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage(
            [Ticket("1", Sept1, issueKeys: "PROJ-42")], Sept1, true));
        await ctx.RunAsync();

        return (ctx, ctx.Repository.Links.Single());
    }

    [Fact]
    public async Task An_issue_key_match_is_recorded_as_a_relationship_and_nothing_more()
    {
        var (_, link) = await WithSuggestedLinkAsync();

        Assert.Equal(SupportLinkMethod.IssueKeyMatch, link.Method);
        Assert.True(link.IsRelationshipOnly);
        Assert.False(link.IsCausalClaim);
        Assert.False(link.IsReviewed);
        Assert.Null(link.ReviewedByStaffKey);
    }

    [Fact]
    public async Task Linking_by_hand_is_still_not_a_causal_claim()
    {
        var ctx = new ServiceOpsTestContext();
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1)], Sept1, true));
        await ctx.RunAsync();

        var link = await ctx.LinkService.LinkManuallyAsync(
            ctx.DeskA.ConnectionKey, "1", LinkedArtifactType.PullRequest, "pr-99",
            "acme/web", null, ctx.Alex.StaffKey, ServiceOpsTestContext.TenantA, 1);

        Assert.Equal(SupportLinkMethod.ManuallyLinked, link.Method);
        Assert.False(link.IsCausalClaim);
    }

    [Fact]
    public async Task Confirming_a_root_cause_requires_a_rationale()
    {
        var (ctx, link) = await WithSuggestedLinkAsync();

        var ex = await Assert.ThrowsAsync<ServiceOpsValidationException>(() => ctx.LinkService.ConfirmRootCauseAsync(
            link.LinkKey, ctx.Alex.StaffKey, "   ", ServiceOpsTestContext.TenantA, 1));

        Assert.Contains("assessment", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(ctx.Repository.Links.Single().IsCausalClaim);
    }

    [Fact]
    public async Task Ruling_out_requires_a_rationale_too()
    {
        var (ctx, link) = await WithSuggestedLinkAsync();

        await Assert.ThrowsAsync<ServiceOpsValidationException>(() => ctx.LinkService.RuleOutAsync(
            link.LinkKey, ctx.Alex.StaffKey, "", ServiceOpsTestContext.TenantA, 1));
    }

    [Fact]
    public async Task A_confirmed_root_cause_records_the_reviewer_the_time_and_the_reasoning()
    {
        var (ctx, link) = await WithSuggestedLinkAsync();

        var confirmed = await ctx.LinkService.ConfirmRootCauseAsync(
            link.LinkKey, ctx.Sarah.StaffKey, "the null check was removed in this change",
            ServiceOpsTestContext.TenantA, 42);

        Assert.True(confirmed.IsCausalClaim);
        Assert.Equal(ctx.Sarah.StaffKey, confirmed.ReviewedByStaffKey);
        Assert.Equal(ctx.Time.Now.UtcDateTime, confirmed.ReviewedAtUtc);
        Assert.Equal("the null check was removed in this change", confirmed.ReviewNote);

        var audit = ctx.Repository.Audit.Single(a => a.Action == ServiceOpsAuditAction.RootCauseConfirmed);
        Assert.Equal(42, audit.ActorMemberId);
    }

    [Fact]
    public async Task Ruling_out_is_recorded_rather_than_deleted_so_it_is_not_re_investigated()
    {
        var (ctx, link) = await WithSuggestedLinkAsync();

        await ctx.LinkService.RuleOutAsync(
            link.LinkKey, ctx.Sarah.StaffKey, "the change shipped after the case was raised",
            ServiceOpsTestContext.TenantA, 1);

        var ruled = ctx.Repository.Links.Single();
        Assert.Equal(SupportLinkMethod.RuledOut, ruled.Method);
        Assert.True(ruled.IsReviewed);
        Assert.False(ruled.IsCausalClaim);
        Assert.Contains(ctx.Repository.Audit, a => a.Action == ServiceOpsAuditAction.RootCauseRuledOut);
    }

    [Fact]
    public async Task A_later_sync_never_overwrites_a_reviewers_verdict()
    {
        var (ctx, link) = await WithSuggestedLinkAsync();
        await ctx.LinkService.RuleOutAsync(
            link.LinkKey, ctx.Sarah.StaffKey, "unrelated change", ServiceOpsTestContext.TenantA, 1);

        // The same issue key comes round again on the next sync. If it
        // reverted the verdict, a reviewer's work would be undone nightly.
        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1, issueKeys: "PROJ-42")], Sept1, true));
        await ctx.RunAsync();

        Assert.Equal(SupportLinkMethod.RuledOut, ctx.Repository.Links.Single().Method);
    }

    [Fact]
    public async Task A_confirmed_cause_survives_a_later_sync_too()
    {
        var (ctx, link) = await WithSuggestedLinkAsync();
        await ctx.LinkService.ConfirmRootCauseAsync(
            link.LinkKey, ctx.Sarah.StaffKey, "confirmed at the incident review", ServiceOpsTestContext.TenantA, 1);

        ctx.Client.WithTickets(new FreshdeskPage([Ticket("1", Sept1, issueKeys: "PROJ-42")], Sept1, true));
        await ctx.RunAsync();

        Assert.True(ctx.Repository.Links.Single().IsCausalClaim);
    }

    [Fact]
    public async Task Re_linking_something_a_reviewer_has_decided_is_refused()
    {
        var (ctx, link) = await WithSuggestedLinkAsync();
        await ctx.LinkService.RuleOutAsync(
            link.LinkKey, ctx.Sarah.StaffKey, "unrelated", ServiceOpsTestContext.TenantA, 1);

        await Assert.ThrowsAsync<ServiceOpsValidationException>(() => ctx.LinkService.LinkManuallyAsync(
            ctx.DeskA.ConnectionKey, "1", LinkedArtifactType.Issue, "PROJ-42",
            null, null, ctx.Alex.StaffKey, ServiceOpsTestContext.TenantA, 1));
    }

    [Fact]
    public async Task A_link_key_from_another_tenant_reads_as_not_found()
    {
        var (ctx, link) = await WithSuggestedLinkAsync();

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => ctx.LinkService.ConfirmRootCauseAsync(
            link.LinkKey, ctx.Rhian.StaffKey, "reaching across tenants", ServiceOpsTestContext.TenantB, 1));
    }

    [Fact]
    public async Task A_suggestion_against_a_case_that_is_not_this_tenants_is_refused()
    {
        var ctx = new ServiceOpsTestContext();

        await Assert.ThrowsAsync<CrossTenantReferenceException>(() => ctx.LinkService.SuggestLinksAsync(
        [
            new SupportCodeLink
            {
                LinkKey = Guid.NewGuid(),
                TenantId = ServiceOpsTestContext.TenantB,
                ConnectionKey = ctx.DeskB.ConnectionKey,
                ExternalTicketId = "1",
                ArtifactType = LinkedArtifactType.Issue,
                ArtifactExternalId = "PROJ-1",
                Method = SupportLinkMethod.IssueKeyMatch,
                CreatedAtUtc = ctx.Time.Now.UtcDateTime,
                UpdatedAtUtc = ctx.Time.Now.UtcDateTime
            }
        ], ServiceOpsTestContext.TenantA, ctx.Time.Now.UtcDateTime));
    }

    // ---- Structural guards ----

    /// <summary>
    /// A root cause is a property of a change, not of a person. Giving
    /// the record nowhere to name someone is what makes "do not assign
    /// blame by temporal proximity or git blame" enforceable rather than
    /// aspirational — the reviewer field is the *assessor*, not a target.
    /// </summary>
    [Fact]
    public void A_support_code_link_has_nowhere_to_record_who_to_blame()
    {
        string[] forbidden = ["author", "committer", "blame", "culprit", "responsible", "causedby", "offender", "owner"];

        var offenders = typeof(SupportCodeLink)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => forbidden.Any(f => p.Name.Replace("_", "").Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"SupportCodeLink gained a blame-shaped member: {string.Join(", ", offenders)}. "
            + "A root cause is a property of a change, not of a person.");
    }

    /// <summary>
    /// The participation roles are all things a person *did about* a
    /// case. There is deliberately no value meaning "caused it": the
    /// design document is explicit that someone who fixed a defect can
    /// receive positive resolution evidence without being labelled
    /// responsible for it.
    /// </summary>
    [Fact]
    public void No_participation_role_can_express_blame()
    {
        string[] forbidden = ["caus", "blame", "culprit", "responsible", "offender", "introduc"];

        foreach (var role in Enum.GetNames<SupportCaseRole>())
        {
            Assert.DoesNotContain(forbidden, f => role.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal(
            new[] { "Contributor", "Resolver", "Reviewer" },
            Enum.GetNames<SupportCaseRole>().Order().ToArray());
    }

    /// <summary>
    /// Nothing in this area may reach a commit's author. Temporal
    /// proximity and <c>git blame</c> are both ruled out by the design
    /// document, and the way to guarantee neither creeps in is to keep
    /// the engineering-evidence types out of the link services entirely.
    /// </summary>
    [Fact]
    public void No_service_ops_type_can_reach_engineering_evidence_authorship()
    {
        Type[] services =
        [
            typeof(SupportCodeLinkService),
            typeof(ServiceHealthQueryService),
            typeof(ServiceOpsRepository),
            typeof(ProgrammePulse.Services.Integrations.Freshdesk.FreshdeskIngestionService)
        ];

        foreach (var service in services)
        {
            var dependencies = service.GetConstructors()
                .SelectMany(c => c.GetParameters())
                .Select(p => p.ParameterType.FullName ?? string.Empty)
                .ToArray();

            Assert.DoesNotContain(dependencies, name => name.Contains("EngineeringEvidence", StringComparison.Ordinal));
            Assert.DoesNotContain(dependencies, name => name.Contains("SkillsEvidence", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// The whole feature area must stay independent of engineering
    /// evidence — a customer can connect a desk and no repository at all.
    /// A link points at an artefact by plain string id, never by a typed
    /// reference into the other area.
    /// </summary>
    [Fact]
    public void Service_ops_never_references_the_evidence_or_vendor_namespaces()
    {
        string[] forbiddenNamespaces =
        [
            "ProgrammePulse.Models.SkillsEvidence",
            "ProgrammePulse.Services.SkillsEvidence",
            "ProgrammePulse.Services.Integrations.Freshdesk",
            "ProgrammePulse.Models.Integrations.Freshdesk"
        ];

        string[] neutral = ["ProgrammePulse.Models.ServiceOps", "ProgrammePulse.Services.ServiceOps"];

        var assembly = typeof(SupportCaseFact).Assembly;
        var violations = new List<string>();

        foreach (var type in assembly.GetTypes().Where(t => t.Namespace is not null && neutral.Contains(t.Namespace)))
        {
            foreach (var referenced in ReferencedTypes(type))
            {
                if (referenced.Namespace is { } ns
                    && forbiddenNamespaces.Any(f => ns == f || ns.StartsWith(f + ".", StringComparison.Ordinal)))
                {
                    violations.Add($"{type.FullName} references {referenced.FullName}");
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Service Ops must stay independent of the evidence area and provider-neutral. Violations:"
            + Environment.NewLine + string.Join(Environment.NewLine, violations.Distinct().Order()));
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var field in type.GetFields(All)) yield return field.FieldType;
        foreach (var property in type.GetProperties(All)) yield return property.PropertyType;

        foreach (var method in type.GetMethods(All))
        {
            yield return method.ReturnType;
            foreach (var parameter in method.GetParameters()) yield return parameter.ParameterType;
        }

        foreach (var constructor in type.GetConstructors(All))
        {
            foreach (var parameter in constructor.GetParameters()) yield return parameter.ParameterType;
        }
    }

    /// <summary>
    /// A case is somebody's customer's problem, often in their own words.
    /// This product counts and categorises cases; it does not read them.
    /// </summary>
    [Fact]
    public void A_support_case_has_nowhere_to_hold_ticket_content_or_a_requester()
    {
        string[] forbidden = ["body", "description", "requester", "contact", "email", "phone", "attachment", "subject", "customername"];

        var offenders = typeof(SupportCaseFact)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => forbidden.Any(f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"SupportCaseFact gained a content-shaped member: {string.Join(", ", offenders)}. "
            + "Only metadata and a link back to the desk are stored.");
    }

    /// <summary>The component view is readable with a wider grant, so it must not be able to name anyone.</summary>
    [Fact]
    public void The_component_view_has_nowhere_to_put_a_person()
    {
        string[] forbidden = ["staff", "agent", "person", "resolver", "assignee", "employee", "user"];

        var offenders = typeof(ComponentServiceRow)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => forbidden.Any(f => p.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Name)
            .ToArray();

        Assert.True(offenders.Length == 0,
            $"ComponentServiceRow gained a person-shaped member: {string.Join(", ", offenders)}. "
            + "Support demand belongs to a product and its owning team, not to an individual.");
    }
}
