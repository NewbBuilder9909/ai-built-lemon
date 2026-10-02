using System.Text.RegularExpressions;

namespace ProgrammePulse.Models.Programme;

/// <summary>
/// A code repository, identified the way repository evidence identifies it:
/// provider, the account or organisation it lives under, and the
/// repository key within that account ("owner/name" on GitHub,
/// "project/repository" under an Azure DevOps organisation). The account is
/// part of the identity because two Azure DevOps organisations can both
/// hold "web/api".
///
/// Plain strings on purpose: Programme Ops must not reference the evidence
/// area's types (SourceIndependenceTests, FeatureAreaDependencyTests), the
/// same rule as SupportCodeLink.
/// </summary>
public sealed partial record CodeRepositoryRef(string Provider, string SourceAccountId, string RepositoryKey)
{
    /// <summary>
    /// A normalised reference, or null with a reason a person can act on.
    /// Account and key are compared case-insensitively everywhere, so they
    /// are stored lower-case; the provider keeps its spelling for display.
    /// </summary>
    public static CodeRepositoryRef? TryCreate(string? provider, string? sourceAccountId, string? repositoryKey, out string? error)
    {
        var trimmedProvider = provider?.Trim();
        var account = sourceAccountId?.Trim();
        var key = repositoryKey?.Trim().Trim('/');

        error = trimmedProvider is null || !ProviderName().IsMatch(trimmedProvider)
            ? "Choose the repository's provider."
            : account is null || !Segment().IsMatch(account) || account is "." or ".."
                ? "Enter the account or organisation the repository lives under."
                : !IsValidKey(key)
                    ? "Enter the repository as two parts separated by '/', for example owner/name or project/repository."
                    : null;

        return error is null
            ? new CodeRepositoryRef(trimmedProvider!, account!.ToLowerInvariant(), key!.ToLowerInvariant())
            : null;
    }

    /// <summary>
    /// For display and gap matching. A GitHub key already starts with its
    /// owner ("acme/api" under "acme"), so the account is shown only when the
    /// key doesn't carry it (Azure DevOps: "org/project/repo").
    /// </summary>
    public string DisplayName => RepositoryKey.StartsWith(SourceAccountId + "/", StringComparison.OrdinalIgnoreCase)
        ? $"{Provider}: {RepositoryKey}"
        : $"{Provider}: {SourceAccountId}/{RepositoryKey}";

    public bool SameRepositoryAs(CodeRepositoryRef other) =>
        string.Equals(Provider, other.Provider, StringComparison.OrdinalIgnoreCase)
        && string.Equals(SourceAccountId, other.SourceAccountId, StringComparison.OrdinalIgnoreCase)
        && string.Equals(RepositoryKey, other.RepositoryKey, StringComparison.OrdinalIgnoreCase);

    private static bool IsValidKey(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        var parts = key.Split('/');
        return parts.Length == 2 && parts.All(part => Segment().IsMatch(part) && part is not ("." or ".."));
    }

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]{1,31}$")]
    private static partial Regex ProviderName();

    // Letters, digits and the punctuation repository and organisation names
    // use; never a slash, wildcard or anything that reshapes a URL.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._ -]{0,99}$")]
    private static partial Regex Segment();
}

/// <summary>
/// An Admin's declaration that a repository's code serves a project, and
/// through it that project's programme, customer and contracts. Declared,
/// never inferred: a wrong link would move effort estimates and contractual
/// obligations onto the wrong customer (docs/delivery-evidence-and-contract-assurance.md).
///
/// Many-to-many: a shared library can serve several projects, and then
/// carries every one of their customers' obligations. A link is ended, not
/// deleted, so "which customer did this repository serve in March" stays
/// answerable.
/// </summary>
public sealed record CodeRepositoryLink
{
    public required Guid LinkKey { get; init; }

    public required Guid TenantId { get; init; }

    public required CodeRepositoryRef Repository { get; init; }

    public required Guid ProjectKey { get; init; }

    /// <summary>Optional context, e.g. "shared UI library; also serves Contoso".</summary>
    public string? Note { get; init; }

    public Guid? LinkedByStaffKey { get; init; }

    public required DateTime LinkedAtUtc { get; init; }

    public DateTime? RemovedAtUtc { get; init; }

    public Guid? RemovedByStaffKey { get; init; }

    public bool IsLive => RemovedAtUtc is null;
}
