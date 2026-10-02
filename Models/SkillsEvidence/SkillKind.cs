namespace ProgrammePulse.Models.SkillsEvidence;

/// <summary>
/// What kind of thing a skill is. Deliberately small and closed: a taxonomy
/// a customer can extend freely stops being comparable across teams, which
/// is the whole point of a coverage view.
///
/// These are the five kinds named in docs/staff-skills-evidence-module.md.
/// Adding a sixth is a taxonomy-version change (see
/// <see cref="SkillTaxonomy.CurrentVersion"/>), not a quiet enum append.
/// </summary>
public enum SkillKind
{
    /// <summary>A programming or markup language — C#, SQL, T-SQL.</summary>
    Language = 0,

    /// <summary>A framework, library or platform — Umbraco, ASP.NET Core, Terraform.</summary>
    Framework = 1,

    /// <summary>A part of this organisation's own product — "Billing", "Sync engine".</summary>
    Component = 2,

    /// <summary>A way of working — code review, incident command, accessibility testing.</summary>
    Practice = 3,

    /// <summary>Subject-matter knowledge — broadcast compliance, GDPR, public-sector procurement.</summary>
    Domain = 4
}
