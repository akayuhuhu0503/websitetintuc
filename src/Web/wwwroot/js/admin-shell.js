(() => {
    document.body.classList.add('cms-js');
    document.addEventListener('invalid', event => {
        for (let parent = event.target.parentElement; parent; parent = parent.parentElement) if (parent instanceof HTMLDetailsElement) parent.open = true;
    }, true);
    const menu = document.querySelector('.cms-menu-toggle');
    const sidebar = document.getElementById('cms-sidebar');
    function closeMenu() { menu?.setAttribute('aria-expanded', 'false'); sidebar?.classList.remove('is-open'); }
    menu?.addEventListener('click', () => { const open = menu.getAttribute('aria-expanded') !== 'true'; menu.setAttribute('aria-expanded', String(open)); sidebar?.classList.toggle('is-open', open); });
    document.addEventListener('keydown', e => { if (e.key === 'Escape' && menu?.getAttribute('aria-expanded') === 'true') { closeMenu(); menu.focus(); } });
    const path = location.pathname.toLowerCase(), query = new URLSearchParams(location.search);
    document.querySelectorAll('[data-admin-nav]').forEach(link => {
        const key = link.dataset.adminNav;
        const active = key === 'weekly' ? path === '/admin' && query.get('categoryId') === '76'
            : key === 'articles' ? (path === '/admin' && query.get('categoryId') !== '76' || /^\/admin\/(edit|create|history)(\/|$)/.test(path))
            : key === 'banners' ? path.startsWith('/admin/site') && ['banners', 'blocks'].includes(query.get('section'))
            : key === 'site' ? path.startsWith('/admin/site') && !['banners', 'blocks'].includes(query.get('section'))
            : key?.startsWith('inbox-') ? path === '/admin/tuong-tac' && (query.get('kind') || 'Comment') === key.slice(6)
            : key && (path === key || path.startsWith(key + '/'));
        if (active) { link.classList.add('active'); link.setAttribute('aria-current', 'page'); const details = link.closest('details'); if (details) details.open = true; }
    });
    const triggers = document.querySelectorAll('[data-picker-target]');
    if (!triggers.length) return;
    const dialog = document.createElement('dialog'); dialog.className = 'cms-picker'; dialog.setAttribute('aria-labelledby', 'cms-picker-title');
    dialog.innerHTML = '<div class="cms-picker-heading"><h2 id="cms-picker-title">Chọn nội dung</h2><button type="button" data-picker-close aria-label="Đóng bộ chọn">Đóng</button></div><form class="cms-picker-search"><label for="cms-picker-query">Tìm theo tên hoặc tiêu đề</label><input id="cms-picker-query" type="search" maxlength="200"><button type="submit">Tìm</button></form><p class="cms-picker-status" role="status" aria-live="polite"></p><div class="cms-picker-results"></div><div class="cms-picker-pages"><button type="button" data-picker-prev>← Trước</button><span data-picker-page></span><button type="button" data-picker-next>Tiếp →</button><button type="button" data-picker-close>Chọn xong</button></div>';
    document.body.append(dialog);
    const search = dialog.querySelector('input'), results = dialog.querySelector('.cms-picker-results'), status = dialog.querySelector('.cms-picker-status');
    let target, trigger, endpoint, page = 1, request;
    async function load() {
        request?.abort(); request = new AbortController(); const current = request;
        status.textContent = 'Đang tìm…'; results.replaceChildren();
        dialog.querySelector('[data-picker-prev]').disabled = true; dialog.querySelector('[data-picker-next]').disabled = true;
        try {
            const url = new URL(endpoint, location.origin); url.searchParams.set('q', search.value); url.searchParams.set('page', String(page)); url.searchParams.set('type', trigger.dataset.pickerType || 'content');
            const response = await fetch(url, { signal: current.signal, headers: { 'Accept': 'application/json' } });
            if (!response.ok || response.redirected) throw new Error('Không đọc được thư viện. Kiểm tra phiên đăng nhập và quyền; nội dung đang nhập vẫn được giữ.');
            const data = await response.json(); if (current !== request) return;
            status.textContent = data.items.length ? 'Chọn một mục bên dưới.' : 'Chưa có kết quả ở trang này. Thử từ khóa khác hoặc trang tiếp theo.';
            for (const item of data.items) {
                const button = document.createElement('button'); button.type = 'button'; button.className = 'cms-picker-item';
                if (item.preview && /^\/admin\/site\/media\/[a-f0-9-]+$/i.test(item.preview)) { const image = document.createElement('img'); image.src = item.preview; image.alt = ''; image.loading = 'lazy'; button.append(image); }
                const label = document.createElement('span'); label.textContent = item.label; button.append(label);
                button.addEventListener('click', () => {
                    let option = Array.from(target.options).find(x => x.value === String(item.value));
                    if (!option) { option = new Option(item.label, item.value); target.add(option); }
                    if (!target.multiple) target.value = String(item.value); else option.selected = true;
                    target.dispatchEvent(new Event('change', { bubbles: true }));
                    status.textContent = 'Đã chọn: ' + item.label;
                    if (!target.multiple) dialog.close(); else { button.setAttribute('aria-pressed', 'true'); }
                }); results.append(button);
            }
            dialog.querySelector('[data-picker-page]').textContent = 'Trang ' + page;
            dialog.querySelector('[data-picker-prev]').disabled = page <= 1; dialog.querySelector('[data-picker-next]').disabled = !data.hasMore;
        } catch (error) { if (error.name !== 'AbortError') status.textContent = error instanceof SyntaxError ? 'Phiên đăng nhập đã thay đổi. Lưu nội dung đang nhập trước khi đăng nhập lại.' : error.message; }
    }
    triggers.forEach(button => button.addEventListener('click', () => {
        target = document.getElementById(button.dataset.pickerTarget); if (!(target instanceof HTMLSelectElement)) return;
        trigger = button; endpoint = button.dataset.pickerEndpoint || '/admin/site/picker'; page = 1; search.value = '';
        dialog.querySelector('h2').textContent = button.dataset.pickerType === 'image' ? 'Chọn ảnh từ thư viện' : 'Chọn bài viết'; dialog.showModal(); search.focus(); load();
    }));
    dialog.querySelector('form').addEventListener('submit', e => { e.preventDefault(); page = 1; load(); });
    dialog.querySelector('[data-picker-prev]').addEventListener('click', () => { page--; load(); });
    dialog.querySelector('[data-picker-next]').addEventListener('click', () => { page++; load(); });
    dialog.querySelectorAll('[data-picker-close]').forEach(button => button.addEventListener('click', () => dialog.close()));
    dialog.addEventListener('close', () => { request?.abort(); trigger?.focus(); });
})();
