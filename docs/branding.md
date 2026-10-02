# Branding Ops: design-token admin & runtime theme resolution

A single-tenant branding admin area at `/staffops/branding`. Admins edit a
controlled set of design tokens (colours, fonts, radius, layout style,
logo/favicon) rather than raw CSS or HTML — the token set is closed, so a
branding change can never inject CSS or script into the site.

This is deliberately **not** multi-tenant. This site serves one
organisation; the original brief this was built from described a
multi-tenant SaaS platform (`Tenant`, `TenantBrandingProfile`, per-tenant
isolation), but nothing else in this codebase is multi-tenant, so that
isolation layer was left out rather than grafted on unrelated to everything
else here. If the product ever needs to serve more than one organisation,
that's a deliberate follow-up project, not an extension of this one.

## How branding resolves per request

`Services/BrandingOps/BrandingThemeResolverService` (`IBrandingThemeResolverService`)
is the single entry point:

1. `GetActiveThemeAsync()` checks an `IMemoryCache` entry (`branding:active-theme`,
   10-minute absolute expiry as a safety net) before touching the database.
2. On a cache miss, it loads the current **Published** `BrandingProfile` via
   `IBrandingRepository.GetActivePublishedAsync()`.
3. If none exists (nothing has ever been published) or the published profile
   is marked inactive, it falls back to `Models/Branding/PlatformDefaultTheme`
   — constants matching the existing `--ops-*` palette in
   `wwwroot/css/app.css`, so the fallback looks like the app
   already does rather than an invented palette.
4. Either way, the token values are mapped to a fixed set of `--brand-*` CSS
   custom properties by `BrandingCssVariableMapper` and returned as a
   `ResolvedBrandingTheme`.

The navigation rail sits on the secondary colour, so its text tokens
(`--brand-sidebar-text`, `-muted`, `-active`, `-hover-bg`, `-active-bg`,
`-rule`) are derived from that colour by `SidebarPalette`: light or dark
text by WCAG contrast, the softened shade only while it still reaches
4.5:1. The content text colour deliberately does not reach the rail; it is
chosen for the light page, and on a dark rail it would be unreadable.

`StaffBrandingController.ThemeCss` (`GET /staffops/branding/theme.css`) turns
that into a `:root { --brand-...: ...; }` stylesheet. It's linked from
`Views/StaffOps/_Layout.cshtml` with `?v=` set to
`ResolvedBrandingTheme.Version` (a hash of the values), so a publish changes
the address and no cache can keep serving the old colours. It is the only
feature area currently wired to consume it. It's intentionally **not** gated by `IsAdminAsync()` —
serving already-published colour/font values isn't a privileged operation;
only the mutating endpoints are admin-only.

`InvalidateCache()` is called explicitly by the controller after a publish
or rollback — the absolute expiry above is a safety net, not the primary
invalidation path.

## How the token set is structured (and why it's closed)

Every token on `Models/Branding/BrandingProfile` is one of three shapes:

- **Hex colour** (`PrimaryColour`, `SecondaryColour`, `AccentColour`,
  `TextColour`, `SurfaceColour`, `BackgroundColour`) — validated against
  `^#[0-9a-fA-F]{6}$` by `BrandingValidationService` before it's ever saved.
- **Clamped integer** (`BorderRadiusPx`, 0–32) — range-checked the same way.
- **Closed enum** (`BrandingFontOption`, `BrandingHeaderStyle`,
  `BrandingFooterStyle`) — `BrandingCssVariableMapper.ResolveFontStack`
  maps each `BrandingFontOption` value to a hardcoded, literal CSS
  font-stack string. Header/footer style are exposed on `ResolvedBrandingTheme`
  for the layout to apply as a class/data-attribute, not as a CSS variable.

Free text (`CompanyName`, `TenantUiLabel`) never reaches CSS output — it's
only ever rendered through Razor, which HTML-encodes it automatically.

This is what makes "no arbitrary CSS/JS injection" true as a mechanism, not
just a stated goal: nothing that reaches `BrandingCssVariableMapper.Map`
originated as unvalidated free text.

Contrast (WCAG relative-luminance ratio between text and surface/background)
is checked but only ever added as a **warning**, not a hard validation
failure — see `BrandingValidationService.CheckContrast`. Hard-blocking a
save on contrast would be a product call for a later phase, not something
assumed here.

## Where config and assets are stored

- `BrandingOps_Profile` — one row per version. Lifecycle is
  `Draft → Published → Archived` (the `status` column) with
  `effectiveFromUtc`/`effectiveToUtc` closing out superseded rows — the same
  append-only pattern as `StaffOps_StaffRate`. There is no separate
  "ThemeVersion" table; a version *is* a `BrandingOps_Profile` row.
  - At most one `Draft` row exists at a time. Saving a draft when one
    already exists updates it in place; publishing promotes it to
    `Published` (archiving whatever was previously `Published`) and there is
    no longer a `Draft` row until an admin starts editing again.
  - Rolling back doesn't mutate historical rows — it archives the current
    `Published` row and inserts a **new** `Published` row cloned from the
    target version's token values, so every row that was ever live stays an
    immutable, distinct history entry.
