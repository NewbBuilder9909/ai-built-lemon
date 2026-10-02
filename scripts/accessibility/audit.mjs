// WCAG 2.2 A/AA automated audit (axe-core) at phone, tablet, desktop and
// 320px reflow, signed in as each Northstar persona. See docs/accessibility.md.
//
//   cd scripts/accessibility && npm install && node audit.mjs
//
// Env: BASE (default http://localhost:5080), PERSONAS (comma list), MAX_PAGES,
// PROGRAMMEPULSE_PERSONA_PASSWORD and PROGRAMMEPULSE_PERSONA_TOTP_SECRET (the
// same values the persona seeder used; nothing is hard-coded). Exits 1 on any
// violation or horizontal overflow, so it can gate CI.
//
// Overflow is checked two ways. The page itself must never scroll sideways
// at any width (WCAG 1.4.10). And at laptop and desktop widths (1024px and
// up) no table may be wider than its card: a table that scrolls inside the
// page is a design failure there even though WCAG allows it, and it was the
// most visible defect before the design system was rebuilt.
import { chromium } from 'playwright';
import { readFileSync } from 'node:fs';
import { createHmac } from 'node:crypto';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const AXE = readFileSync(require.resolve('axe-core/axe.min.js'), 'utf8');
const BASE = process.env.BASE ?? 'http://localhost:5080';
const PASSWORD = process.env.PROGRAMMEPULSE_PERSONA_PASSWORD ?? process.env.PERSONA_PASSWORD;
const TOTP_SECRET = process.env.PROGRAMMEPULSE_PERSONA_TOTP_SECRET;
const MAX_PAGES = Number(process.env.MAX_PAGES ?? 60);
const USERS = {
  anon: null,
  dev: 'dev.alex@northstar.test',
  pm: 'pm.sarah@northstar.test',
  admin: 'admin.emma@northstar.test',
  platform: 'platform.operator@programmepulse.test',
};
const PERSONAS = (process.env.PERSONAS ?? Object.keys(USERS).join(',')).split(',');

if (PERSONAS.some(p => USERS[p]) && !PASSWORD) {
  console.error('Set PROGRAMMEPULSE_PERSONA_PASSWORD to the password the persona seeder used.');
  process.exit(2);
}
if (PERSONAS.some(p => ['admin', 'platform'].includes(p)) && !TOTP_SECRET) {
  console.error('Set PROGRAMMEPULSE_PERSONA_TOTP_SECRET to the TOTP secret the persona seeder used (MFA personas).');
  process.exit(2);
}
const VIEWPORTS = { phone: [390, 844], tablet: [768, 1024], desktop: [1366, 900] };
const TAGS = ['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa', 'wcag22aa'];
const SKIP = /(logout|sign-?out|export|download|template|sample|oauth|\/erase|\/delete|\/purge|mailto:)/i;

function totp() {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bits = [...TOTP_SECRET].map(c => alphabet.indexOf(c).toString(2).padStart(5, '0')).join('');
  const key = Buffer.from(bits.match(/.{8}/g).map(b => parseInt(b, 2)));
  const counter = Buffer.alloc(8);
  counter.writeBigUInt64BE(BigInt(Math.floor(Date.now() / 30000)));
  const hmac = createHmac('sha1', key).update(counter).digest();
  const offset = hmac[hmac.length - 1] & 15;
  return String((hmac.readUInt32BE(offset) & 0x7fffffff) % 1e6).padStart(6, '0');
}

async function signIn(page, email) {
  await page.goto(`${BASE}/staffops/account/login`);
  await page.fill('#email', email);
  await page.fill('#password', PASSWORD);
  await Promise.all([page.waitForLoadState(), page.click('button[type=submit]')]);
  if (page.url().includes('/mfa/')) {
    // Codes are single-use per 30s window; wait for a fresh one.
    await new Promise(r => setTimeout(r, (31 - (Date.now() / 1000) % 30) * 1000));
    await page.fill('#code', totp());
    await Promise.all([page.waitForLoadState(), page.click('button[type=submit]')]);
  }
}

const template = p => p.replace(/[0-9a-f]{8}-[0-9a-f-]{27}/g, '{id}').replace(/ERP-\d+/, '{erp}').split('?')[0];
const browser = await chromium.launch();
const failures = [];
const audited = new Set();

for (const persona of PERSONAS) {
  // bypassCSP lets the audit inject axe; the site's CSP is unchanged.
  const context = await browser.newContext({ bypassCSP: true });
  const page = await context.newPage();
  if (USERS[persona]) await signIn(page, USERS[persona]);
  const queue = [USERS[persona] ? '/staffops/home' : '/'];
  if (!USERS[persona]) queue.push('/purchase', '/demo', '/staffops/account/login');
  const visited = new Set();

  while (queue.length && visited.size < MAX_PAGES) {
    const path = queue.shift();
    if (visited.has(path)) continue;
    visited.add(path);
    await page.setViewportSize({ width: 1366, height: 900 });
    try {
      await page.goto(BASE + path);
    } catch {
      continue; // a link that starts a download (or fails) is not a page to audit
    }
    const links = await page.$$eval('a[href]', as => as.map(a => a.getAttribute('href')));
    for (const href of links) {
      if (href?.startsWith('/') && !href.startsWith('//') && !SKIP.test(href) && /^\/(staffops|demo|purchase)/.test(href)) queue.push(href.split('#')[0]);
    }
    const key = template(new URL(page.url()).pathname);
    if (audited.has(key)) continue;
    audited.add(key);

    for (const [name, [width, height]] of Object.entries(VIEWPORTS)) {
      await page.setViewportSize({ width, height });
      await page.goto(page.url());
      await page.addScriptTag({ content: AXE });
      const violations = await page.evaluate(async tags => (await axe.run(document, { runOnly: { type: 'tag', values: tags } }))
        .violations.map(v => `${v.id} (${v.impact}, ${v.nodes.length} el.) e.g. ${v.nodes[0]?.target.join(' ')}`), TAGS);
      violations.forEach(v => failures.push(`[${persona} ${name}] ${key}: ${v}`));
    }
    for (const width of [320, 390, 768, 1024, 1280, 1440]) {
      await page.setViewportSize({ width, height: 800 });
      await page.goto(page.url());
      const overflow = await page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth);
      if (overflow > 2) failures.push(`[${persona} ${width}px] ${key}: page scrolls sideways by ${overflow}px (WCAG 1.4.10)`);
      if (width >= 1024) {
        const wide = await page.evaluate(() => [...document.querySelectorAll('.ops-table-scroll')]
          .filter(w => w.scrollWidth - w.clientWidth > 2)
          .map(w => `${(w.querySelector('caption')?.textContent ?? 'table').trim().slice(0, 60)} (+${w.scrollWidth - w.clientWidth}px)`));
        wide.forEach(t => failures.push(`[${persona} ${width}px] ${key}: table wider than its card: ${t}`));
      }
    }
  }
  await context.close();
}

await browser.close();
console.log(`Audited ${audited.size} distinct pages at 3 viewports plus reflow widths.`);
if (failures.length) {
  console.log(failures.join('\n'));
  process.exit(1);
}
console.log('No automated WCAG 2.2 A/AA violations and no horizontal overflow.');
