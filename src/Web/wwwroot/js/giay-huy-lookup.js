(() => {
    const form = document.querySelector('[data-lookup-filter]');
    if (!(form instanceof HTMLFormElement)) return;
    document.querySelectorAll('.lookup-expand').forEach(button => {
        const text = document.getElementById(button.getAttribute('aria-controls'));
        if (!text) return;
        const measure = () => { button.hidden = button.getAttribute('aria-expanded') !== 'true' && text.scrollHeight <= text.clientHeight + 1; };
        measure();
        void document.fonts.ready.then(measure);
        if (typeof ResizeObserver !== 'undefined') new ResizeObserver(measure).observe(text);
        button.addEventListener('click', () => {
            const expanded = button.getAttribute('aria-expanded') !== 'true';
            button.setAttribute('aria-expanded', String(expanded));
            button.textContent = expanded ? 'Thu gọn' : 'Xem thêm';
            text.classList.toggle('is-expanded', expanded);
        });
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
