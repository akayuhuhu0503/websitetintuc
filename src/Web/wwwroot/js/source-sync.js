(() => {
    const status = document.getElementById('sync-feedback');
    const results = document.querySelector('[data-sync-results]');
    let refreshing = false;
    async function refresh() {
        if (!results || refreshing || document.hidden) return;
        refreshing = true;
        try {
            const url = new URL(location.href); url.searchParams.set('fragment', 'true');
            const response = await fetch(url, { credentials: 'same-origin', cache: 'no-store' });
            if (response.ok && !response.redirected) results.innerHTML = await response.text();
        } catch { /* Keep the last real result; do not report a successful check. */ }
        finally { refreshing = false; }
    }
    document.addEventListener('submit', async event => {
        const form = event.target;
        if (form instanceof HTMLFormElement && form.id === 'sync-filter') {
            event.preventDefault();
            const url = new URL(location.href);
            for (const [key, value] of new FormData(form)) url.searchParams.set(key, value);
            url.searchParams.delete('page'); history.replaceState(null, '', url); await refresh(); return;
        }
        if (!(form instanceof HTMLFormElement) || !form.matches('[data-sync-form]')) return;
        event.preventDefault();
        const button = form.querySelector('button'); if (button?.disabled) return;
        const data = new FormData(form); if (button) button.disabled = true;
        if (status) status.textContent = 'Đang xử lý yêu cầu…';
        try {
            const response = await fetch(form.action, { method: 'POST', body: data, credentials: 'same-origin' });
            if (response.redirected) throw new Error('Phiên đăng nhập đã hết hạn. Đăng nhập lại rồi thử lại.');
            const result = response.headers.get('content-type')?.includes('json') ? await response.json() : {};
            if (!response.ok) throw new Error(result.message || 'Chưa thực hiện được. Kiểm tra quyền hoặc tải lại trang.');
            if (status) status.textContent = result.message;
            if (typeof result.redirect === 'string' && /^\/admin\/edit\/[a-f0-9-]{36}$/i.test(result.redirect)) location.assign(result.redirect);
            else await refresh();
        } catch (error) { if (status) status.textContent = error.message || 'Mất kết nối. Chưa xác nhận thao tác thành công.'; }
        finally { if (button) button.disabled = false; }
    });
    document.addEventListener('click', async event => {
        const link = event.target.closest?.('[data-sync-results] .pagination a');
        if (!link || event.ctrlKey || event.metaKey || event.shiftKey || event.button !== 0) return;
        event.preventDefault(); history.replaceState(null, '', link.href); await refresh();
    });
    if (results) window.setInterval(refresh, 10000);
})();
