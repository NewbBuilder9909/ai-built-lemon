(() => {
  const tools = [...document.querySelectorAll('.tool input')];
  const deployments = [...document.querySelectorAll('input[name="deployment"]')];
  const quotePlans = [...document.querySelectorAll('#trial-signup-form input[name="plan"]')];
  const quoteModules = [...document.querySelectorAll('#trial-signup-form input[name="selectedModules"]')];
  const planOrder = { Starter: 0, Professional: 1, Enterprise: 2 };
  const number = id => Math.max(0, Number(document.getElementById(id)?.value) || 0);
  const money = value => new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP', maximumFractionDigits: 0 }).format(value);
  const hours = value => `${new Intl.NumberFormat('en-GB', { maximumFractionDigits: 0 }).format(value)} hours`;
  const text = (id, value) => { const el = document.getElementById(id); if (el) el.textContent = value; };

  const calculateRoi = () => {
    const rate = number('roi-rate');
    const cycles = number('roi-cycles');
    const baselineHoursPerCycle = number('roi-people') * number('roi-hours');
    const assistedHoursPerCycle = number('roi-assisted');
    const releasedPerCycle = baselineHoursPerCycle - assistedHoursPerCycle;
    const currentAnnualCost = baselineHoursPerCycle * rate * cycles;
    const annualHoursReleased = releasedPerCycle * cycles;
    const grossValue = annualHoursReleased * rate;
    const realisedValue = grossValue * Math.min(100, number('roi-realisation')) / 100;
    const adoptionCost = number('roi-adoption') * rate;
    const yearOneCost = number('roi-fee') + (number('roi-monthly') * 12) + adoptionCost;
    const net = realisedValue - yearOneCost;

    text('roi-current', money(currentAnnualCost));
    text('roi-hours-released', hours(annualHoursReleased));
    text('roi-value', money(realisedValue));
    text('roi-cost', money(yearOneCost));
    text('roi-net', money(net));

    let verdict;
    if (releasedPerCycle <= 0) {
      verdict = 'This releases no time at all once correction and review are counted. There is no time-based case here — do not buy on one.';
    } else if (net <= 0) {
      verdict = `Year one is ${money(Math.abs(net))} short on a time-only basis. Either the scope is too small, the fee is too high, or the value is somewhere other than preparation time. Ask us to narrow the scope or quote differently.`;
    } else if (net < yearOneCost * 0.25) {
      verdict = 'Year one is marginally positive on a time-only basis, which is too thin to survive a wrong assumption. Buy only if a specific discrepancy is also worth resolving.';
    } else {
      verdict = 'Year one is positive on modelled capacity. That is a reason to run the diagnostic and measure the real figures — not yet a proven return.';
    }

    text('roi-verdict', verdict);
    return { currentAnnualCost, annualHoursReleased, realisedValue, yearOneCost, net, verdict };
  };

  const refreshQuote = () => {
    const selectedPlan = quotePlans.find(x => x.checked);
    if (!selectedPlan) return;

    quoteModules.forEach(module => {
      const requiredPlan = module.dataset.modulePlan || '';
      const enabled = !requiredPlan || (planOrder[selectedPlan.value] ?? 0) >= (planOrder[requiredPlan] ?? 0);
      module.disabled = !enabled;
      if (!enabled) {
        module.checked = false;
      }
    });

    const selectedModules = quoteModules.filter(x => x.checked && !x.disabled);
    const monthly = Number(selectedPlan.dataset.planMonthly || 0)
      + selectedModules.reduce((sum, x) => sum + Number(x.dataset.moduleMonthly || 0), 0);
    const setup = Number(selectedPlan.dataset.planSetup || 0)
      + selectedModules.reduce((sum, x) => sum + Number(x.dataset.moduleSetup || 0), 0);

    text('trial-plan-name', `${selectedPlan.value} trial quote`);
    text('trial-monthly', money(monthly));
    text('trial-setup', money(setup));
    text('trial-features', selectedModules.length
      ? `Selected add-ons: ${selectedModules.map(x => x.value).join(', ')}`
      : 'No paid add-ons selected yet. The plan price below covers the default feature set.');
  };

  const summary = () => {
    const chosen = tools.filter(x => x.checked);
    const boundary = deployments.find(x => x.checked)?.value || 'Not selected';
    const roi = calculateRoi();
    refreshQuote();
    const sourceText = chosen.length ? chosen.map(x => x.value).join(', ') : 'To be confirmed';
    text('selection', `Sources: ${sourceText}`);
    text('deployment', `Boundary: ${boundary}`);
    text('readiness', chosen.some(x => x.dataset.status === 'validation')
      ? 'One or more selected connectors remain under live compatibility validation; begin with exports where necessary.'
      : 'Connector and data availability will be verified before any connected pilot.');
    text('roi-summary', `Modelled year-one net position: ${money(roi.net)} (capacity, not cash), against ${money(roi.yearOneCost)} of year-one cost.`);

    return [
      'Weekly delivery evidence diagnostic enquiry',
      `Sources: ${sourceText}`,
      `Preferred boundary: ${boundary}`,
      '',
      'Buyer-entered figures (unverified, from the browser calculator):',
      `  Current annual preparation cost: ${money(roi.currentAnnualCost)}`,
      `  Hours released per year, net of correction and review: ${hours(roi.annualHoursReleased)}`,
      `  Realised capacity value: ${money(roi.realisedValue)} — capacity, not cash`,
      `  Year-one cost of buying: ${money(roi.yearOneCost)}`,
      `  Year-one net position: ${money(roi.net)}`,
      `  Read: ${roi.verdict}`,
      '',
      'Review required: one recurring weekly review, its decision owner, source definitions, permitted data and a representative period.',
      'No credentials or personal data are included in this enquiry.'
    ].join('\n');
  };

  const emailLink = document.getElementById('email-plan');
  const enquiryAddress = document.querySelector('.actions')?.dataset.enquiryEmail || '';
  const refresh = () => {
    const value = summary();
    if (emailLink && enquiryAddress) {
      emailLink.href = `mailto:${encodeURIComponent(enquiryAddress)}?subject=${encodeURIComponent('Weekly delivery evidence diagnostic')}&body=${encodeURIComponent(value)}`;
    }
  };

  [...tools, ...deployments, ...document.querySelectorAll('#roi-calculator input'), ...quotePlans, ...quoteModules].forEach(x => x.addEventListener('input', refresh));
  document.getElementById('copy-plan')?.addEventListener('click', async () => {
    try {
      await navigator.clipboard.writeText(summary());
      text('copy-status', 'Review brief copied.');
    } catch {
      text('copy-status', emailLink ? 'Clipboard unavailable. Use Open in email instead.' : 'Clipboard unavailable. Select the brief text above and copy it manually.');
    }
  });

  refresh();
})();
