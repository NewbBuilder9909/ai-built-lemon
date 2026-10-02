using ProgrammePulse.Models.Staff;

namespace ProgrammePulse.Services.Staff;

/// <summary>
/// A feature area that holds personal data keyed by StaffKey outside the
/// Staff domain, and must therefore take part in a subject access request
/// (export) and erasure. GdprService takes every registered participant,
/// so the Staff domain never has to depend on the areas above it —
/// ProgrammeOps registers IdentityLinkDataParticipant for the explicit
/// identity links (source-tool user ids and emails) it stores.
/// </summary>
public interface IStaffDataParticipant
{
    /// <summary>Label for the export section, e.g. "Identity links".</summary>
    string Section { get; }

    Task<IReadOnlyList<GdprExportLinkedRecordRow>> ExportAsync(Guid staffKey);

    Task EraseAsync(Guid staffKey, DateTime nowUtc);
}
