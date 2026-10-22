"use strict";
(() => {
    const toggle = document.querySelector('.mobile-menu-toggle');
    const links = document.querySelector('#primary-links');
    toggle?.addEventListener('click', () => {
        const expanded = toggle.getAttribute('aria-expanded') === 'true';
        toggle.setAttribute('aria-expanded', String(!expanded));
        links?.classList.toggle('is-open', !expanded);
    });
    const menus = Array.from(document.querySelectorAll('.nav-dropdown'));
    menus.forEach(menu => menu.addEventListener('toggle', () => {
        if (menu.open)
            menus.forEach(other => { if (other !== menu)
                other.open = false; });
    }));
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape')
            return;
        const opened = menus.find(menu => menu.open);
        if (opened) {
            opened.open = false;
            opened.querySelector('summary')?.focus();
        }
        else if (toggle?.getAttribute('aria-expanded') === 'true') {
            toggle.click();
            toggle.focus();
        }
    });
    document.addEventListener('click', event => {
        if (!(event.target instanceof Node))
            return;
        if (!menus.some(menu => menu.contains(event.target)))
            menus.forEach(menu => { menu.open = false; });
    });
    const body = document.querySelector('.article-detail .article-body');
    let fontSize = 18;
    const feedback = document.querySelector('.reading-feedback');
    document.querySelectorAll('[data-font-size]').forEach(button => button.addEventListener('click', () => {
        fontSize = button.dataset.fontSize === 'reset' ? 18 : Math.max(16, Math.min(24, fontSize + (button.dataset.fontSize === 'increase' ? 2 : -2)));
        if (body)
            body.style.fontSize = fontSize + 'px';
        if (feedback)
            feedback.textContent = 'Cỡ chữ ' + fontSize + ' pixel.';
    }));
    document.querySelector('[data-print-article]')?.addEventListener('click', () => window.print());
    const publicUrl = () => document.querySelector('link[rel=canonical]')?.href || location.href;
    async function copyLink() { try {
        await navigator.clipboard.writeText(publicUrl());
        if (feedback) {
            feedback.classList.remove('sr-only');
            feedback.textContent = 'Đã sao chép liên kết bài đang công khai.';
        }
    }
    catch {
        if (feedback) {
            feedback.classList.remove('sr-only');
            feedback.textContent = 'Không thể truy cập clipboard. Sao chép địa chỉ từ thanh địa chỉ trình duyệt.';
        }
    } }
    document.querySelector('[data-copy-link]')?.addEventListener('click', () => void copyLink());
    document.querySelector('[data-share-article]')?.addEventListener('click', () => { if (navigator.share)
        void navigator.share({ title: document.querySelector('h1')?.textContent || '', url: publicUrl() }).catch(() => { });
    else
        void copyLink(); });
    // Wrap source tables only in the rendered DOM. Original revision HTML stays unchanged.
    body?.querySelectorAll('table').forEach(table => { const wrapper = document.createElement('div'); wrapper.className = 'article-table-scroll'; wrapper.tabIndex = 0; wrapper.setAttribute('role', 'region'); wrapper.setAttribute('aria-label', 'Bảng trong bài viết; cuộn ngang để xem đủ cột'); table.before(wrapper); wrapper.append(table); });
})();
