"use strict";
(() => {
    const foundForm = document.querySelector('#editor-form');
    const source = document.querySelector('#content-html');
    if (!foundForm || !source || typeof Jodit === 'undefined')
        return;
    const form = foundForm;
    const returnToList = document.querySelector('.back-link')?.getAttribute('href');
    const editorUrl = (id) => '/admin/edit/' + id + (returnToList?.startsWith('/admin?') ? '?returnUrl=' + encodeURIComponent(returnToList) : '');
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
    const disclosure = form.querySelector('[name="Metadata.DisclosureGroupId"]');
    function showDisclosureFields() {
        const panel = document.querySelector('#disclosure-fields');
        if (!panel)
            return;
        panel.hidden = !disclosure?.value;
        panel.querySelectorAll('input,textarea').forEach(e => e.disabled = !canSave || panel.hidden);
        const direct = form.querySelector('[data-save-publish]');
        if (direct) {
            direct.hidden = !!disclosure?.value;
            direct.disabled = !canSave || !!disclosure?.value;
        }
        const directSchedule = form.querySelector('[data-editor-schedule="direct"]');
        if (directSchedule)
            directSchedule.hidden = !!disclosure?.value;
    }
    disclosure?.addEventListener('change', showDisclosureFields);
    showDisclosureFields();
    let dirty = false, bodyDirty = false, userBodyInteraction = false, generation = 0, saving = false, conflicted = false, navigating = false;
    let timer;
    let externalBusy = false;
    let documentUploadBusy = false;
    let readinessRequest = 0;
    let activeSave = null;
    let uncertainSave = null;
    let initialBody = source.value;
    // LAN HTTP is not a secure context: randomUUID may be unavailable, while
    // getRandomValues remains supported. Keep cryptographic RFC 4122 v4 IDs.
    const uuid = () => {
        if (typeof crypto.randomUUID === 'function')
            return crypto.randomUUID();
        const bytes = crypto.getRandomValues(new Uint8Array(16));
        bytes[6] = (bytes[6] & 0x0f) | 0x40;
        bytes[8] = (bytes[8] & 0x3f) | 0x80;
        const hex = Array.from(bytes, b => b.toString(16).padStart(2, '0')).join('');
        return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
    };
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
        form.querySelectorAll('[data-readiness-blocked]').forEach(b => { b.disabled = false; delete b.dataset.readinessBlocked; });
        notify('Có thay đổi chưa lưu. Hệ thống sẽ tự lưu sau khi bạn ngừng nhập.');
        window.clearTimeout(timer);
        // An upload may finish while a video is being attached server-side.
        // Retain its selection as dirty; save after the new rowversion arrives.
        if (externalBusy)
            return;
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
    function isDraftField(event) { const target = event.target; return !target.closest('.jodit-container') && !target.matches('input[type=file],#review-reason,#editor-due'); }
    // Choosing a local file is not yet a draft change. Mark dirty only when its
    // verified server ID is attached; otherwise this event races the first save.
    form.addEventListener('input', event => { if (isDraftField(event))
        changed(); });
    form.addEventListener('change', event => { if (isDraftField(event))
        changed(); });
    function data(intent, autosave) {
        const result = new FormData(form);
        result.set('BodyHtml', bodyDirty ? rewrite(editor.value, false) : initialBody);
        result.set('intent', intent);
        result.set('autosave', String(autosave));
        result.set('RequestId', uuid());
        return result;
    }
    // Compare only persisted fields, not command/version/session tokens or local files.
    // This runs at the debounced save boundary, not for every keystroke.
    function fingerprint(payload) {
        const values = [];
        payload.forEach((value, key) => { if (typeof value === 'string' && !['Id', 'CurrentRevisionId', 'RowVersion', 'RequestId', '__RequestVerificationToken', 'intent', 'autosave'].includes(key))
            values.push([key, value]); });
        return JSON.stringify(values);
    }
    let savedFingerprint = fingerprint(data('Save', true));
    let confirmedAttachmentIds = new Set();
    async function errorMessage(response) {
        const type = response.headers.get('content-type') || '';
        if (type.includes('application/json')) {
            const payload = await response.json();
            // Save and workflow transition are separate guarded commands. Retain the
            // committed draft identity/version if only the transition was refused.
            if (payload.savedDraft)
                update(payload.savedDraft);
            form.querySelectorAll('[data-server-error]').forEach(e => e.remove());
            form.querySelectorAll('[aria-invalid=true]').forEach(e => e.removeAttribute('aria-invalid'));
            let first = null;
            for (const [name, message] of Object.entries(payload.fieldErrors || {})) {
                const control = name === 'BodyHtml' ? form.querySelector('.jodit-wysiwyg') : name === 'CoverAssetId' ? document.querySelector('[data-open-media=cover]') : form.elements.namedItem(name);
                if (!control)
                    continue;
                control.setAttribute('aria-invalid', 'true');
                const error = document.createElement('p');
                error.dataset.serverError = 'true';
                error.className = 'field-validation-error';
                error.textContent = message;
                control.after(error);
                first || (first = control);
            }
            const target = first || document.querySelector('#editor-feedback');
            target?.focus();
            target?.scrollIntoView({ block: 'center', behavior: 'auto' });
            return [...new Set([payload.message || payload.error, ...(payload.errors || [])].filter(Boolean))].join(' ') || 'Không lưu được; máy chủ chưa xác nhận thay đổi.';
        }
        if (response.status === 403)
            return 'Bạn không còn quyền thực hiện thao tác này. Nội dung đang nhập vẫn được giữ.';
        if (response.status === 409)
            return 'Bài đã thay đổi hoặc chuyển trạng thái ở phiên khác. Nội dung đang nhập vẫn được giữ; tải lại và đối chiếu trước khi tiếp tục.';
        return 'Không lưu được (HTTP ' + response.status + '). Nội dung đang nhập vẫn được giữ.';
    }
    function update(result, checkReadiness = true) {
        const previousState = form.dataset.state;
        field('Id').value = result.contentId;
        field('RowVersion').value = result.rowVersion;
        field('CurrentRevisionId').value = result.currentRevisionId || '';
        form.dataset.state = result.state;
        if (previousState !== result.state && result.state === 'Approved') {
            form.querySelectorAll('[data-workflow="ApprovePublish"],[data-workflow="Approve"],[data-workflow="Reject"]').forEach(x => x.hidden = true);
            let recovery = document.querySelector('#editor-state-recovery');
            if (!recovery) {
                recovery = document.createElement('a');
                recovery.id = 'editor-state-recovery';
                recovery.className = 'outline-link';
                feedback.after(recovery);
            }
            recovery.href = editorUrl(result.contentId);
            recovery.textContent = 'Bài đã được duyệt. Mở lại phiên bản này để tiếp tục xuất bản hoặc đặt lịch.';
        }
        if (window.location.pathname === '/admin/create') {
            window.history.replaceState(null, '', editorUrl(result.contentId));
        }
        const preview = document.querySelector('#preview-link');
        if (preview)
            preview.href = '/admin/preview/' + result.contentId;
        window.dispatchEvent(new CustomEvent('cms:draft-saved', { detail: result }));
        if (checkReadiness)
            void refreshReadiness();
    }
    async function save(intent, autosave = false) {
        window.clearTimeout(timer);
        if (!canSave || conflicted || sessionPaused || externalBusy)
            return null;
        if (documentUploadBusy && intent !== 'Save') {
            notify('Chờ đính kèm tài liệu hoàn tất trước khi gửi duyệt hoặc đăng bài.', true);
            return null;
        }
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
        const requestFingerprint = fingerprint(request.payload);
        if (autosave && !uncertainSave && field('Id').value && requestFingerprint === savedFingerprint) {
            dirty = false;
            notify('Nội dung không đổi; bản nháp đã được lưu.');
            return null;
        }
        const capturedGeneration = request.generation;
        const wasNew = request.wasNew;
        intent = request.intent;
        saving = true;
        notify('Đang lưu bản nháp…');
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
                update(result, false);
                savedFingerprint = requestFingerprint;
                confirmedAttachmentIds = new Set(request.payload.getAll('AttachmentIds').filter((value) => typeof value === 'string'));
                form.querySelectorAll('[data-server-error]').forEach(e => e.remove());
                form.querySelectorAll('[aria-invalid=true]').forEach(e => e.removeAttribute('aria-invalid'));
                if (capturedGeneration === generation) {
                    dirty = false;
                    if (bodyDirty)
                        initialBody = rewrite(editor.value, false);
                }
                if (!dirty)
                    void refreshReadiness();
                notify('Đã lưu lúc ' + new Date(result.savedUtc).toLocaleTimeString('vi-VN', { timeZone: 'Asia/Ho_Chi_Minh' }) + (dirty ? '. Còn thay đổi mới đang chờ lưu.' : '.'));
                if (dirty && result.state !== 'Draft' && result.state !== 'Rejected') {
                    conflicted = true;
                    notify('Thao tác trước đã được xác nhận, nhưng còn thay đổi mới chưa lưu. Nội dung vẫn ở đây; giữ lại nội dung và đối chiếu bài trước khi mở bản sửa mới.', true);
                }
                else if (intent !== 'Save') {
                    navigating = true;
                    window.location.assign(editorUrl(result.contentId));
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
    window.addEventListener('beforeunload', event => { if ((dirty || saving || externalBusy || documentUploadBusy) && !navigating) {
        event.preventDefault();
        event.returnValue = '';
    } });
    document.querySelectorAll('[data-workflow]').forEach(button => button.addEventListener('click', async () => {
        var _a;
        if (documentUploadBusy) {
            notify('Chờ đính kèm tài liệu hoàn tất trước khi xử lý phiên bản bài.', true);
            return;
        }
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
            const response = await fetch((button.dataset.workflow === 'ApprovePublish' ? '/admin/approve-publish/' : '/admin/workflow/') + field('Id').value, { method: 'POST', body: payload, headers: { 'X-CSRF-TOKEN': csrf, 'X-Editor-Request': 'true' }, credentials: 'same-origin' });
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
            window.location.assign(editorUrl(result.contentId));
        }
        catch {
            notify('Mất kết nối; chưa xác nhận thao tác thành công.', true);
        }
        finally {
            button.disabled = false;
        }
    }));
    async function refreshReadiness() {
        const panel = document.querySelector('#editor-workflow-readiness');
        if (!panel || !field('Id').value || documentUploadBusy || dirty)
            return;
        const seq = ++readinessRequest;
        try {
            const response = await fetch('/admin/editor-readiness/' + field('Id').value, { credentials: 'same-origin' });
            if (!response.ok || seq !== readinessRequest)
                return;
            const state = await response.json();
            if (seq !== readinessRequest || dirty)
                return;
            panel.replaceChildren();
            const list = document.createElement('ul');
            for (const message of Object.values(state.errors)) {
                const item = document.createElement('li');
                item.textContent = message;
                list.append(item);
            }
            if (list.children.length)
                panel.append(list);
            const note = document.createElement('p');
            note.textContent = state.fileNotice;
            panel.append(note);
            for (const button of Array.from(form.querySelectorAll('[data-save-submit],[data-save-publish],[data-workflow="PublishApproved"],[data-workflow="ApprovePublish"]'))) {
                const allowed = button.hasAttribute('data-save-submit') ? state.canSubmit : state.canPublish;
                button.disabled = !allowed;
                if (!allowed)
                    button.dataset.readinessBlocked = 'true';
                else
                    delete button.dataset.readinessBlocked;
            }
        }
        catch {
            panel.textContent = 'Chưa kiểm tra được điều kiện phát hành. Bản nháp vẫn giữ nguyên; thử lại khi có mạng.';
        }
    }
    async function ensureSavedDraft() {
        if (externalBusy || sessionPaused || conflicted)
            return null;
        if (activeSave)
            await activeSave;
        if (!dirty && !uncertainSave && field('Id').value && !sessionPaused && !conflicted)
            return { id: field('Id').value, rowVersion: field('RowVersion').value };
        const result = await save('Save');
        if (!result || dirty || sessionPaused || conflicted)
            return null;
        return { id: result.contentId, rowVersion: result.rowVersion };
    }
    let externalPreparing = false;
    async function withSavedDraft(operation) {
        if (externalBusy || externalPreparing)
            return null;
        externalPreparing = true;
        const draft = await ensureSavedDraft();
        externalPreparing = false;
        if (!draft)
            return null;
        externalBusy = true;
        window.clearTimeout(timer);
        form.inert = true;
        try {
            const result = await operation(draft);
            if (!result)
                return null;
            update(result);
            if (result.videoAssetId && !form.querySelector('input[name="VideoAssetIds"][value="' + result.videoAssetId + '"]')) {
                const row = document.createElement('div');
                row.dataset.videoReference = result.videoAssetId;
                const input = document.createElement('input');
                input.type = 'hidden';
                input.name = 'VideoAssetIds';
                input.value = result.videoAssetId;
                row.append(input);
                const label = document.createElement('label');
                label.textContent = 'Chú thích video vừa đính kèm';
                label.htmlFor = 'video-caption-' + result.videoAssetId;
                const caption = document.createElement('input');
                caption.id = label.htmlFor;
                caption.name = 'AttachmentLabels[' + result.videoAssetId + ']';
                caption.maxLength = 2000;
                const remove = document.createElement('button');
                remove.type = 'button';
                remove.className = 'link-button';
                remove.dataset.removeVideo = result.videoAssetId;
                remove.textContent = 'Gỡ video và các phụ đề khỏi nháp';
                row.append(label, caption, remove);
                form.querySelector('.editor-video-references').append(row);
            }
            window.dispatchEvent(new CustomEvent('cms:external-saved', { detail: result }));
            return result;
        }
        finally {
            externalBusy = false;
            form.inert = false;
            if (dirty && !conflicted && !sessionPaused)
                timer = window.setTimeout(() => { void save('Save', true); }, 1600);
        }
    }
    function acceptServerVideoState(state) {
        if (!state.rowVersion || state.rowVersion === field('RowVersion').value)
            return 'current';
        // A background attachment creates a real revision. Never advance only the
        // version token: that would let this tab silently remove the new video.
        if (saving || activeSave || externalBusy || externalPreparing || uncertainSave || sessionPaused)
            return 'deferred';
        window.clearTimeout(timer);
        const selectedFile = Array.from(document.querySelectorAll('input[type=file]')).some(input => !!input.files?.length);
        if (dirty || conflicted || selectedFile) {
            conflicted = true;
            notify('Máy chủ đã cập nhật bản nháp trong lúc xử lý video. Nội dung đang nhập vẫn được giữ và tự lưu đã tạm dừng. Sao chép phần chưa lưu rồi mở bản nháp mới để đối chiếu trước khi tiếp tục.', true);
            let link = document.querySelector('#editor-video-reconcile');
            if (!link) {
                link = document.createElement('a');
                link.id = 'editor-video-reconcile';
                link.className = 'outline-link';
                link.target = '_blank';
                link.rel = 'noopener';
                feedback.after(link);
            }
            link.href = editorUrl(field('Id').value);
            link.textContent = 'Mở bản nháp mới trong tab khác để đối chiếu';
            return 'conflict';
        }
        navigating = true;
        window.location.assign(editorUrl(field('Id').value));
        return 'reloading';
    }
    window.landPortalEditor = { ensureSavedDraft, withSavedDraft, acceptServerVideoState };
    document.querySelector('#preview-link')?.addEventListener('click', async (event) => {
        if (!canSave)
            return;
        event.preventDefault();
        const preview = window.open('about:blank', '_blank');
        if (preview)
            preview.opener = null;
        const result = await save('Save');
        if (!result || dirty) {
            preview?.close();
            notify('Chưa mở xem trước vì bản soạn chưa lưu thành công. Nội dung vẫn ở trang này.', true);
            return;
        }
        if (preview)
            preview.location.href = '/admin/preview/' + result.contentId;
        else
            notify('Đã lưu. Trình duyệt chặn cửa sổ xem trước; cho phép cửa sổ mới rồi thử lại.', true);
    });
    document.querySelectorAll('[data-editor-schedule]').forEach(scheduleButton => scheduleButton.addEventListener('click', async (event) => {
        const button = event.currentTarget;
        const due = document.querySelector('#editor-due').value;
        const mode = button.dataset.editorSchedule;
        if (mode !== 'approved' && mode !== 'direct') {
            notify('Chọn cách hẹn giờ xuất bản.', true);
            return;
        }
        if (!due) {
            notify('Chọn thời điểm đăng theo giờ Việt Nam.', true);
            return;
        }
        if (externalBusy || saving || documentUploadBusy) {
            notify('Chờ lưu bài hoặc đính kèm tệp hoàn tất trước khi hẹn giờ.', true);
            return;
        }
        const scheduleButtons = document.querySelectorAll('[data-editor-schedule]');
        scheduleButtons.forEach(b => b.disabled = true);
        try {
            if (mode === 'direct' && canSave && (!(await save('Save')) || dirty))
                return;
            if (mode === 'approved' && form.dataset.state === 'PendingReview') {
                const p = new FormData();
                p.set('Action', 'Approve');
                p.set('RowVersion', field('RowVersion').value);
                p.set('RevisionId', field('CurrentRevisionId').value);
                p.set('RequestId', uuid());
                const approved = await fetch('/admin/workflow/' + field('Id').value, { method: 'POST', body: p, credentials: 'same-origin', headers: { 'X-CSRF-TOKEN': csrf, 'X-Editor-Request': 'true' } });
                if (!approved.ok) {
                    notify(await errorMessage(approved), true);
                    return;
                }
                update(await approved.json());
                notify('Đã duyệt; đang đặt lịch cho phiên bản này…');
            }
            if (mode === 'approved' && form.dataset.state !== 'Approved') {
                notify('Phiên bản này chưa được duyệt. Gửi duyệt trước khi hẹn giờ bản đã duyệt.', true);
                return;
            }
            const p = new FormData();
            p.set('ContentId', field('Id').value);
            p.set('RevisionId', field('CurrentRevisionId').value);
            p.set('ContentVersion', field('RowVersion').value);
            p.set('DueVietnam', due);
            p.set('Direct', mode === 'direct' ? 'true' : 'false');
            p.set('EditorMode', mode);
            const response = await fetch('/admin/editor-schedule/' + field('Id').value, { method: 'POST', body: p, credentials: 'same-origin', headers: { 'X-CSRF-TOKEN': csrf, 'X-Editor-Request': 'true' } });
            if (!response.ok) {
                notify((form.dataset.state === 'Approved' ? 'Bài đã duyệt nhưng chưa đặt lịch. ' : '') + await errorMessage(response), true);
                return;
            }
            notify((await response.json()).message);
        }
        catch {
            notify('Chưa xác nhận lịch đã lưu. Kiểm tra Lịch xuất bản trước khi thử lại; bản soạn vẫn được giữ.', true);
        }
        finally {
            scheduleButtons.forEach(b => b.disabled = false);
        }
    }));
    void refreshReadiness();
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
        const detail = document.createElement('small');
        const kind = asset.mimeType === 'application/pdf' ? 'PDF' : asset.mimeType.includes('wordprocessingml') ? 'Word' : asset.mimeType.includes('spreadsheetml') ? 'Excel' : 'Tài liệu';
        detail.textContent = kind + ' · ' + (asset.byteLength >= 1048576 ? (asset.byteLength / 1048576).toLocaleString('vi-VN', { maximumFractionDigits: 1 }) + ' MB' : Math.max(1, Math.ceil(asset.byteLength / 1024)) + ' KB');
        const remove = document.createElement('button');
        remove.type = 'button';
        remove.className = 'link-button';
        remove.textContent = 'Gỡ khỏi bản nháp';
        remove.dataset.removeAttachment = '';
        const view = document.createElement('a');
        view.href = asset.previewUrl;
        view.target = '_blank';
        view.rel = 'noopener';
        view.textContent = 'Xem tệp';
        li.append(input, name, detail, view, remove);
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
                uploadFeedback.textContent = payload.notice || 'Đã tải ảnh. Chưa công khai.';
                selectMedia(payload);
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
    const documentFeedback = document.querySelector('#document-feedback');
    async function loadDocuments() {
        const draft = await ensureSavedDraft();
        if (!draft)
            return;
        const response = await fetch('/admin/documents/' + draft.id + '/items', { credentials: 'same-origin' });
        if (!response.ok) {
            documentFeedback.textContent = await errorMessage(response);
            return;
        }
        const payload = await response.json();
        const list = document.querySelector('#document-library');
        list.replaceChildren();
        for (const item of payload.items) {
            const row = document.createElement('div');
            row.className = 'editor-document-row';
            const name = document.createElement('span');
            name.textContent = item.originalName + ' · ' + Math.ceil(item.byteLength / 1024) + ' KB';
            row.append(name);
            if (item.usable) {
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'button secondary';
                button.textContent = 'Đính kèm vào bài';
                button.onclick = () => { addAttachment(item); void save('Save'); };
                row.append(button);
                if (item.mimeType === 'application/vnd.openxmlformats-officedocument.wordprocessingml.document' && field('Metadata.WeeklyYear')) {
                    const importButton = document.createElement('button');
                    importButton.type = 'button';
                    importButton.className = 'button secondary';
                    importButton.textContent = 'Nhập lịch từ Word';
                    importButton.onclick = async () => { importButton.disabled = true; try {
                        const current = await ensureSavedDraft();
                        if (!current)
                            return;
                        const response = await fetch('/admin/editor-weekly/' + current.id + '/' + item.id, { method: 'POST', credentials: 'same-origin', headers: { 'X-CSRF-TOKEN': csrf, 'X-Editor-Request': 'true' } });
                        if (!response.ok) {
                            documentFeedback.textContent = await errorMessage(response);
                            return;
                        }
                        const preview = await response.json();
                        addAttachment(item);
                        window.dispatchEvent(new CustomEvent('cms:weekly-import', { detail: preview }));
                    }
                    catch {
                        documentFeedback.textContent = 'Chưa đọc được Word; các dòng lịch đang soạn vẫn giữ nguyên.';
                    }
                    finally {
                        importButton.disabled = false;
                    } };
                    row.append(importButton);
                }
            }
            else {
                const note = document.createElement('span');
                note.textContent = 'Chưa đủ điều kiện đính kèm. Tệp vẫn được giữ riêng cho bài này.';
                row.append(note);
            }
            list.append(row);
        }
        if (!payload.items.length)
            list.textContent = 'Bài này chưa có tài liệu đã tải.';
    }
    document.querySelector('[data-document-library]')?.addEventListener('click', () => void loadDocuments());
    const uncertainDocuments = new Set();
    const documentProgress = document.querySelector('#document-progress');
    function documentStatus(message, phase) {
        if (documentFeedback) {
            documentFeedback.textContent = message;
            documentFeedback.dataset.phase = phase;
            documentFeedback.setAttribute('role', 'status');
        }
    }
    async function reconcileDocument(contentId, sha256) {
        if (!sha256)
            return null;
        const response = await fetch('/admin/documents/' + contentId + '/result?sha256=' + encodeURIComponent(sha256), { credentials: 'same-origin', headers: { 'X-Editor-Request': 'true' } });
        if (response.status === 401) {
            requireSession();
            return null;
        }
        if (!response.ok)
            return null;
        return await response.json();
    }
    async function attachDocument(asset) {
        if (!asset.usable) {
            documentStatus(asset.notice || 'Tệp đã lưu nhưng chưa đủ điều kiện đính kèm. Kiểm tra lại trong Tài liệu đã tải.', 'held');
            return;
        }
        documentStatus('Đang gắn ' + asset.originalName + ' vào bài…', 'attaching');
        addAttachment(asset);
        const saved = await save('Save');
        const attached = !!saved && confirmedAttachmentIds.has(asset.id);
        documentStatus(attached ? 'Đã đính kèm ' + asset.originalName + ' vào bản nháp. ' + (asset.notice || '') : 'Tệp đã lưu riêng cho bài này nhưng chưa xác nhận gắn vào nháp. Bấm Lưu hoặc chọn lại từ tài liệu của bài.', attached ? 'complete' : 'unconfirmed');
    }
    document.querySelector('#editor-document-file')?.addEventListener('change', async (event) => {
        const input = event.currentTarget;
        const file = input.files?.[0];
        if (!file)
            return;
        if (documentUploadBusy)
            return;
        if (!/\.(pdf|docx|xlsx)$/i.test(file.name) || file.size > 10 * 1024 * 1024) {
            documentFeedback.textContent = 'Chọn PDF, DOCX hoặc XLSX không quá 10 MB.';
            return;
        }
        documentUploadBusy = true;
        input.disabled = true;
        documentStatus('Chuẩn bị ' + file.name + '…', 'preparing');
        if (documentProgress) {
            documentProgress.hidden = false;
            documentProgress.removeAttribute('value');
            documentProgress.max = 100;
        }
        const fileKey = JSON.stringify([file.name, file.size, file.lastModified]);
        try {
            // Existing drafts do not need a redundant save before upload: the server
            // checks edit rights and the final save includes current unsaved typing.
            if (activeSave)
                await activeSave;
            const draft = field('Id').value && !uncertainSave && !sessionPaused && !conflicted && !externalBusy ? { id: field('Id').value, rowVersion: field('RowVersion').value } : await ensureSavedDraft();
            if (!draft) {
                documentStatus('Chưa chuẩn bị được bản nháp. Kiểm tra thông báo lưu bài; nội dung vẫn được giữ.', 'unconfirmed');
                return;
            }
            // WebCrypto hashes at most the configured 10 MiB off the UI thread.
            // Plain HTTP LAN may not expose SubtleCrypto; it must never fake a hash.
            const sha256 = crypto.subtle ? Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', await file.arrayBuffer())), b => b.toString(16).padStart(2, '0')).join('') : null;
            const pendingKey = draft.id + '|' + (sha256 || fileKey);
            if (uncertainDocuments.has(pendingKey)) {
                documentStatus('Đang kiểm tra kết quả lần tải trước…', 'checking');
                const recovered = await reconcileDocument(draft.id, sha256);
                if (!recovered) {
                    documentStatus('Chưa tìm thấy kết quả đã lưu; tệp có thể vẫn đang được xử lý. Chọn lại cùng tệp để kiểm tra, không tải trùng.', 'unconfirmed');
                    return;
                }
                uncertainDocuments.delete(pendingKey);
                await attachDocument(recovered);
                input.value = '';
                return;
            }
            documentStatus('Đang tải ' + file.name + '…', 'uploading');
            if (documentProgress)
                documentProgress.value = 0;
            const asset = await new Promise((resolve, reject) => {
                const request = new XMLHttpRequest();
                request.open('POST', '/admin/documents/' + draft.id + '?format=json');
                request.setRequestHeader('X-CSRF-TOKEN', csrf);
                request.setRequestHeader('X-Editor-Request', 'true');
                request.upload.onprogress = e => { if (e.lengthComputable && documentProgress) {
                    documentProgress.value = e.loaded / e.total * 100;
                    documentStatus('Đang tải ' + file.name + ' · ' + Math.floor(e.loaded / e.total * 100) + '%', 'uploading');
                } };
                request.upload.onload = () => { if (documentProgress)
                    documentProgress.removeAttribute('value'); documentStatus('Đang kiểm tra định dạng và lưu tệp…', 'checking'); };
                request.onload = () => {
                    if (request.status === 401) {
                        requireSession();
                        documentStatus('Phiên đã hết hạn. Bản soạn vẫn được giữ; xác thực lại trước khi tải.', 'unconfirmed');
                        resolve(null);
                        return;
                    }
                    if (request.status >= 500) {
                        reject(Error('Upload result uncertain'));
                        return;
                    }
                    if (request.status < 200 || request.status >= 300) {
                        documentStatus(request.getResponseHeader('content-type')?.includes('text/plain') ? request.responseText : 'Chưa tải được tài liệu. Kiểm tra loại tệp, dung lượng và quyền của bạn.', 'rejected');
                        resolve(null);
                        return;
                    }
                    try {
                        resolve(JSON.parse(request.responseText));
                    }
                    catch {
                        reject(Error('Upload result uncertain'));
                    }
                };
                request.onerror = () => reject(Error('Upload result uncertain'));
                request.onabort = () => reject(Error('Upload result uncertain'));
                const payload = new FormData();
                payload.append('file', file);
                request.send(payload);
            }).catch(async () => {
                uncertainDocuments.add(pendingKey);
                documentStatus('Đang kiểm tra kết quả đã lưu sau mất kết nối…', 'checking');
                const recovered = await reconcileDocument(draft.id, sha256).catch(() => null);
                if (recovered) {
                    uncertainDocuments.delete(pendingKey);
                    return recovered;
                }
                documentStatus('Chưa xác nhận kết quả. Chọn lại cùng tệp để kiểm tra hoặc mở “Tài liệu đã tải”; không gửi lại tệp khi chưa rõ kết quả.', 'unconfirmed');
                return null;
            });
            if (asset) {
                await attachDocument(asset);
                input.value = '';
            }
        }
        catch {
            documentStatus('Chưa hoàn tất thao tác. Nội dung đang soạn vẫn được giữ; kiểm tra “Tài liệu đã tải” trước khi thử lại.', 'unconfirmed');
        }
        finally {
            documentUploadBusy = false;
            input.disabled = false;
            if (documentProgress)
                documentProgress.hidden = true;
            if (!dirty)
                void refreshReadiness();
        }
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
