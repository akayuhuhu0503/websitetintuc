(() => {
    const form = document.querySelector<HTMLFormElement>('#history-form'); if (!form) return;
    const csrf = (form.elements.namedItem('__RequestVerificationToken') as HTMLInputElement).value;
    const version = (form.elements.namedItem('RowVersion') as HTMLInputElement).value;
    const feedback = document.querySelector<HTMLElement>('#history-feedback')!;
    document.querySelectorAll<HTMLButtonElement>('[data-restore]').forEach(button => button.addEventListener('click', async () => {
        if (!window.confirm('Khôi phục phiên bản này thành bản nháp mới? Phiên bản công khai và lịch sử vẫn được giữ.')) return;
        button.disabled = true;
        const data = new FormData(); data.set('Action', 'RestoreRevision'); data.set('RowVersion', version); data.set('RevisionId', button.dataset.restore!); data.set('RequestId', crypto.randomUUID());
        try {
            const response = await fetch('/admin/workflow/' + form.dataset.contentId, { method: 'POST', body: data, headers: { 'X-CSRF-TOKEN': csrf } });
            const result = await response.json() as { message?: string; contentId?: string };
            if (!response.ok) { feedback.textContent = result.message || 'Không khôi phục được phiên bản.'; return; }
            window.location.assign('/admin/edit/' + result.contentId);
        } catch { feedback.textContent = 'Mất kết nối; chưa xác nhận khôi phục thành công.'; } finally { button.disabled = false; }
    }));
    document.querySelectorAll<HTMLButtonElement>('[data-compare]').forEach(button => button.addEventListener('click', async () => {
        feedback.textContent = 'Đang đọc phiên bản để so sánh…';
        try {
            const response = await fetch('/admin/history/' + form.dataset.contentId + '/compare?revisionId=' + button.dataset.compare);
            if (!response.ok) { feedback.textContent = 'Không thể xem phiên bản trong phạm vi quyền hiện tại.'; return; }
            const result = await response.json() as { before: Record<string, string> | null; after: Record<string, string> };
            const host = document.querySelector<HTMLElement>('#comparison-content')!; host.replaceChildren();
            const names: Record<string, string> = { title: 'Tiêu đề', summary: 'Mô tả', author: 'Tác giả', source: 'Nguồn', categories: 'Chuyên mục', cover: 'Ảnh đại diện', bodyText: 'Nội dung văn bản', attachments: 'Tài liệu / tài nguyên' };
            for (const [key, label] of Object.entries(names)) {
                const before = result.before?.[key] || '', after = result.after[key] || '';
                if (result.before && before === after) continue;
                const field = document.createElement('section'); field.className = 'diff-field';
                const title = document.createElement('h3'); title.textContent = label;
                const row = document.createElement('div'); row.className = 'diff-columns';
                for (const [name, text] of [['Trước', before], ['Sau', after]]) { const block = document.createElement('div'); const strong = document.createElement('strong'); strong.textContent = name; const content = document.createElement('pre'); content.textContent = text || '(trống)'; block.append(strong, content); row.append(block); }
                field.append(title, row); host.append(field);
            }
            if (!host.childElementCount) host.textContent = 'Các trường nội dung được so sánh giống nhau.';
            document.querySelector<HTMLElement>('#revision-comparison')!.hidden = false; feedback.textContent = ''; host.scrollIntoView({ block: 'nearest' });
        } catch { feedback.textContent = 'Không kết nối được để so sánh phiên bản.'; }
    }));
})();
