(() => {
    const section = document.querySelector('[data-credential-once]');
    if (!section) return;
    const value = section.querySelector('[data-sensitive-value]');
    const toggle = section.querySelector('[data-credential-toggle]');
    const copy = section.querySelector('[data-credential-copy]');
    const status = section.querySelector('[data-credential-status]');
    toggle.addEventListener('click', () => {
        const shown = value.type === 'password';
        value.type = shown ? 'text' : 'password';
        toggle.textContent = shown ? 'Ẩn mật khẩu' : 'Hiện mật khẩu';
        toggle.setAttribute('aria-pressed', String(shown));
    });
    copy.addEventListener('click', async () => {
        try {
            if (!navigator.clipboard || !value.value) throw new Error();
            await navigator.clipboard.writeText(value.value);
            status.textContent = 'Đã sao chép. Chỉ bàn giao qua kênh riêng và xóa nội dung clipboard sau khi dùng.';
        } catch { status.textContent = 'Trình duyệt không cho sao chép. Bấm Hiện mật khẩu và sao chép trực tiếp.'; }
    });
    const clear = () => {
        value.value = ''; value.removeAttribute('value'); value.type = 'password';
        toggle.disabled = true; copy.disabled = true;
        status.textContent = 'Phiên bàn giao đã đóng. Không thể đọc lại mật khẩu.';
    };
    addEventListener('pagehide', clear);
    addEventListener('pageshow', event => { if (event.persisted) clear(); });
})();