- `BrandingOps_Asset` — logo/favicon/hero metadata (dimensions, content
  type, size, storage path). The file itself lives on disk under
  `wwwroot/media/branding/{assetKey}.{extension}` — never under the
  uploaded filename; see the next section for why.
- `BrandingOps_AuditLog` — a straight clone of `ProgrammeOps_AuditLog`'s
  shape, logging `DraftSaved`, `Published`, `RolledBack`, and
  `AssetUploaded` with actor, timestamp, and a JSON detail blob.

All three tables are created by the `BrandingOps` migration plan
(`Migrations/BrandingOps/`), which runs independently of the `StaffOps` and
`ProgrammeOps` plans on every startup — safe to re-run, tracked by plan name
via `IKeyValueService`.

## How asset uploads are validated

`Services/BrandingOps/BrandingAssetStorageService` (`IBrandingAssetStorageService`)
is the only thing that touches the filesystem for branding assets:

1. Content-type allowlist per asset type — PNG/JPEG for logo and hero, PNG
   only for favicon. **No SVG** — an SVG can carry `<script>`/event-handler
   content, which would reintroduce exactly the injection risk the token
   system exists to avoid.
2. A size cap per asset type (2MB logo, 512KB favicon, 4MB hero).
3. The file is decoded with `SixLabors.ImageSharp` (already a transitive
   dependency of `Umbraco.Cms` — no new package was added for this) and
   **re-encoded** to disk rather than the raw uploaded bytes being saved
   verbatim. A file with a spoofed extension, or a polyglot file with
   non-image data appended after valid image bytes, fails to decode and is
   rejected outright rather than being written to disk as-is.
4. Real pixel dimensions are captured from the decoded image (not trusted
   from any client-supplied value) and checked against a sane 16–4096px
   range.
5. The stored filename is always `{assetKey:N}.{extension}`, where
   `assetKey` is server-generated (`Guid.NewGuid()`) and `extension` comes
   from the *decoded* image format — the uploaded filename is kept only as
   display metadata on the `BrandingOps_Asset` row and never used to build
   a path.

A failed validation raises `BrandingAssetValidationException`, which
`StaffBrandingController` catches and turns into a message shown back to
the admin rather than a 500.

## Where admin permissions are enforced

Reuses the existing `StaffRole.Admin` Member Group and
`IStaffAuthorizationService.IsAdminAsync()` — no new role was added, and no
Platform-Admin/Tenant-Admin split (not meaningful for a single-tenant site).
Every mutating action in `StaffBrandingController` (`SaveDraft`, `Publish`,
`Rollback`, `UploadLogo`, `UploadFavicon`) checks `IsAdminAsync()` as the
first line of the method body and returns `Forbid()` if it fails — not via a
filter or attribute, matching `StaffAdminController`'s established pattern
in this codebase. `theme.css` is the one deliberate exception (see above).

## How to preview and publish branding changes

1. Edit the settings form at `/staffops/branding` and click **Save draft**
   — this validates and upserts the single in-progress `Draft` row.
   Validation failures re-render the form with the submitted (invalid)
   values and the error list, rather than redirecting and losing the input.
2. Click **Preview draft** to see the draft's resolved tokens rendered on a
   standalone page (`StaffBrandingController.Preview`) without touching the
   cached live theme other users see.
3. Click **Publish draft** to promote it — this archives whatever was
   previously published and invalidates the theme cache immediately.
4. **Version history** lists every `Published`/`Archived` version; rolling
   back to an archived one creates a new `Published` row cloned from its
   token values (see the lifecycle note above) and also invalidates the
   cache.

## Known gaps / deliberate scope cuts

- **Dark mode** is an org-wide Admin choice (the `DarkModeEnabled` checkbox,
  published once for everyone — single-tenant, not a per-user preference),
  and there's still no auto-derived dark palette: the Admin's own
  BackgroundColour/SurfaceColour/TextColour choices *are* the dark palette
  when the flag is on. What changed: the flag used to be stored and carried
  through `ResolvedBrandingTheme` but never actually read anywhere — `<html>`
  now carries `data-theme="dark"`/`"light"` from it
  (`Views/StaffOps/_Layout.cshtml`), and `app.css` uses that
  to switch `color-scheme` (native form controls/scrollbars follow) and no
  longer hardcodes input backgrounds to white, which would otherwise stay
  white against a dark `BackgroundColour`. Still no true end-user override
  toggle — building one would mean maintaining a second palette per user,
  which is exactly the auto-derivation work this phase deliberately cut.
- **Asset storage is local disk**, not blob storage. Fine for a
  single-instance deployment; would need revisiting if the app ever scales
  to multiple instances/containers, since `wwwroot/media/branding` wouldn't
  be shared between them.
- **Contrast is advisory, not enforced** — see above.
