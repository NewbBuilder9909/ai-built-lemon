# Brand, website and buyer journey

27 September 2026. Decision record. The offer, price and claims are
unchanged; see [`paid-diagnostic-offer.md`](paid-diagnostic-offer.md),
[`first-sale-playbook.md`](first-sale-playbook.md) and
[`claim-register.md`](claim-register.md).

## Decisions

1. **Candidate brand: Assayer.** An assayer tests metal independently of
   its maker and the hallmark records the result, which is the product's
   neutrality position in one word. Galileo's *The Assayer* (1623) argued
   for testing claims against evidence rather than authority. It is a
   candidate, not a cleared name (see below).
2. **The website is a separate repository:
   `NewbBuilder9909/Assayer`.** It is a static site with no cookies, no
   analytics, no third-party requests and no client-side JavaScript. It
   shares no code or hosting with this product. The name appears only in
   its `src/site.config.ts`, so a failed clearance is a one-line change.
3. **This product keeps its internal name.** `Commercial:ProductName` stays
   "Delivery Evidence Check" until clearance is confirmed.
4. **The buyer journey is staged.** The website sells the diagnostic
   through a call and a proposal. The self-service trial
   (`Commercial:SelfServiceTrialEnabled`) stays off, as B4 decided, until
   the demand and safety conditions in the website repository's
   `docs/launch-plan.md` are both met.

## Name search, 27 September 2026

A preliminary web search only, with no trade mark register searched.

| Candidate | Result |
|---|---|
| Plumbline | Plumbline Consulting sells Microsoft Dynamics apps to professional-services and project-based firms, the same buyer |
| Proofline | An automated reconciliation platform that "surfaces exceptions", the same concept |
| Outturn | Inoapps Outturn Planning, project financial status software on G-Cloud |
| Onus | A crypto investment app with millions of users, and a programming language about trust |
| Countersign | E-signature software |
| Provenant | Several software companies, including a business management platform |
| Plimsoll | Plimsoll Publishing, UK company financial analysis |
| Evidra / Evidara | Several AI compliance and verification products |
| Candour, Probity, Rigour | Crowded with UK and international software firms |
| Fair Witness | Adtech (fairwitness.com) and an LLM framework |
| **Assayer** | **No software product found.** An unrelated `ASSAYER LIMITED` (Companies House 09773781, Wembley, 2015) exists; its business wasn't checked |

## Before the name is used with a buyer

- UK IPO and EUIPO searches in classes 9, 42 and 35.
- Check what `ASSAYER LIMITED` does. While it exists, the company can't be
  registered as "Assayer Ltd"; trade under the existing company or use a
  variant such as "Assayer Software Ltd".
- ~~Register the domain.~~ Done: `assayerhq.com` (27 September 2026).
  `assayer.com` is taken; `assayer.info` is available but unsuitable as a
  primary, because .info scores badly with spam filters and outreach
  depends on email.
- File a UK trade mark in classes 9 and 42 if the search is clean.

The website enforces this: its production build fails until each item is
confirmed in `src/site.config.ts`.

## Journey by stage

| Stage | Starts when | Demo | Purchase | Trial |
|---|---|---|---|---|
| 1 | Name cleared, domain and email live | Static sample check on the website, and the live sample walkthrough on the call | Proposal and invoice, 50% on signature | None: the sample check is the trial |
| 2 | First paid diagnostic delivered | Hosted read-only sample workspace (B9) | Stripe Payment Link for the deposit | None |
| 3 | Three paid, one monthly repeat, trial requested, release gates closed | Self-service, own data | Stripe Checkout for the monthly review | `SelfServiceTrialEnabled` on |

The website's sample check shows this product's own sample export
(`DeliveryExportSample`), with five of six people linked. Its figures were
derived by following `EvidenceCheckCalculator` and are marked provisional
until regenerated from a real export with the website's
`npm run demo:import`. If a rule or the sample changes here, regenerate it
there.
