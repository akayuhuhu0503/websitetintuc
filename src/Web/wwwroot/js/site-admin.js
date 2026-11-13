(() => {
    const targetKind = document.getElementById('site_targetKind');
    const revealTarget = () => document.querySelectorAll('[data-target-kind]').forEach(group => { group.hidden = !group.dataset.targetKind.split(' ').includes(targetKind?.value); });
    targetKind?.addEventListener('change', revealTarget); revealTarget();
    const edit = document.getElementById('site-edit-form'); let changed = false;
    edit?.addEventListener('input', () => changed = true); edit?.addEventListener('change', () => changed = true);
    edit?.addEventListener('submit', () => changed = false);
    window.addEventListener('beforeunload', event => { if (changed) { event.preventDefault(); event.returnValue = ''; } });
    const select = document.querySelector('.site-image-select');
    const image = document.getElementById('site-image-preview');
    if (image) document.getElementById('site-image-slot')?.append(image);
    const cropWarning = document.getElementById('event-banner-crop-warning');
    const bannerPosition = document.getElementById('site_position');
    function warnCrop() {
        if (!cropWarning) return;
        const ratio = image?.naturalWidth / image?.naturalHeight;
        const target = 117 / 10;
        cropWarning.hidden = !(bannerPosition?.value === 'home_event_strip' && select?.value && image?.complete && ratio > 0 && Math.min(ratio / target, target / ratio) < 0.9);
    }
    image?.addEventListener('load', warnCrop);
    image?.addEventListener('error', () => { if (cropWarning) cropWarning.hidden = true; });
    bannerPosition?.addEventListener('change', warnCrop);
    function preview() { if (!image || !select) return; if (cropWarning) cropWarning.hidden = true; image.hidden = !select.value; if (select.value) { image.src = '/admin/site/media/' + encodeURIComponent(select.value); if (image.complete) warnCrop(); } }
    select?.addEventListener('change', preview); preview();
    const form = document.getElementById('site-image-upload');
    if (!form) return;
    form.addEventListener('submit', event => {
        event.preventDefault(); const file = document.getElementById('site-upload-file').files[0]; if (!file) return;
        const status = document.getElementById('site-upload-status'); const progress = document.getElementById('site-upload-progress'); const button = form.querySelector('button');
        const token = document.querySelector('#site-edit-form input[name="__RequestVerificationToken"]')?.value;
        if (!token) { status.textContent = 'Phiên biểu mẫu không còn hợp lệ. Giữ nội dung đang nhập và tải lại trang trước khi thử lại.'; return; }
        button.disabled = true; progress.hidden = false; progress.value = 0; status.textContent = 'Đang tải và kiểm tra định dạng…';
        const request = new XMLHttpRequest(); request.open('POST', '/admin/media/upload?site=true'); request.setRequestHeader('X-CSRF-TOKEN', token);
        request.upload.onprogress = e => { if (e.lengthComputable) progress.value = Math.round(e.loaded * 100 / e.total); };
        request.onload = () => { button.disabled = false; let result; try { result = JSON.parse(request.responseText); } catch { status.textContent = 'Server chưa xác nhận lưu ảnh. Giữ bản nháp hiện có và kiểm tra kho trước khi thử lại.'; return; }
            if (request.status < 200 || request.status >= 300) { status.textContent = result.error || 'Upload bị từ chối.'; return; }
            const id = result.id || result.Id; const name = result.originalName || result.OriginalName || file.name;
            if (!id || !select) { status.textContent = 'Chưa nhận được mã ảnh đã lưu; chưa chọn vào cấu hình. Giữ nội dung đang nhập và kiểm tra thư viện.'; return; }
            let option = [...select.options].find(x => x.value === id); if (!option) { option = new Option(name, id); select.add(option); } select.value = id; select.dispatchEvent(new Event('change', { bubbles: true })); progress.value = 100;
            status.textContent = (result.notice || 'Đã tải và chọn ảnh.') + ' Lưu bản nháp để gắn ảnh; ảnh chưa hiển thị công khai.';
        };
        request.onerror = () => { button.disabled = false; status.textContent = 'Mất kết nối; chưa xác nhận ảnh đã lưu. Nội dung đang nhập vẫn được giữ. Kiểm tra thư viện trước khi tải lại.'; };
        const body = new FormData(); body.append('file', file); request.send(body);
    });
})();
