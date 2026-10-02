namespace ProgrammePulse.Services.Shared;

/// <summary>
/// A caller-supplied foreign key (ProgrammeKey, ProjectKey, WorkstreamKey,
/// WorkItemKey, CustomerKey, ...) belongs to a different tenant than the one
/// making the write, or doesn't exist at all. Deliberately the same message
/// shape for "wrong tenant" and "doesn't exist" — callers turn this into
/// NotFound(), and a foreign key from another tenant must be
/// indistinguishable from one that was never real, same principle as
/// StaffAdminController.GetTenantStaffAsync applies to primary keys.
/// </summary>
public sealed class CrossTenantReferenceException(string entityType, Guid key)
    : InvalidOperationException($"{entityType} {key} was not found.")
{
    public string EntityType { get; } = entityType;

    public Guid Key { get; } = key;
}
