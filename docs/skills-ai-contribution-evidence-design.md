# Skills and AI-assisted contribution evidence

26 September 2026. Isolated implementation branch: reviewed contribution links
and self-declared AI assistance are implemented; no live vendor integration has
been exercised. See [delivery handoff](skills-ai-contribution-delivery.md).

## Product decision

Extend the staff skills portfolio with source-linked examples of human work in
AI-assisted delivery. The user selected **skills + AI-assisted contribution
evidence**, rather than a direct LinearB integration or a team productivity
dashboard as the first outcome.

The questions are: what can this person demonstrate, what part did they play,
where did a tool assist, and what did a reviewer actually validate? This builds
on the existing reviewed skills matrix and repository evidence. AI involvement
neither proves proficiency nor invalidates a person's contribution.

## Existing foundations and gaps

| Available in the code | Addition needed |
|---|---|
| Append-only skill assertions, four proficiency levels, review and challenge | Structured links from an assertion revision to chosen evidence and an explanation of demonstrated behaviour |
| Commit author, committer, co-author, PR author and reviewer roles | Artefact-level AI observations with an explicit source and workflow |
| Approved external-account mapping and bot exclusion | Keep AI agents as tool context; do not map them to staff or discard their relevance to human contributions |
| Source URLs, language hints and connector coverage | A portfolio showing role, assistance provenance, coverage and uncertainty together |
| Capacity/utilisation reporting based on recorded time and availability | Later, a read-only context panel using the existing reporting rules |

The existing source documentation records fixture validation and outstanding live
connector/design-partner validation. This increment does not change that status.

## A concrete portfolio experience

For a staff member, show three sections using the same selected period:

1. **Reviewed skills.** C# at Practitioner, who reviewed it, review due date,
   and the examples the reviewer accepted. AI-assisted testing, output
   verification and agent task design can be separately declared practice
   skills using the existing rubric; installing a tool does not grant them.
2. **Contribution examples.** Source PR/commit/review link, the person's exact
   role, the recorded AI tool/workflow, and how that assistance was reported.
   Example: “Reviewed PR 142. Code-generation assistance reported by provider.
   Demonstrated checking failure cases and correcting an unsafe retry.” The last
   sentence is a human assessment with its own author, not generated certainty.
3. **Capacity context.** Available hours, recorded hours, planned commitments
   and missing-data status. For example, 37.5 baseline hours minus 7.5 leave gives
   30 available; 24 recorded hours gives 80% recorded utilisation. This says
   nothing about the usefulness of those hours or the remaining unrecorded work.

An illustrative matrix cell reads “Practitioner · reviewed 12 Sep · 3 linked
examples · review due 12 Sep next year”. Opening it shows those examples and
their assistance provenance. Counts help navigation; they never set a level.

Do not convert commits, PR duration, accepted suggestions or tool tokens into
worked hours. A coding timestamp is not a timesheet. Pairing, support, design,
mentoring and review remain real work when no commit is produced.

## Evidence model

**Artefact identity:** tenant + connection + provider + source account + repository
+ source type + external id. A SHA or PR number by itself is not a safe join.
Each person's participation remains a separate edge with its existing role.
Grouping those edges for display prevents an author/committer from appearing as
two separate pieces of work. A PR, its reviews and its commits are different
artefacts until a provider supplies an explicit relationship; do not infer a
relationship from timing, names or matching numeric IDs.

**AI observation:** an artefact-bound positive claim carrying tool name,
workflow, signal kind, source-record identifier and observation timestamp.
Supported provenance categories for the foundation are developer declaration,
commit trailer and provider-reported. Keep multiple tools and their sources.
A trailer supports “assistance reported in commit metadata”; it does not prove
what fraction of the work was generated, or that every participant used AI.
If a source does not state the workflow, use Unspecified rather than inventing
code-generation or review activity.

Missing observations mean **unknown**, never “manual”. A repository rule file
shows configuration, not use on each commit. A provider's daily user usage
cannot be joined to a commit solely because the date and person coincide.
Provider-provided “manual” classifications should retain the provider's own
definition and coverage if integrated later.

**Skill evidence link:** tenant, subject, assertion revision,
evidence key, demonstration note, recorded-by identity and recorded-at time.
The linked source supplies the contribution date; connector coverage states the
observed period. A reviewer accepts or rejects relevance with a rationale;
the normal skill assertion workflow remains the only way to validate a level.
Make corrections append-only, retain previous decisions, and remove personal
records through the existing export/erasure participant when persistence lands.
Pending, disputed and withdrawn links do not silently become validated evidence.

