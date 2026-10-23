(() => {
    const select = document.querySelector('.site-image-select');
    const image = document.getElementById('site-image-preview');
    function preview() { if (!image || !select) return; image.hidden = !select.value; if (select.value) image.src = '/admin/site/media/' + encodeURIComponent(select.value); }
    select?.addEventListener('change', preview); preview();
    const form = document.getElementById('site-image-upload');
    if (!form) return;
    form.addEventListener('submit', event => {
        event.preventDefault(); const file = document.getElementById('site-upload-file').files[0]; if (!file) return;
        const status = document.getElementById('site-upload-status'); const progress = document.getElementById('site-upload-progress'); const button = form.querySelector('button');
        const token = document.querySelector('#site-edit-form input[name="__RequestVerificationToken"]')?.value;
        if (!token) { status.textContent = 'Thiếu CSRF token; tải lại trang.'; return; }
        button.disabled = true; progress.hidden = false; progress.value = 0; status.textContent = 'Đang tải và kiểm tra định dạng…';
        const request = new XMLHttpRequest(); request.open('POST', '/admin/media/upload?site=true'); request.setRequestHeader('X-CSRF-TOKEN', token);
        request.upload.onprogress = e => { if (e.lengthComputable) progress.value = Math.round(e.loaded * 100 / e.total); };
        request.onload = () => { button.disabled = false; let result; try { result = JSON.parse(request.responseText); } catch { status.textContent = 'Server chưa xác nhận lưu ảnh. Giữ bản nháp hiện có và kiểm tra kho trước khi thử lại.'; return; }
            if (request.status < 200 || request.status >= 300) { status.textContent = result.error || 'Upload bị từ chối.'; return; }
            const id = result.id || result.Id; const name = result.originalName || result.OriginalName || file.name;
            if (!id || !select) { status.textContent = 'Phản hồi thiếu asset ID; chưa chọn vào cấu hình.'; return; }
            let option = [...select.options].find(x => x.value === id); if (!option) { option = new Option(name + ' · Chưa quét an toàn', id); select.add(option); } select.value = id; preview(); progress.value = 100;
            status.textContent = result.notice + ' Lưu bản nháp để gắn ảnh; ảnh chưa public.';
        };
        request.onerror = () => { button.disabled = false; status.textContent = 'Mất kết nối; chưa xác nhận kết quả SQL. Kiểm tra kho ảnh trước khi tải lại.'; };
        const body = new FormData(); body.append('file', file); request.send(body);
    });
})();
