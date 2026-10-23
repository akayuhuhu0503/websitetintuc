"use strict";
(() => {
    const foundForm = document.querySelector('#editor-form');
    const source = document.querySelector('#content-html');
    if (!foundForm || !source || typeof Jodit === 'undefined')
        return;
    const form = foundForm;
    const feedback = document.querySelector('#editor-feedback');
    const field = (name) => form.elements.namedItem(name);
    let csrf = field('__RequestVerificationToken').value;
    let sessionPaused = false;
    const reauth = document.querySelector('#reauth-dialog');
    let reauthCsrf = '';
    async function loadReauthChallenge() {
        const question = document.querySelector('#reauth-captcha-question');
        const authForm = document.querySelector('#reauth-form');
        if (!authForm || !question)
            return;
        const answer = authForm.elements.namedItem('captchaAnswer');
        const id = authForm.elements.namedItem('captchaId');
        answer.value = '';
        id.value = '';
        reauthCsrf = '';
        question.textContent = 'Đang lấy câu hỏi xác thực…';
        try {
            const response = await fetch('/admin/reauthenticate/challenge', { credentials: 'same-origin' });
            if (!response.ok)
                throw Error('Không lấy được câu hỏi. Chờ và thử lại; bản soạn vẫn giữ trong tab.');
            const challenge = await response.json();
            id.value = challenge.captchaId;
            reauthCsrf = challenge.csrf;
            question.textContent = challenge.question;
        }
        catch (error) {
            question.textContent = error instanceof Error ? error.message : 'Chưa lấy được câu hỏi xác thực.';
        }
    }
    function requireSession() { sessionPaused = true; window.clearTimeout(timer); if (reauth && !reauth.open) {
        reauth.showModal();
        void loadReauthChallenge();
    } }
    const canSave = form.dataset.canSave === 'true';
    let dirty = false, bodyDirty = false, userBodyInteraction = false, generation = 0, saving = false, conflicted = false, navigating = false;
    let timer;
    let activeSave = null;
    let uncertainSave = null;
    let initialBody = source.value;
    const uuid = () => crypto.randomUUID();
    const privateUrl = (id) => '/admin/media/' + id + '/preview' + (field('Id').value ? '?contentId=' + encodeURIComponent(field('Id').value) : '');
    const mediaGuid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
    function rewrite(html, toPrivate) {
        const document = new DOMParser().parseFromString(html, 'text/html');
        document.querySelectorAll('[src],[href]').forEach(element => {
            for (const attr of ['src', 'href']) {
                const value = element.getAttribute(attr);
                if (!value)
                    continue;
                const match = value.match(toPrivate ? /^\/media\/([0-9a-f-]{36})(?:\?.*)?$/i : /^\/admin\/media\/([0-9a-f-]{36})\/preview(?:\?.*)?$/i);
                if (match && mediaGuid.test(match[1]))
                    element.setAttribute(attr, toPrivate ? privateUrl(match[1]) : '/media/' + match[1]);
            }
        });
        return document.body.innerHTML;
    }
    source.value = rewrite(initialBody, true);
    const editor = Jodit.make(source, {
        language: 'vi', height: 550, minHeight: 360, toolbarAdaptive: false, toolbarSticky: false,
        readonly: !canSave, placeholder: 'Viết nội dung bài…',
        buttons: ['bold', 'italic', 'underline', '|', 'ul', 'ol', '|', 'paragraph', 'table', 'link', '|', 'undo', 'redo', 'eraser'],
        controls: { paragraph: { list: { p: 'Đoạn văn', h2: 'Tiêu đề 2', h3: 'Tiêu đề 3', h4: 'Tiêu đề 4', blockquote: 'Trích dẫn' } } },
        buttonsMD: [], buttonsSM: [], buttonsXS: [],
        disablePlugins: ['source', 'image', 'image-properties', 'video', 'file', 'file-browser', 'iframe', 'print', 'about', 'powered-by-jodit'],
        showCharsCounter: false, showWordsCounter: false, showXPathInStatusbar: false,
        askBeforePasteHTML: false, askBeforePasteFromWord: false,
        defaultActionOnPaste: 'insert_clear_html', defaultActionOnPasteFromWord: 'insert_clear_html',
        cleanHTML: { removeEmptyElements: false, fillEmptyParagraph: false },
        uploader: { url: '', insertImageAsBase64URI: false }
    });
    editor.editor.setAttribute('aria-label', 'Nội dung bài viết');
    document.querySelectorAll('input[id^="jodit-toolbar-focus-helper"],input[name^="jodit-toolbar-focus-helper"]').forEach(input => input.setAttribute('aria-label', 'Điều hướng thanh công cụ soạn thảo'));
    source.hidden = true;
    const notify = (message, error = false) => { feedback.textContent = message; feedback.classList.toggle('has-error', error); };
    const changed = () => {
        if (!canSave || conflicted)
            return;
        dirty = true;
        generation++;
        notify('Có thay đổi chưa lưu. Hệ thống sẽ tự lưu sau khi bạn ngừng nhập.');
        window.clearTimeout(timer);
        timer = window.setTimeout(() => { void save('Save', true); }, 1600);
    };
    editor.editor.addEventListener('input', () => { userBodyInteraction = true; bodyDirty = true; changed(); });
    editor.editor.addEventListener('keydown', () => { userBodyInteraction = true; });
    editor.editor.addEventListener('paste', () => { userBodyInteraction = true; });
    editor.editor.closest('.jodit-container')?.addEventListener('pointerdown', event => { if (event.target.closest('.jodit-toolbar-button'))
        userBodyInteraction = true; });
    editor.events.on('change', () => { if (userBodyInteraction && rewrite(editor.value, false) !== initialBody) {
        bodyDirty = true;
        changed();
    } });
    // Formatting toolbar operations can change content without a DOM input event.
    editor.events.on('afterCommand', () => { if (userBodyInteraction) {
        bodyDirty = true;
        changed();
    } });
    form.addEventListener('input', event => { if (event.target.closest('.jodit-container') || event.target.id === 'review-reason')
        return; changed(); });
    form.addEventListener('change', event => { if (event.target.closest('.jodit-container'))
        return; changed(); });
    function data(intent, autosave) {
        const result = new FormData(form);
        result.set('BodyHtml', bodyDirty ? rewrite(editor.value, false) : initialBody);
        result.set('intent', intent);
        result.set('autosave', String(autosave));
        result.set('RequestId', uuid());
        return result;
    }
    async function errorMessage(response) {
        const type = response.headers.get('content-type') || '';
        if (type.includes('application/json')) {
            const payload = await response.json();
            return payload.message || payload.error || payload.errors?.join(' ') || 'Không lưu được; máy chủ chưa xác nhận thay đổi.';
        }
        if (response.status === 403)
            return 'Bạn không còn quyền thực hiện thao tác này. Nội dung đang nhập vẫn được giữ.';
        if (response.status === 409)
            return 'Bài đã thay đổi hoặc chuyển trạng thái ở phiên khác. Nội dung đang nhập vẫn được giữ; tải lại và đối chiếu trước khi tiếp tục.';
        return 'Không lưu được (HTTP ' + response.status + '). Nội dung đang nhập vẫn được giữ.';
    }
    function update(result) {
        field('Id').value = result.contentId;
        field('RowVersion').value = result.rowVersion;
        field('CurrentRevisionId').value = result.currentRevisionId || '';
        form.dataset.state = result.state;
    }
    async function save(intent, autosave = false) {
        window.clearTimeout(timer);
        if (!canSave || conflicted || sessionPaused)
            return null;
        if (activeSave) {
            await activeSave;
            if (conflicted)
                return null;
            if (autosave)
                return null;
        }
        if (autosave && !dirty)
            return null;
        const request = uncertainSave || { payload: data(intent, autosave), intent, generation, wasNew: !field('Id').value };
        const capturedGeneration = request.generation;
        const wasNew = request.wasNew;
        intent = request.intent;
        saving = true;
        notify('Đang lưu vào SQL Server…');
        activeSave = (async () => {
            try {
                const response = await fetch('/admin/save?format=json', { method: 'POST', body: request.payload, headers: { 'X-CSRF-TOKEN': csrf, 'X-Editor-Request': 'true' }, credentials: 'same-origin' });
                if (response.status === 401) {
                    uncertainSave = request;
                    requireSession();
                    notify('Phiên đã hết hạn. Nội dung chưa lưu vẫn ở trang này; xác thực lại rồi bấm lưu.', true);
                    return null;
                }
                if (!response.ok) {
                    uncertainSave = response.status >= 500 ? request : null;
                    if (response.status === 409 || response.status === 403)
                        conflicted = true;
                    notify(await errorMessage(response), true);
                    return null;
                }
                const result = await response.json();
                uncertainSave = null;
                update(result);
                if (capturedGeneration === generation) {
                    dirty = false;
                    if (bodyDirty)
                        initialBody = rewrite(editor.value, false);
                }
                notify('Đã lưu lúc ' + new Date(result.savedUtc).toLocaleTimeString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' }) + (dirty ? '. Còn thay đổi mới đang chờ lưu.' : '.'));
                if (dirty && result.state !== 'Draft' && result.state !== 'Rejected') {
                    conflicted = true;
                    notify('Thao tác trước đã được xác nhận, nhưng còn thay đổi mới chưa lưu. Nội dung vẫn ở đây; giữ lại nội dung và đối chiếu bài trước khi mở bản sửa mới.', true);
                }
                else if (intent !== 'Save' || (wasNew && !dirty)) {
                    navigating = true;
                    window.location.assign('/admin/edit/' + result.contentId);
                }
                else if (dirty)
                    timer = window.setTimeout(() => { void save('Save', true); }, 1600);
                return result;
            }
            catch {
                uncertainSave = request;
                notify('Mất kết nối khi lưu. Chưa xác nhận thành công; nội dung đang nhập vẫn được giữ. Bấm lưu để kiểm tra lại cùng thao tác.', true);
                return null;
            }
            finally {
                saving = false;
            }
        })();
        try {
            return await activeSave;
        }
        finally {
            activeSave = null;
        }
    }
    form.addEventListener('submit', event => {
        event.preventDefault();
        const submitter = event.submitter;
        void save(submitter?.value || 'Save');
    });
    window.addEventListener('beforeunload', event => { if ((dirty || saving) && !navigating) {
        event.preventDefault();
        event.returnValue = '';
    } });
    document.querySelectorAll('[data-workflow]').forEach(button => button.addEventListener('click', async () => {
        var _a;
        if (button.dataset.confirm && !window.confirm(button.dataset.confirm))
            return;
        window.clearTimeout(timer);
        if (activeSave)
            await activeSave;
        if (dirty) {
            notify('Hãy lưu thay đổi trước khi thực hiện thao tác với phiên bản.', true);
            return;
        }
        const reason = document.querySelector('#review-reason')?.value || '';
        if (button.dataset.workflow === 'Reject' && !reason.trim()) {
            notify('Nhập lý do trả bài.', true);
            document.querySelector('#review-reason')?.focus();
            return;
        }
        button.disabled = true;
        (_a = button.dataset).commandId || (_a.commandId = uuid());
        const payload = new FormData();
        payload.set('Action', button.dataset.workflow);
        payload.set('RowVersion', field('RowVersion').value);
        payload.set('RevisionId', field('CurrentRevisionId').value);
        payload.set('Reason', reason);
        payload.set('RequestId', button.dataset.commandId);
        try {
            const response = await fetch('/admin/workflow/' + field('Id').value, { method: 'POST', body: payload, headers: { 'X-CSRF-TOKEN': csrf, 'X-Editor-Request': 'true' }, credentials: 'same-origin' });
            if (response.status === 401) {
                requireSession();
                notify('Cần xác thực lại. Thao tác xử lý bài chưa được xác nhận.', true);
                return;
            }
            if (!response.ok) {
                if (response.status < 500)
                    delete button.dataset.commandId;
                if (response.status === 409 || response.status === 403)
                    conflicted = true;
                notify(await errorMessage(response), true);
                return;
            }
            const result = await response.json();
            navigating = true;
            window.location.assign('/admin/edit/' + result.contentId);
        }
        catch {
            notify('Mất kết nối; chưa xác nhận thao tác thành công.', true);
        }
        finally {
            button.disabled = false;
        }
    }));
    const dialog = document.querySelector('#media-dialog');
    let mode = 'inline', mediaPage = 1, mediaRequest = 0;
    const mediaItems = document.querySelector('#media-items');
    const uploadFeedback = document.querySelector('#upload-feedback');
    function addAttachment(asset) {
        const list = document.querySelector('#selected-attachments');
        if (list.querySelector('[data-asset="' + asset.id + '"]'))
            return;
        const li = document.createElement('li');
        li.dataset.asset = asset.id;
        const input = document.createElement('input');
        input.type = 'hidden';
        input.name = 'AttachmentIds';
        input.value = asset.id;
        const captionLabel = document.createElement('label');
        captionLabel.htmlFor = 'attachment-label-' + asset.id;
        captionLabel.textContent = 'Nhãn tài liệu';
        const caption = document.createElement('input');
        caption.id = captionLabel.htmlFor;
        caption.name = 'AttachmentLabels[' + asset.id + ']';
        caption.maxLength = 2000;
        caption.className = 'attachment-caption';
        li.append(captionLabel, caption);
        const name = document.createElement('span');
        name.textContent = asset.originalName;
        const remove = document.createElement('button');
        remove.type = 'button';
        remove.className = 'link-button';
        remove.textContent = 'Gỡ khỏi bản nháp';
        remove.dataset.removeAttachment = '';
        li.append(input, name, remove);
        list.append(li);
        changed();
    }
    function selectMedia(asset) {
        if (mode === 'attachment') {
            addAttachment(asset);
        }
        else if (mode === 'cover') {
            field('CoverAssetId').value = asset.id;
            const img = document.createElement('img');
            img.src = asset.previewUrl;
            img.alt = 'Ảnh đại diện đang biên tập';
            document.querySelector('#cover-preview').replaceChildren(img);
            changed();
        }
        else {
            const figure = document.createElement('figure'), image = document.createElement('img');
            const caption = document.querySelector('#media-caption').value.trim();
            image.src = asset.previewUrl;
            image.alt = caption || asset.originalName;
            const figcaption = document.createElement('figcaption');
            figcaption.textContent = caption;
            figure.append(image);
            if (caption)
                figure.append(figcaption);
            editor.s.restore();
            editor.s.insertHTML(figure.outerHTML + '<p><br></p>');
            bodyDirty = true;
            changed();
        }
        dialog?.close();
    }
    async function loadMedia(page = 1) {
        if (!mediaItems)
            return;
        const requestNumber = ++mediaRequest;
        mediaPage = page;
        mediaItems.textContent = 'Đang đọc kho tệp…';
        const parameters = new URLSearchParams({ kind: mode === 'attachment' ? 'pdf' : 'image', search: document.querySelector('#media-search').value, page: String(page) });
        if (field('Id').value)
            parameters.set('contentId', field('Id').value);
        try {
            const response = await fetch('/admin/media?' + parameters, { credentials: 'same-origin', headers: { 'X-Editor-Request': 'true' } });
            if (response.status === 401) {
                requireSession();
                return;
            }
            if (requestNumber !== mediaRequest)
                return;
            if (!response.ok) {
                const message = await errorMessage(response);
                if (requestNumber === mediaRequest)
                    mediaItems.textContent = message;
                return;
            }
            const payload = await response.json();
            if (requestNumber !== mediaRequest)
                return;
            const uploadInput = document.querySelector('#media-upload-file');
            uploadInput.disabled = !payload.uploadAvailable;
            if (!payload.uploadAvailable)
                uploadFeedback.textContent = payload.notice;
            mediaItems.replaceChildren();
            if (!payload.items.length)
                mediaItems.textContent = 'Chưa có tệp phù hợp. Bạn có thể tải tệp mới từ máy.';
            for (const asset of payload.items) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'media-item';
                if (asset.mimeType.startsWith('image/')) {
                    const img = document.createElement('img');
                    img.src = asset.previewUrl;
                    img.alt = '';
                    img.loading = 'lazy';
                    button.append(img);
                }
                const name = document.createElement('strong');
                name.textContent = asset.originalName;
                const status = document.createElement('span');
                status.textContent = Math.ceil(asset.byteLength / 1024) + ' KB · ' + (window.landPortalStateLabel(asset.scanStatus));
                button.append(name, status);
                button.addEventListener('click', () => selectMedia(asset));
                mediaItems.append(button);
            }
            const pagination = document.querySelector('#media-pagination');
            pagination.replaceChildren();
            const count = Math.max(1, Math.ceil(payload.total / payload.pageSize));
            for (const [label, target] of [['← Trước', page - 1], ['Sau →', page + 1]]) {
                if (target < 1 || target > count)
                    continue;
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'button secondary';
                button.textContent = label;
                button.addEventListener('click', () => { void loadMedia(target); });
                pagination.append(button);
            }
        }
        catch {
            if (requestNumber === mediaRequest)
                mediaItems.textContent = 'Không kết nối được tới kho tệp.';
        }
    }
    document.querySelectorAll('[data-open-media]').forEach(button => button.addEventListener('click', () => {
        editor.s.save();
        mode = button.dataset.openMedia || 'inline';
        document.querySelector('#media-dialog-title').textContent = mode === 'cover' ? 'Chọn ảnh đại diện' : mode === 'attachment' ? 'Chọn PDF đính kèm' : 'Chèn ảnh có chú thích';
        const upload = document.querySelector('#media-upload-file');
        upload.accept = mode === 'attachment' ? 'application/pdf' : 'image/jpeg,image/png,image/gif,image/webp';
        dialog?.showModal();
        void loadMedia();
    }));
    document.querySelector('[data-close-media]')?.addEventListener('click', () => dialog?.close());
    document.querySelector('[data-search-media]')?.addEventListener('click', () => { void loadMedia(); });
    document.querySelector('.remove-cover')?.addEventListener('click', () => { field('CoverAssetId').value = ''; document.querySelector('#cover-preview').replaceChildren(); changed(); });
    form.addEventListener('click', event => {
        const target = event.target;
        const video = target.closest('[data-remove-video]'), subtitle = target.closest('[data-remove-subtitle]');
        if (video) {
            video.closest('[data-video-reference]')?.remove();
            form.querySelectorAll('[data-subtitle-reference]').forEach(x => x.remove());
            changed();
        }
        if (subtitle) {
            subtitle.closest('[data-subtitle-reference]')?.remove();
            changed();
        }
    });
    document.querySelector('#selected-attachments')?.addEventListener('click', event => { const button = event.target.closest('[data-remove-attachment]'); if (button) {
        button.closest('li').remove();
        changed();
    } });
    document.querySelector('#media-upload-file')?.addEventListener('change', event => {
        const file = event.target.files?.[0];
        if (!file)
            return;
        const progress = document.querySelector('#upload-progress');
        progress.hidden = false;
        progress.value = 0;
        uploadFeedback.textContent = 'Đang tải ' + file.name + '…';
        const request = new XMLHttpRequest();
        request.open('POST', '/admin/media/upload' + (field('Id').value ? '?contentId=' + encodeURIComponent(field('Id').value) : ''));
        request.setRequestHeader('X-CSRF-TOKEN', csrf);
        request.setRequestHeader('X-Editor-Request', 'true');
        request.upload.onprogress = progressEvent => { if (progressEvent.lengthComputable)
            progress.value = progressEvent.loaded / progressEvent.total * 100; };
        request.onload = () => {
            progress.hidden = true;
            if (request.status === 401) {
                requireSession();
                uploadFeedback.textContent = 'Phiên đã hết hạn. Chưa xác nhận upload thành công.';
                return;
            }
            try {
                const payload = JSON.parse(request.responseText);
                if (request.status < 200 || request.status >= 300) {
                    uploadFeedback.textContent = payload.message || payload.error || 'Tải tệp không thành công (HTTP ' + request.status + ').';
                    return;
                }
                uploadFeedback.textContent = payload.notice || 'Đã lưu tệp vào kho riêng. Tệp chưa được công khai.';
                void loadMedia(mediaPage);
            }
            catch {
                uploadFeedback.textContent = 'Tải tệp không thành công; máy chủ chưa xác nhận.';
            }
        };
        request.onerror = () => { progress.hidden = true; uploadFeedback.textContent = 'Mất kết nối khi tải tệp. Chưa xác nhận thành công.'; };
        const payload = new FormData();
        payload.append('file', file);
        request.send(payload);
    });
    document.querySelector('[data-close-reauth]')?.addEventListener('click', () => reauth?.close());
    document.querySelector('[data-refresh-reauth]')?.addEventListener('click', () => { void loadReauthChallenge(); });
    document.querySelector('#reauth-form')?.addEventListener('submit', async (event) => {
        event.preventDefault();
        const authForm = event.currentTarget;
        const message = document.querySelector('#reauth-feedback');
        const password = authForm.elements.namedItem('password');
        try {
            const token = reauthCsrf;
            if (!token)
                throw Error('Không lấy được token xác thực; bản soạn vẫn giữ nguyên.');
            const payload = new FormData(authForm);
            payload.set('expectedUserId', form.dataset.userId || '');
            payload.set('__RequestVerificationToken', token);
            const response = await fetch('/admin/reauthenticate', { method: 'POST', body: payload, credentials: 'same-origin', headers: { 'X-Editor-Request': 'true' } });
            if (!response.ok) {
                message.textContent = 'Không xác thực được đúng tài khoản hoặc câu hỏi đã hết hạn. Nội dung vẫn giữ trong trang này.';
                await loadReauthChallenge();
                return;
            }
            const status = await fetch('/admin/session', { credentials: 'same-origin', headers: { 'X-Editor-Request': 'true' } });
            if (!status.ok)
                throw Error('Chưa xác nhận phiên mới.');
            const session = await status.json();
            if (session.userId !== form.dataset.userId)
                throw Error('Tài khoản phiên mới không khớp bản soạn.');
            csrf = session.csrf;
            field('__RequestVerificationToken').value = csrf;
            uncertainSave?.payload.set('__RequestVerificationToken', csrf);
            sessionPaused = false;
            conflicted = false;
            reauth?.close();
            notify('Đã xác thực lại. Nội dung vẫn giữ nguyên; bấm Lưu để kiểm tra quyền và phiên bản hiện thời.');
        }
        catch {
            message.textContent = 'Chưa xác thực lại được; giữ trang mở để bảo toàn bản soạn.';
        }
        finally {
            password.value = '';
        }
    });
    window.setInterval(() => {
        if (sessionPaused)
            return;
        void fetch('/admin/session', { credentials: 'same-origin', headers: { 'X-Editor-Request': 'true' } }).then(async (response) => {
            if (response.status === 401) {
                requireSession();
                return;
            }
            if (!response.ok)
                return;
            const session = await response.json();
            if (Date.parse(session.expiresUtc) - Date.now() < 120000)
                notify('Phiên sắp hết hạn do không hoạt động. Giữ trang mở; xác thực lại nếu được yêu cầu.', true);
        }).catch(() => { });
    }, 60000); // GET polling never renews the server activity clock.
})();
