# Skills and evidence: what we may claim, and what we may not

21 September 2026. Slices 1–4 are built; **no connector has run against a
live vendor account.** Read the build status in
[`staff-skills-evidence-module.md`](staff-skills-evidence-module.md)
before quoting anything here in front of a customer.

## The claim

> Programme Pulse combines reviewed staff skills with optional repository
> contributions and support trends to help teams find expertise, plan
> cover and improve services.

Three words in that sentence are doing the work. **Reviewed** — a human
set the level and a human agreed it. **Optional** — a customer can buy
and use the skills matrix, the repository evidence and the service health
view independently, and the plan features are separate for exactly that
reason. **Help** — the product surfaces and organises; people decide.

## What we must not claim

Each of these is prevented by something in the code, not only by this
document. The mechanism is named so a salesperson who is asked "how do
you know?" has an answer.

| Never claim | Why it would be false | What prevents it |
|---|---|---|
| Commit evidence proves who wrote the code | Anyone can write a commit naming a colleague as author or co-author; only a signature the provider verified shows who made a commit | `EngineeringEvidence.AuthorshipVerified`; commit rows without it show "authorship not verified" (`CommitAuthorshipTests`) |
| We measure developer value or productivity | Commit volume, lines changed and ticket counts are poor proxies for the value of someone's work | `StaffEvidencePortfolio` has no score, rank or rating member; `EvidenceContractTests` fails the build if one is added |
| We prove code quality | Quality is judged in context — design, tests, maintainability, incident follow-through, peer feedback — not counted | There is no quality field anywhere in the evidence contract |
| We identify who caused a support case | A matching issue key means two records mention each other | Only `SupportLinkMethod.ConfirmedRootCause`, which requires a named reviewer and a rationale, may be described as causal; `SupportCodeLink` has nowhere to record an author or blame target |
| Skills are detected automatically | A language hint means "participated in changes to files classified this way", not "is proficient" | No evidence service may reference `ISkillAssertionService`; activity cannot raise a level even by accident |
| The product suggests proficiency levels | A suggestion proposes an *Awareness-level, unreviewed* claim and nothing more, and is refused outright if the person already has a record for that skill | `SuggestionService.AcceptSkillTagAsync`, pinned by `SuggestionTests.Accepting_can_never_raise_an_existing_level` |
| Confidence scores tell you how likely a suggestion is to be right | The bands are threshold counts over evidence, not a calibrated model. A percentage would be fabricated precision | `SuggestionConfidence` is a three-value enum; a test fails the build if a score- or probability-shaped member appears on `Suggestion` |
| We predict individual performance | Nothing here forecasts anything about a person | No model, no scoring, no ranking exists in the product |
| It tells you who to let go, promote or reassign | Employment decisions need context the product does not have | `CoverageActionType` offers only NominateBackup, Pair, DocumentRunbook, Training and Investigate — there is no value expressing an employment decision |

## Things to say carefully

**"Key-person risk."** Correct phrasing: *this component depends on one
reviewed person with no approved backup.* Incorrect: anything implying
the person is the problem. The finding is about the organisation's
exposure and reads identically whoever holds the component.

**"Coverage."** Always paired with its denominator and its review date.
A coverage figure without "as at" and "how many people said nothing" is
a number that will be over-read.

**"Evidence."** Means participation observed in a connected source during
a stated window. If the window is partial or accounts are unmapped, the
product says so on the same page, and so should we.

**"Automatic."** Only the *collection* is automatic. Every judgement —
proficiency, identity, root cause, ownership — is a person's, recorded
with their name against it.

## What a demo must disclose

- Whether the data is a fixture. Until a tenant installation has
  completed a clean run, `IsUnverifiedReplay` is true and the UI carries
  a banner; do not screenshot around it.
- That repository evidence requires the customer to record a lawful
  basis, worker notice and DPIA decision before it will collect anything
  at all — this is a gate in the software, not a recommendation.
- That the skills matrix works with nothing connected, and is the part we
  can stand behind today without a vendor validation.

## Before a customer enables person-level collection

The pre-enablement check at `/staffops/skills/continuity/processing`
walks the list. It separates what was observed in their data, what is
structural in the product, and what only they can answer. It is
deliberately not a score: "no check currently fails" is not the same as
"you are ready", and the page says so.
