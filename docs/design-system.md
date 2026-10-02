# Design system

**28 September 2026; charts added 30 September.** Every signed-in page shares
one stylesheet, `wwwroot/css/app.css`, one chart stylesheet,
`wwwroot/css/charts.css`, and one script, `wwwroot/js/app.js`. There is no CSS
framework or chart library. This page says what the rules are, so a new page looks as if it
belongs rather than as if it was added later.

It was rebuilt because the previous version failed at the basics: most report
tables scrolled sideways inside their card even at 1440px (the Reporting Hub
by 897px, the audit log by 2,181px), pages ran to 5,000px of tables with no
sense of what mattered, and every link was a filled button. The measurements
before and after are in [accessibility.md](accessibility.md).

## Rules

1. **A page answers one question first.** Put what needs acting on at the
   top (the Programme Overview's *Needs attention* panel), then the summary,
   then detail. Pipeline plumbing (sources, sync buttons, record-level
   diagnostics) goes at the foot of the page or behind a disclosure.
2. **Tables wrap; numbers don't.** Body cells wrap at word boundaries, so
   a narrow column never splits "Approved" in two. Long unbroken tokens
   (ids, URLs, emails, JSON) may break anywhere, and live in `code`, links or
   `.ops-break`, so one of them cannot set a table's width. `app.js` marks
   columns that are at least four-fifths numeric `.is-num` (right-aligned,
   tabular figures, one line) and date columns `.is-nowrap`; a view can mark
   a cell `.num` itself. Headers wrap.
3. **No table wider than its card from 1024px up.** The audit fails it. If a
   table will not fit, remove columns before shrinking text: fold context
   into the main cell with `.ops-cell-context` (the programme and project
   under a workstream), merge related columns (from/to into dates), and move
   row actions behind a `Manage` disclosure (`.ops-manage`).
4. **Long tables show ten rows.** Any table with 16 or more body rows shows
   its first ten and a *Show all* control (`app.js`). Flagged rows are never
   hidden, printing restores every row, and `data-collapse="off"` opts out.
5. **One filled button per decision.** `button.ops-button` is primary and
   submits something. A link styled as a button is secondary automatically;
   `.ops-button--primary` opts a link back in when it is the page's main
   call to action. Related pages are navigation, not buttons: use a section
   sub-nav (`.ops-subnav`, see `Views/StaffOps/Reporting/_ReportingNav.cshtml`)
   or a text link row (`.ops-link-row`). "Back to…" is `.ops-back-link`.
6. **Quiet text is roman.** `.ops-empty-state` (explanations and empty
   states) and `.ops-page-lede` (one sentence under the title) are muted, not
   italic. User-facing copy never names a source file, a config key the
   customer cannot set, or an internal type: "see docs/…" belongs in a
   comment.
7. **Report actions live in the page header.** Set
   `ViewData["ReportActions"] = true` and, for a CSV, `ViewData["ExportUrl"]`;
   do not add a separate Export button to a filter bar.
8. **Never show an identifier.** Enum values go through
   `DisplayText.Words` (`TimeAndMaterials` → "Time and materials"), stored
   JSON through `DisplayText.Fields` (labelled values, ids shortened), and
   no page names a test, a table, a type or a config key. The built-in
   readiness checks on the Processing page were the worst case: a data
   protection officer was reading test class names.
9. **Row actions are quiet.** A button inside a table cell is small and
   secondary; the row's one decision (Approve, Link) adds
   `.ops-button--primary`; anything irreversible adds `.ops-button--danger`.
   A column form's submit button keeps its own width.
10. **No inline styles.** Use a class. The only exception is a value that is
    data, such as a progress width or a brand colour, passed as a custom
    property (`style="--swatch: #2554c7"`).
11. **Status colour is never the only signal.** Flagged rows carry a left bar
   and screen-reader text; alert KPI cards (`.ops-kpi-card--alert`, or
   `NeedsAttention` on `KpiCardViewModel`) carry a red rule and a red value.
12. **A figure has a denominator.** "3 blocked" can't be judged; "3 of 67 open
   items" can. Give a KPI card `Numerator`, `Denominator` and a
   `DenominatorKey` (a resource string such as "of {0} open items") and it
   draws the share as a thin meter.
13. **Draw what is being judged, and only that.** Charts live in
   `wwwroot/css/charts.css` (see *Charts* below) and follow the rules there:
   the figure is always written beside the mark, readiness is a band and never
   a score or gauge, status colour comes with its words, and nothing charts
   named people. Lists of people are sorted by name, never by load: sorted by
   how much someone carries, the bottom of the list reads as a ranking
   ([delivery-load.md](delivery-load.md)).

## Tokens

All colours are CSS custom properties on `:root`, each falling back to a
Branding Ops `--brand-*` token, so a tenant's published branding reaches every
page. The platform defaults (`Models/Branding/PlatformDefaultTheme.cs`) are a
slate rail (`#0f172a`), a blue accent (`#2554c7`, 6.6:1 on white) and a
`#f4f6f9` page. Text colours: `--ops-text`, `--ops-text-muted` (7.6:1),
`--ops-text-subtle` (6.0:1, the faintest allowed). Check any new colour at
4.5:1 for text and 3:1 for large text and UI parts.

Spacing is `--ops-space-1` to `--ops-space-8` (4px to 32px); type sizes are
`--ops-font-size-xs` to `--ops-font-size-base` (12px to 15px). Content is
capped at 1,400px wide.

## Components

| Class | Use |
|---|---|
| `.ops-panel`, `.ops-panel__header`, `.ops-panel__meta` | A section card; the header holds the heading and one link or note |
| `details > .ops-panel__summary` | A whole panel that folds, its heading as the summary |
| `.ops-kpi-row`, `.ops-kpi-card` | Headline figures; flat when inside a panel |
| `.ops-attention`, `.ops-attention__item--high/--medium` | "Needs attention" items, each with the figure behind it |
| `.ops-source-strip`, `.ops-dot` | Data freshness on one line |
| `.ops-subnav` | Section tabs; wraps, never scrolls |
| `.ops-finding`, `.ops-finding__decision` | A finding that opens to its source records; on a recorded review the row also shows its decision and owner, and the decision form lives inside |
| `.ops-record` | An action or decision in a list |
| `.ops-inline-form`, `.ops-manage` | Row actions in a table cell |
| `.ops-decision-form` | A decision in one row: fields, then its buttons. Two outcomes that share a reason (Validate, Reject) are one form with a second button carrying `asp-action` |
| `.ops-field-hint` | A short qualifier inside a label, such as a default |
| `.ops-checklist`, `.ops-step` | A short list of guarantees; a numbered step marker |
| `.ops-grid-2` | Two panels side by side when there is room (30rem each) |

## Charts

Added 30 September 2026, from the
[design and PMO data review](design-and-pmo-data-review-2026-09-30.md).
`wwwroot/css/charts.css` is loaded by the layout and by the board pack, which
doesn't load `app.css`, so every value in it has a fallback. Charts are
server-rendered HTML with widths passed as data (`style="--share: 12.5%"`,
rule 10); there is no chart library and no script. `print-color-adjust:
exact` keeps the bars in a printed or PDF report.

| Class or partial | Use |
|---|---|
| `Programme/_ReadinessBand.cshtml` (`.ops-band`) | The three readiness bands with the current one marked and each band's rule stated. Never a dial, score or percentage |
| `Programme/_ProjectBands.cshtml` (`.ops-project-bands`, `.ops-readiness`) | Readiness per project beside the portfolio band, so a "Use with caveats" headline can't hide projects that are not decision-ready |
| `Programme/_FindingShare.cshtml` (`.ops-share`) | A finding's affected share of its own total; high-severity evidence gaps carry the 10% line that decides readiness |
| `.ops-share--thin` | A meter under a figure (KPI cards, pack figures) |
| `.ops-movement` | Records cleared (left of the line), still present and new between two recorded reviews, on one scale per table |
| `.ops-stack`, `.ops-stack-rows` | Parts of a count on a shared scale, such as overdue items by days late, split by whether anyone is named |
| `Programme/_MilestoneTimeline.cshtml` (`.ops-timeline`) | Dated milestones on one axis with today marked; a past-due item whose status can't be read is a hollow diamond, never a "late" dot |
| `.ops-legend`, `.ops-key` | The key for a chart's colours |

Every chart is hidden from assistive technology only when the same numbers
are written beside it or in a table on the page; otherwise it carries a
`role="img"` summary. Colours are tokens (`--ops-viz-*`, `--ops-band-*`)
with dark-theme values, and were checked with a palette validator for the
lightness band, colour-blind separation and contrast on white and on the
dark surface. The amber "caveats" colour is below 3:1 on white by design,
which is why it always appears with its words.

## Checking a change

After changing a view, run the view type-check (`./scripts/check-views.ps1`,
or `dotnet build ProgrammePulse.csproj -p:CheckRazorViews=true` without
PowerShell) and the audit (`scripts/accessibility/audit.mjs`) against a site
with the Northstar demo loaded ([demo-data.md](demo-data.md)), so every table
has realistic volume in it.
