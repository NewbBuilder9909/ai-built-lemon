namespace ProgrammePulse.Services.Integrations.Abstractions;

/// <summary>
/// The outcome of one sync run, in source-neutral terms. Deliberately just
/// the two fields any caller actually consumes today (the overview page's
/// confirmation message and its identity-queue hint) rather than a general
/// counts bag nothing reads — per-source detail still goes to
/// ProgrammeOps_SyncRun.SummaryJson and the audit log, which is where a
/// diagnostic reader looks.
/// </summary>
public sealed record SyncOutcome(string Summary, int UnresolvedPeople);

/// <summary>
/// One integrated source, as everything above the integration boundary sees
/// it. Adding a source (Jira, a calendar, an HR platform) means writing its
/// Bronze client/mapper and one implementation of this interface — no
/// controller, view, status service, entitlement list or dashboard changes.
/// That is Stage 0's exit criterion, and it is what
/// ProgrammePulse.Tests/Architecture/SourceIndependenceTests protects.
///
/// Implementations are thin adapters over the existing per-source sync
/// services (ClickUpSyncService, HubPlannerSyncService); the orchestration
/// contract those already share — one run at a time per (tenant, source) via
/// the database lease, idempotent upserts keyed on (TenantId,
/// ExternalSource, ExternalId), durable run state, audited outcomes — is
/// unchanged and still documented on ClickUpSyncService.
///
/// <see cref="Name"/> is the identity key: it must equal the value that
/// source writes to <c>ProgrammeOps_SyncRun.Source</c> and to
/// <c>ExternalSource</c> on the Silver rows it maps, because the publication
/// header and the provenance trail join on it.
/// </summary>
public interface ISyncSource
{
    /// <summary>Stable key, e.g. "ClickUp". Matches SyncRun.Source and Silver ExternalSource. Never localised.</summary>
    string Name { get; }

    /// <summary>Human label for buttons and the data-sources panel, e.g. "Hub Planner".</summary>
    string DisplayName { get; }

    /// <summary>The <see cref="Models.Tenancy.ProductFeature"/> key a tenant's plan must include to run this sync.</summary>
    string FeatureKey { get; }

    /// <summary>What kinds of canonical data this source supplies.</summary>
    SourceCapabilities Capabilities { get; }

    /// <summary>
    /// Runs a full sync for one tenant. Throws
    /// <see cref="InvalidOperationException"/> with a reader-facing message
    /// when the source is misconfigured or a run is already in flight — the
    /// contract the existing sync services already honour.
    /// </summary>
    Task<SyncOutcome> RunAsync(Guid tenantId, int? triggeredByMemberId, CancellationToken cancellationToken = default);
}
