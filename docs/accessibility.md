# Accessibility, devices and report sharing

Target: **WCAG 2.2 level AA** on phones, tablets and desktops.

## Where it stands (2026-09-27)

`scripts/accessibility/audit.mjs` signs in as each Northstar persona, crawls
every page it can reach, and runs axe-core's WCAG 2.0/2.1/2.2 A and AA rules at
phone (390px), tablet (768px) and desktop (1366px), then checks for horizontal
scrolling at 320px, 390px, 768px, 1024px, 1280px and 1440px (WCAG 1.4.10
Reflow). From 1024px up it also fails any table wider than its card: WCAG
allows a table to scroll inside the page, but on a laptop it is a design
failure, and it was the most visible defect before the design system was
rebuilt (28 September; see [design-system.md](design-system.md)).

| | Before | After |
|---|---|---|
| Colour contrast (1.4.3) | 316 elements on 38 pages | 0 |
| Scrollable table not keyboard-reachable (2.1.1) | 10 pages | 0 |
| Form inputs without labels (1.3.1, 4.1.2) | Branding uploads | 0 |
| Touch target under 24px (2.5.8) | Branding uploads on phone | 0 |
| Sideways scrolling at 320–390px (1.4.10) | 4 pages | 0 |
| Sideways scrolling on tablet | 1 page | 0 |
| Status shown by colour only (1.4.1) | ~24 tables | bar + screen-reader text |
| Tables scrolling inside their card at 1440px (28 Sep) | most report pages, up to 2,848px | 0 |

Keyboard walk-through on a phone: skip link → menu button → page actions,
every stop with a visible focus ring (2.4.7); the menu opens from the keyboard
and exposes language and Log out.

### What automated checks cannot prove

Automated rules catch roughly a third of WCAG failures. **Do not claim WCAG
2.2 AA conformance** on the strength of this — see
`docs/commercial/claim-register.md`. Not yet done:

- Screen-reader passes (NVDA + Firefox, VoiceOver on iOS and macOS) of the
  sign-in, Programme Overview, Reporting Hub, import and leave journeys.
- Manual review of content-level criteria: meaningful link text, error
  identification and suggestions on every form (3.3.1, 3.3.3), consistent
  help (3.2.6), redundant entry (3.3.7), accessible authentication (3.3.8)
  for the TOTP step.
- 200% zoom and text-spacing (1.4.4, 1.4.12) checks page by page.
- The public `/purchase` page was scanned but its interactive ROI calculator
  has not been walked with assistive technology.
- Right-to-left layouts mirror correctly but have not been reviewed by a
  native reader (`docs/localization.md`).

## How the fixes work (keep them working)

- **Contrast lives in a few CSS colours**, not per page: `--ops-text-muted`,
  the sidebar section label, and the teal accents in `purchase.css`. Check any
  new colour at 4.5:1 for text (3:1 for large text and UI parts). The rail's
  text colours are computed from the published secondary colour
  (`SidebarPalette`, tested across the colour cube), so a tenant's branding
  can't make the menu unreadable.
- **Scrollable tables**: `app.js` makes an `.ops-table-scroll` focusable,
  with `role="region"` and the table's caption as its name, *only when it
  actually overflows*, and re-checks on resize. Give every table a
  `<caption>` (visually hidden is fine).
- **Flagged rows** (`.ops-table__row--overdue`) get a left bar (a non-colour
  cue that also survives greyscale printing) and a hidden "Flagged:" prefix.
- **Narrow screens**: navigation collapses into the menu button at 900px
  (phones and portrait tablets); language and Log out move inside the menu;
  tables become labelled cards at 640px; headline figures are two across on
  phones. Flex form fields may shrink (`min-width: 0`), so no control can
  widen the page.
- **Logical CSS only** (`inline-start`/`end`) — `RightToLeftStylesheetTests`
  fails the build otherwise, for `charts.css` as well as `app.css`.
- **Charts** (`charts.css`, 30 September) never carry information alone: a
  bar is hidden from assistive technology only where its numbers are written
  beside it or in a table on the page (the milestone timeline has a
  `role="img"` summary and a table twin), status colour always comes with
  its words, and `print-color-adjust: exact` keeps bars in print. Not yet
  covered by an audit run: the audit needs Node, and the charts have not
  been walked with a screen reader.

## Sharing reports

Report pages set `ViewData["ReportActions"] = true` (and optionally
`ViewData["ExportUrl"]`) to show `Views/StaffOps/_ReportActions.cshtml`:

- **Print or save as PDF** — the print stylesheet strips navigation, buttons
  and forms, prints full tables with the header row repeated on each page, and
  adds a header with organisation, generation time (UTC) and the page's
  address including its filters. Tables of nine or more columns print more
  compactly; orientation is left to the print dialog.
- **Copy link** — copies the address with the page's filters.
- **Download CSV** — the Reporting Hub and Programme Overview exports are
  sectioned CSVs, UTF-8 with a byte-order mark (Excel opens £ and Welsh
  correctly), with spreadsheet formulas neutralised. The Programme Overview
  export (`/staffops/programme/export`) leads with provenance: generation
  time, sources and their freshness, and the unmatched-people count.

**There is deliberately no public or unauthenticated share link.** A copied
link still requires the recipient to sign in with a role that can see the
page, so sharing never bypasses tenant isolation, role checks, the cost-data
rule or the audit trail. A time-limited, read-only public link would be a
product and data-protection decision (what data, who approves, expiry,
revocation, logging), not a UI change.

Enabled on: Programme Overview, Reporting Hub, RAID, Governance, Trend,
Alerts, Cost Summary, Estimate calibration, Delivery load, Commercial
Overview and My Work.

## Running the audit

```bash
# Choose the persona credentials once, and use them for both the seeding run
# and the audit. Nothing is hard-coded; the TOTP secret is any Base32 string.
export PROGRAMMEPULSE_PERSONA_PASSWORD='<a password you choose>'
export PROGRAMMEPULSE_PERSONA_TOTP_SECRET='<a Base32 secret you choose>'

# with the site running and the Northstar personas seeded under those values
cd scripts/accessibility
npm install
node audit.mjs          # exits 1 on any violation or horizontal overflow
```

The script refuses to start without the password, and without the TOTP secret when an
MFA persona (admin, platform) is included. The Northstar demo loader takes the same
`PROGRAMMEPULSE_PERSONA_TOTP_SECRET`; see [demo-data.md](demo-data.md).

Set `BASE` to audit another environment, `PERSONAS=pm,admin` to narrow it.
