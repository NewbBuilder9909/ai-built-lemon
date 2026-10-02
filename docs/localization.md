# Languages, formats and markets

Which languages the interface offers — and which number/date formats,
markets, currencies and time zones a tenant may choose — is **deployment
configuration**, not code. English and Welsh are the shipped set; adding
another language is a settings entry plus a translation file.

## Configuration

`appsettings.json` → `Localization` (bound to
`Services/Localization/LocalizationSettings`):

```json
"Localization": {
  "DefaultCulture": "en-GB",
  "UiCultures": [ "en-GB", "cy-GB" ],
  "FormatCultures": [ "en-GB", "cy-GB", "en-IE" ],
  "UseBrowserLanguage": true,
  "Markets": [ "GB", "IE" ],
  "Currencies": [ "GBP", "EUR", "USD" ],
  "TimeZones": [ "Europe/London", "Europe/Dublin", "Etc/UTC" ]
}
```

| Setting | Meaning |
|---|---|
| `DefaultCulture` | Used when neither the member's choice nor their browser matches. Must appear in both lists below. |
| `UiCultures` | Interface languages offered in the switcher, in display order. |
| `FormatCultures` | Number/date/currency formats, chosen independently of language (a Welsh-language user can keep `en-GB` formatting). |
| `UseBrowserLanguage` | On first visit, pick the best match from the browser's `Accept-Language`. The member's explicit choice always wins. |
| `Markets`, `Currencies`, `TimeZones` | What a tenant may choose in executive review market settings. Currencies are ISO 4217; time zones are IANA IDs. |

Like every setting, these can be overridden per environment
(`Localization__UiCultures__2=fr-FR`) without editing the file.

**Invalid entries stop startup** with a message naming the problem (an
unknown culture, a default that isn't offered, a malformed currency, a time
zone the server doesn't know). A language that is offered but has **no
translation file** is a startup *warning*: it works, but every screen shows
English.

## Adding a language (e.g. French)

1. Add `"fr-FR"` to `UiCultures` (and to `FormatCultures` if French number
   and date formatting should be selectable).
2. Copy `Resources/SharedResource.cy-GB.resx` to
   `Resources/SharedResource.fr-FR.resx` and replace each `<value>` with the
   French text. A partial file is fine — any key it lacks falls back to
   English — but it must not contain keys the English file lacks (a test
   fails the build on those).
3. Build and run. The switcher lists "Français" automatically.

Right-to-left languages (Arabic, Hebrew, Urdu…) need nothing extra to be
offered: every page sets `dir="rtl"` from the current culture, and the shared
stylesheet positions everything by reading direction (`inline-start`/`end`),
so the signed-in layout mirrors — navigation on the right, tables reading
right to left. `RightToLeftStylesheetTests` fails the build if a physical
`left`/`right` rule creeps back in (one such rule once widened every Arabic
page to 2,365px).

Checked on 2026-09-26 with `ar-SA` switched on in configuration only: the
layout mirrors correctly. What still looks wrong is **untranslated English
inside a right-to-left page** — sentence punctuation jumps to the start, and
dates and "50 min ago" read in the wrong order. That goes away with a real
translation file; don't offer a right-to-left language to a customer before
one exists and a native speaker has reviewed the screens.

## How a request's language is chosen

1. The culture cookie, set when the member picks a language
   (`/language?culture=…`, `Controllers/LanguageController`).
2. Otherwise, if `UseBrowserLanguage` is on, the browser's `Accept-Language`,
   matched by **language**, not exact tag
   (`Services/Localization/BrowserLanguageRequestCultureProvider`): `cy` finds
   `cy-GB`, `fr-CA` finds `fr-FR`. Formatting keeps the browser's own region
   when it is offered (`en-US` dates for an American browser, even though the
   interface is `en-GB`).
3. Otherwise `DefaultCulture`.

There is deliberately no query-string provider: a link must never change
someone's language.

## What is translatable today

26 of 79 views read their text through `@Localizer[...]`: the signed-in
navigation and chrome, sign-in, access denied, the language switcher, the
Reporting Hub (with RAID, Governance, Trend, Alerts and Cost), Executive
Review and the operations demo. Everything else — including the Programme
Overview, My Work, Delivery load, Contracts, Skills and Admin — is
English-only until its text is moved into the resource file. Extend it one
area at a time: wrap each string in `@Localizer["..."]` and add the key to
`SharedResource.resx` (English) and to each translation file.

Welsh covers every key in the English file. It has not had a professional
review; see `docs/commercial/claim-register.md` before claiming Welsh (or any
language) as supported.
