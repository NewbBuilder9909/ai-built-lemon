(() => {
    const action = document.getElementById('Action');
    if (!action) return;
    const update = () => {
        document.querySelectorAll('[data-decision-fields]').forEach(group => {
            const selected = group.dataset.decisionFields === action.value;
            group.hidden = !selected;
            group.querySelectorAll('input, select, textarea').forEach(field => {
                field.disabled = !selected;
                field.required = selected;
            });
        });
    };
    action.addEventListener('change', update);
    update();
})();
