(() => {
    const form = document.querySelector('[data-lifecycle-form]');
    if (!form) return;
    const operation = form.elements.Operation;
    const target = form.elements.TargetKey;
    const reason = form.elements.Reason;
    const update = () => {
        const targeted = ['RedactDecision', 'WithdrawPack'].includes(operation.value);
        target.disabled = !targeted;
        target.required = targeted;
        form.querySelector('[data-lifecycle-target]').hidden = !targeted;
        const reasons = targeted ? ['PersonalData', 'IncorrectEvidence']
            : operation.value === 'ApplyRetention' ? ['RetentionPolicy']
            : operation.value === 'PurgeTenantData' ? ['TenantOffboarding'] : [];
        for (const option of reason.options) {
            option.disabled = option.value !== '' && !reasons.includes(option.value);
            option.hidden = option.disabled;
        }
        if (!reasons.includes(reason.value)) reason.value = reasons.length === 1 ? reasons[0] : '';
    };
    operation.addEventListener('change', update);
    update();
})();
