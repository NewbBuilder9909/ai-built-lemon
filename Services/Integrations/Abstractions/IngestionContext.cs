namespace ProgrammePulse.Services.Integrations.Abstractions;

/// <summary>
/// Which sync run and which tenant-owned source connection is currently
/// writing — the provenance a mapper stamps onto every canonical row it
/// upserts, so a management conclusion can later be traced back to the
/// evidence that produced it (the staged brief's "preserve provenance"
/// principle).
///
/// Scoped per request, and therefore per sync run: a sync always runs for
/// exactly one tenant, from one connection, under one run. This mirrors the
/// existing IStaffIdentityResolver, which is scoped for the same reason and
/// documents the same assumption.
///
/// Note the deliberate asymmetry with tenantId. Repository methods take
/// tenantId as an explicit parameter and never read it from ambient state,
/// because tenantId is a security *filter* — see IProgrammeRepository. The
/// values here are payload, not a filter: they are written onto rows, never
/// used to decide which rows a caller may see. Getting them wrong produces a
/// wrong audit trail, not a tenant leak. That is why ambient scoping is
/// acceptable here and is not acceptable for tenantId.
///
/// Unset (<see cref="IsActive"/> false) is a legitimate state: a row written
/// outside a sync — admin-authored, or by a future importer — honestly has
/// no run to point at, and the provenance columns stay null rather than
/// being filled with a fiction.
/// </summary>
public interface IIngestionContext
{
    bool IsActive { get; }

    Guid? ConnectionKey { get; }

    Guid? SyncRunKey { get; }

    DateTime? IngestedAtUtc { get; }

    void Begin(Guid connectionKey, Guid syncRunKey, DateTime ingestedAtUtc);
}

public sealed class IngestionContext : IIngestionContext
{
    public bool IsActive { get; private set; }

    public Guid? ConnectionKey { get; private set; }

    public Guid? SyncRunKey { get; private set; }

    public DateTime? IngestedAtUtc { get; private set; }

    public void Begin(Guid connectionKey, Guid syncRunKey, DateTime ingestedAtUtc)
    {
        ConnectionKey = connectionKey;
        SyncRunKey = syncRunKey;
        IngestedAtUtc = ingestedAtUtc;
        IsActive = true;
    }
}
