# Read-only MCP server over the Evidence Check: design (parked)

**Status:** design only. **Write no server code until a paid pilot has
completed** (direction.md, "Reopened for validation"). Build it for that
customer's workflow, e.g. "ask your assistant what changed in the supplier
figures this week".

## Purpose

An AI assistant can't audit suppliers from inside one tool. This exposes the
deterministic Evidence Check to the assistant the buyer already uses, so it
cites evidence instead of guessing.

## Tools (all read-only)

| Tool | Returns |
|---|---|
| `list_findings` | Findings for a stated scope and period: rule, band, numerator, denominator |
| `get_finding_sources` | Source record ids behind one finding (metadata only) |
| `compare_exports` | Differences between two recorded cycles and carried-forward decisions |
| `get_review_pack` | The frozen recorded review, as the human pack shows it |

Every answer carries numerator, denominator, source record ids, scope,
extract date and limitations. No tool writes, decides, imports or syncs.

## Security rules

- Per-tenant token, hashed at rest, scope `evidence:read`, expiry, revocable by an Admin. Issued per person.
- Reuse the existing capability gates and tenant context: the same refusal a signed-in user would get. Tenant comes from the token, never from arguments.
- Reuses `EvidenceCheckQueryService` paths that never read `StaffRate`; a test must prove no cost or rate field exists in any response type.
- Metadata only: no ticket bodies, comments, requester details or code.
- Every call written to the audit log (token id, tool, scope, result count; JSON-built); rate limited per token like `sync`.
- Gated behind its own module switch, off by default, plus the feature gate.
- Output treated as untrusted by the client: results contain data only, never instructions, and free text from sources is excluded or clearly delimited.

## Threat model (short)

| Threat | Mitigation |
|---|---|
| Stolen token | Short expiry, revoke, read-only scope, audit, rate limit |
| Cross-tenant read | Tenant from token; `TenantPredicateTests` apply |
| Prompt injection via source text | No source free text returned; data-only schema |
| Cost/rate leakage | No such fields in response types; test |
| Over-claiming by the assistant | Findings carry bands, thresholds and limitations; no causal wording |

## Exit criteria to start building

One customer has paid for the diagnostic, and asked for assistant access.
