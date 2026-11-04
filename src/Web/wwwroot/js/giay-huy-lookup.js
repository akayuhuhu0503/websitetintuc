(() => {
    const form = document.querySelector('[data-lookup-filter]');
    if (!(form instanceof HTMLFormElement)) return;
    form.querySelector('[name="year"]')?.addEventListener('change', () => {
        const province = form.querySelector('[name="province"]');
        if (province instanceof HTMLSelectElement) province.value = '';
        form.requestSubmit();
    });
    form.addEventListener('submit', () => {
        form.setAttribute('aria-busy', 'true');
        const status = form.querySelector('[role="status"]');
        if (status instanceof HTMLElement) status.hidden = false;
    });
    window.addEventListener('pageshow', () => {
        form.removeAttribute('aria-busy');
        const status = form.querySelector('[role="status"]');
        if (status instanceof HTMLElement) status.hidden = true;
    });
})();