The foundation's projector accepts already-approved identity attribution and
current observations. It does not authenticate callers, verify declarations,
store correction history or authorize a reviewer. Those are explicit integration
requirements, not capabilities implied by a passing unit test.

## LinearB reference and optional adapter

LinearB documents identifying AI involvement through commit co-author metadata,
AI comments/PR authors and supported tool integrations. This supports a
metadata-provenance approach; it does not establish that a missing marker proves
no AI use. [LinearB detection FAQ](https://linearb.helpdocs.io/article/ww1ciql9uw-linear-b-ai-insights-faq).

Its Measurements V2 API supports Git and AI-tool metrics, including workflow
grouping; its stated limitations exclude PM velocity, investment profile and
time distribution. The documentation describes export access for Business and
Enterprise customers. It therefore is not a substitute for this solution's
capacity/time records. [Measurements V2](https://docs.linearb.io/api-measurements-v2/).

The product emphasis here is reviewed skill development and expertise coverage,
with checkable examples of human judgement in AI-assisted work. LinearB's
delivery/AI analytics can be an optional source of context, not a prerequisite.

If a customer already has LinearB, begin with team-level contextual imports.
Before importing person/artefact observations, verify that the licensed endpoint
actually returns stable artefact identifiers, connection/repository identity,
timestamps and provenance at that granularity. Aggregate metrics must not be
expanded into fabricated commit links. Version the mapping and preserve the
upstream metric definition, units, window, coverage and last successful sync.
No LinearB account, credentials or live endpoint was exercised for this work.

## Delivery and collision boundaries

Worktree: `<local>/MyUmbraco18Project-skills-ai`.
Branch: `feature/skills-ai-evidence`, based on `02b5a33`.
Claude's active working tree was left untouched. Once Phase 2 was committed as
`d7b2459`, it was merged into this isolated branch in `42a46ce` and the combined
suite passed. No feature changes have been merged back into the main branch.

**Implemented:** provider-neutral observation contracts and a pure projector;
an append-only contribution-review table; subject submission, correction and
withdrawal; reviewer acceptance/rejection with rationale; a paged examples list
and revision-detail page. A self-declared tool/workflow stays a declaration even
after relevance is accepted. No path changes a skill proficiency or status.
The pages carry current connector coverage and suppress links to no-longer-
eligible sources. Source metadata ingestion remains unchanged.

The new controller and views are separate from the existing portfolio. The
only portfolio edits are navigation links; DI wiring, an append-only migration
step, and the route snapshot are the other integration points. None of Claude's
modified source files were copied from its working tree.

**Following slice:** enrich observations from existing GitHub/Azure DevOps
metadata using explicit artefact relationships. Preserve bot signals as tool
context while keeping bots out of person attribution. Do not retain prompts,
chat transcripts or source bodies to implement this feature.

**Later context:** read utilisation through the committed Phase 2 interfaces;
add a LinearB adapter only after validating the available customer data.
Do not start background infrastructure, cancellation plumbing or repository
rewrites in this branch.

## Acceptance examples

- The same SHA in another tenant, connection or repository cannot annotate a
  person's contribution.
- Unmapped accounts and bots cannot appear as named staff contributions.
- A person with author and committer roles gets one artefact with both roles.
- Two tools on a PR do not turn it into two delivered items.
- An AI code-generation signal on a PR does not say the human reviewer used
  that tool; their role remains Reviewer.
- Missing signals leave contribution evidence visible with unknown assistance.
- Late-discovered metadata annotates the contribution's original period;
  observation time remains visible separately.
- No skill level, utilisation, productivity rank, time saved or causal AI uplift
  is derived by the projector.

Future live acceptance also needs revoked mappings, observation retraction,
source deletion, squash/force-push relationships, denied access, erasure,
partial ingestion and a design-partner review of representative examples.

## Validation

The initial projector passed its focused and existing skills tests. The
persistent increment adds SQL-backed lifecycle, isolation, concurrency, erasure,
paging and real signed-in HTTP checks. Current run results and reproducible
commands are recorded in the delivery handoff, rather than treating the earlier
foundation-only checks as evidence for the web/database implementation.
