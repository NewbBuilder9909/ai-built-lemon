# Design-partner validation: protocol and known failure modes

21 September 2026.

**No design-partner validation has been run.** This is the protocol for
one, plus the false positives the code is already known to produce,
derived from its actual behaviour rather than from guesswork. Slice 4's
brief asks for sample attribution and support links to be validated with
a design partner; that work needs a partner, a live connector and real
data, and none of the three exists yet.

## Why this cannot be desk-checked

Every failure mode below was reproduced as a fixture, so the code handles
it deliberately. What fixtures cannot tell us is **how often each occurs
in a real repository**, and that frequency is the whole question. A
squash-merge false positive that happens twice a year is a footnote; one
that happens on every merge makes the committer role useless. Only a
partner's data answers that.

## Known false positives and unknowns

| Case | What the product does | What is still unknown |
|---|---|---|
| **Squash merge** | Author and committer are separate rows; the committer gets no language hints, because applying a squash is not participating in the files | How often the customer's merge queue makes a bot the committer, and whether the author row survives their settings |
| **Pair programming** | Read from `Co-authored-by` trailers only, never inferred | Whether the partner's teams actually use the trailer. If they do not, pair work is invisible and will be under-credited |
| **Force push / rebase** | The rewritten commit is ingested under its new SHA and the old one is kept — both are counted | How much double-counting this causes on a team that rebases routinely. We cannot distinguish a rewrite from new work, and guessing would be worse |
| **Inherited code** | Nothing attributes ownership from history; ownership is declared by a manager | Whether managers actually maintain the map, or let it go stale — the coverage view reports staleness, but cannot fix it |
| **Renamed accounts** | Mapping is keyed on the provider's stable account id, not the login | Whether the partner has accounts that pre-date stable ids, or bot accounts misclassified as people |
| **Trailer-only co-authors** | Queued under a synthetic `email:` key an admin can approve once | Whether the same human appears under several addresses, inflating the queue |
| **Non-code support demand** | Cases with no link to source are counted and reported as unlinked; `SourceLinkCoverage` shows the share | What proportion of real demand is unlinkable. If it is most of it, the code-linking feature is decoration |
| **Unknown component tags** | Counted, kept, shown in their own row, never assigned to a guessed component | Whether the partner's tagging is consistent enough for the component view to mean anything |
| **Custom desk statuses** | An unrecognised Freshdesk status maps to Active with the raw value preserved | Which statuses the partner actually uses. This is the single most likely mapping error |
| **Deleted and reopened tickets** | Deletions become Withdrawn and are excluded from trends; reopens are first-class | Whether the partner's desk exposes `reopened_at` reliably |

## The protocol

**1. Sample, do not survey.** Take 30 artefacts spanning: a squash
merge, a rebase, a pair-programmed change, a change to inherited code, a
bot PR, a co-authored commit, and ten ordinary ones. Take 30 support
cases spanning: a reopen, a deletion, an unlinked case, a case with an
issue key, an unknown component tag, and a case unrelated to code.

**2. Ask the person, not the data.** For each person-level attribution,
ask the named individual whether it is right. The failure this catches —
work credited to the wrong colleague — is invisible to anyone else.

**3. Record every disagreement**, including ones where the product was
right and the human was wrong. That direction matters too: it tells you
where the UI is unclear rather than where the data is wrong.

**4. Test the correction flow, not just the data.** Have the subject
raise a challenge on a skill and an admin revoke an actor link. Time how
long each takes and whether the person understood what to do. A
correction route nobody can find is the same as not having one.

**5. Confirm the Freshdesk field mapping explicitly** — which field
carries the product/component tag, and every custom status code. This is
convention-based today and is the likeliest source of silently wrong
service figures.

## What to record

For each sample: artefact, what the product said, what the human said,
agree or disagree, and — if disagree — which failure mode above it
matches, or a new one. Publish the disagreement rate per category rather
than an overall accuracy figure: "94% accurate" hides that every
pair-programmed change was wrong.

## Exit

The validation passes when the partner's own engineers accept the
attribution on their own work, the unlinkable share of support demand is
known and stated, and the component vocabulary is agreed. Until then,
`IsUnverifiedReplay` stays true and the UI keeps saying so.
