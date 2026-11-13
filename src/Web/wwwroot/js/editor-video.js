(() => {
  const root = document.getElementById('editor-video');
  if (!root || !root.querySelector('[data-video-action="start"]')) return;
  const form = root.closest('form'), find = selector => root.querySelector(selector);
  const button = action => find(`[data-video-action="${action}"]`);
  const fileInput = find('#editor-video-file'), progress = find('#editor-video-progress');
  const message = value => { find('[data-video-message]').textContent = value; };
  const processing = value => { find('[data-video-processing]').textContent = value; };
  const bridge = () => window.landPortalEditor;
  let contentId = root.dataset.contentId, session = null, file = null, busy = false, polling = false;
  let paused = false, abort = null, attaching = false, editable = true;
  const attachFailures = new Set();
  const labels = { Uploading: 'Đang tải lên', Completed: 'Đã tải lên, đang chờ xử lý', Pending: 'Đang chờ xử lý',
    Scanning: 'Đang kiểm tra tệp', Processing: 'Đang chuyển mã và tạo ảnh đại diện', Ready: 'Đã xử lý, sẵn sàng dùng trong bài',
    Failed: 'Xử lý chưa thành công', ScanFailed: 'Chưa hoàn tất kiểm tra an toàn', Blocked: 'Chưa đủ điều kiện xử lý',
    Infected: 'Đã cách ly do phát hiện nguy cơ', Cancelled: 'Đã hủy', Expired: 'Lần tải đã hết hạn' };
  async function api(path, method = 'GET', body = null, extraHeaders = {}, signal = null) {
    const token = form?.querySelector('[name="__RequestVerificationToken"]')?.value;
    const response = await fetch(path, { method, credentials: 'same-origin', cache: 'no-store', signal,
      headers: { 'X-Editor-Request': 'true', ...(token ? { 'X-CSRF-TOKEN': token } : {}),
        ...(body && !(body instanceof Blob) ? { 'Content-Type': 'application/json' } : {}), ...extraHeaders },
      body: body instanceof Blob ? body : body ? JSON.stringify(body) : null });
    let value;
    try { value = await response.json(); } catch { throw new Error('Chưa nhận được trạng thái tải. Kiểm tra phiên đăng nhập rồi thử lại.'); }
    if (!response.ok) throw new Error(value.error || value.message || 'Thao tác chưa thành công. Bản nháp và phần tệp đã nhận được giữ nguyên.');
    return value;
  }
  const digest = async blob => {
    if (!window.crypto?.subtle) throw new Error('Trình duyệt cần kết nối HTTPS để kiểm tra tính toàn vẹn tệp.');
    return Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256', await blob.arrayBuffer()))).map(x => x.toString(16).padStart(2, '0')).join('');
  };
  async function manifest(selected, chunkBytes) {
    const parts = [];
    for (let index = 0; index * chunkBytes < selected.size; index++) {
      if (paused) throw new DOMException('Paused', 'AbortError');
      const part = selected.slice(index * chunkBytes, Math.min(selected.size, (index + 1) * chunkBytes));
      parts.push(`${index}:${part.size}:${await digest(part)}`);
      processing(`Đang kiểm tra tệp trước khi tải: ${Math.round(Math.min(selected.size, (index + 1) * chunkBytes) / selected.size * 100)}%.`);
    }
    return digest(new Blob([parts.join('\n')]));
  }
  function controls() {
    button('start').disabled = busy || attaching || !editable;
    button('pause').disabled = !busy;
    button('resume').disabled = busy || attaching || !editable || session?.state !== 'Uploading';
    button('cancel').disabled = busy || attaching || !editable || session?.state !== 'Uploading';
    fileInput.disabled = busy || attaching;
  }
  function showProgress(value) {
    const percent = value.expectedBytes ? Math.floor(value.receivedBytes / value.expectedBytes * 100) : 0;
    progress.value = percent;
    message(`Đã tải ${percent}% · ${(value.receivedBytes / 1024 / 1024).toLocaleString('vi-VN', { maximumFractionDigits: 1 })} / ${(value.expectedBytes / 1024 / 1024).toLocaleString('vi-VN', { maximumFractionDigits: 1 })} MiB.`);
  }
  function preview(assetId, ready) {
    const box = find('[data-video-preview]');
    if (!ready || !assetId) { box.replaceChildren(); delete box.dataset.assetId; return; }
    if (box.dataset.assetId === assetId) return;
    box.replaceChildren(); box.dataset.assetId = assetId;
    const video = document.createElement('video');
    video.controls = true; video.playsInline = true; video.preload = 'metadata'; video.className = 'article-video';
    video.setAttribute('aria-label', 'Xem trước video đã xử lý trong bản nháp');
    video.src = `/admin/videos/media/${assetId}/play?contentId=${contentId}`;
    video.poster = `/admin/videos/media/${assetId}/poster?contentId=${contentId}`;
    const caption = document.createElement('p'); caption.textContent = 'Xem trước tệp đã xử lý. Video chỉ hiện trên website sau khi bài được duyệt và xuất bản.';
    box.append(video, caption);
  }
  async function attach(candidate) {
    if (attaching || busy || !candidate?.autoAttach || !bridge()?.withSavedDraft) return;
    attaching = true; controls();
    try {
      const result = await bridge().withSavedDraft(async draft => {
        if (draft.id !== contentId) throw new Error('Bản nháp đã thay đổi. Mở lại bài để đối chiếu video.');
        return api(`/admin/videos/${contentId}/attach-ready`, 'POST', { sessionId: candidate.id, rowVersion: draft.rowVersion });
      });
      if (!result) { attachFailures.add(candidate.id); processing('Video đã xử lý. Hoàn tất lưu bản nháp rồi bấm “Kiểm tra và gắn lại vào nháp”.'); }
      else { attachFailures.delete(candidate.id); processing(result.videoAssetId ? 'Video đã được gắn vào bản nháp. Bạn có thể xem trước và gửi duyệt.' : 'Lần gắn này đã được xử lý trước đó; không tự gắn lại video đã gỡ.'); }
    } catch (error) { attachFailures.add(candidate.id); processing(error.message); }
    finally { attaching = false; controls(); }
  }
  async function refresh(allowAttach = true) {
    if (!contentId || polling || busy || attaching) return;
    polling = true;
    try {
      const state = await api(`/admin/videos/editor/${contentId}`); editable = state.canUpload;
      const reconciliation = bridge()?.acceptServerVideoState?.(state) || 'current';
      if (reconciliation !== 'current') {
        if (reconciliation === 'conflict') {
          editable = false; controls();
          processing('Video được xử lý và gắn ở máy chủ kể cả khi đóng trang. Bản nháp trong tab này đã cũ; giữ phần chưa lưu và đối chiếu bản mới theo hướng dẫn phía trên.');
        }
        return;
      }
      if (!session && state.sessions.length) session = state.sessions.find(x => !['Cancelled', 'Expired'].includes(x.state)) || state.sessions[0];
      const current = state.sessions.find(x => x.id === session?.id);
      if (current) {
        session = { ...session, ...current }; showProgress(current);
        if (!attachFailures.has(current.id)) processing(`Video: ${current.status}. ${current.attached ? 'Đã gắn vào bản nháp.' : current.ready ? 'Đang chờ gắn vào bản nháp.' : 'Tải và xử lý tệp không làm bài tự xuất bản.'}`);
        preview(current.mediaAssetId, current.ready);
      } else if (state.attached.length) { const attached = state.attached[0]; preview(attached.mediaAssetId, attached.ready); }
      button('attach').hidden = !editable || !current?.autoAttach || !attachFailures.has(current.id);
      button('retry').hidden = !current?.retryAllowed;
      const linked = find('[data-video-attached]'); linked.replaceChildren();
      for (const value of state.attached) { const p = document.createElement('p'); p.textContent = `Video đã gắn: ${value.status}.`; linked.append(p); }
      const history = find('[data-video-sessions]'); history.replaceChildren();
      if (!state.sessions.length) history.textContent = 'Chưa có lần tải video nào.';
      state.sessions.forEach((value, index) => {
        const choice = document.createElement('button'); choice.type = 'button'; choice.className = 'button secondary';
        choice.textContent = `Lần tải ${state.sessions.length - index} · ${value.status}${value.attached ? ' · Đã gắn vào nháp' : ''}`;
        choice.disabled = busy || attaching;
        choice.addEventListener('click', () => guarded(async () => { if (busy || attaching) return; session = value; await refresh(); })); history.append(choice);
      });
      controls();
      if (allowAttach) {
        const candidate = state.sessions.find(x => x.autoAttach && !attachFailures.has(x.id));
        if (candidate) await attach(candidate);
      }
    } finally { polling = false; }
  }
  async function transfer() {
    session = await api(`/admin/videos/sessions/${session.id}`);
    if (session.state !== 'Uploading') throw new Error('Lần tải đã hoàn tất hoặc đã đóng. Không gửi lại tệp.');
    if (!file || file.size !== session.expectedBytes || !file.name.toLowerCase().endsWith(session.extension)) throw new Error('Chọn lại đúng tệp ban đầu để tiếp tục tải.');
    if (await manifest(file, session.chunkBytes) !== session.chunkManifestSha256) throw new Error('Tệp đã thay đổi so với lần tải trước; không ghi đè phần đã nhận.');
    for (const chunk of session.chunks) {
      if (paused) return;
      if (await digest(file.slice(chunk.index * session.chunkBytes, chunk.index * session.chunkBytes + chunk.bytes)) !== chunk.sha256) throw new Error('Phần tệp đã tải không khớp tệp đang chọn.');
    }
    for (let index = session.chunks.length; index < session.chunkCount; index++) {
      if (paused) return;
      const part = file.slice(index * session.chunkBytes, Math.min(file.size, (index + 1) * session.chunkBytes));
      const hash = await digest(part); if (paused) return;
      abort = new AbortController();
      await api(`/admin/videos/sessions/${session.id}/chunks/${index}`, 'PUT', part, { 'Content-Type': 'application/octet-stream', 'X-Chunk-SHA256': hash }, abort.signal);
      session = await api(`/admin/videos/sessions/${session.id}`); showProgress(session); processing('Đang tải video. Các phần đã nhận được giữ để tiếp tục khi cần.');
    }
    if (!paused) {
      await api(`/admin/videos/sessions/${session.id}/complete`, 'POST');
      fileInput.value = ''; file = null;
      processing('Đã tải xong. Hệ thống sẽ kiểm định, xử lý và tự gắn vào bản nháp. Bạn có thể đóng trang; bài chưa được công khai.');
    }
  }
  async function upload(create) {
    if (busy || attaching || !editable) return;
    if (!file) throw new Error('Chọn tệp video trên máy.');
    if (!file.size || file.size > Number(root.dataset.maxBytes)) throw new Error('Dung lượng video không hợp lệ hoặc vượt giới hạn.');
    busy = true; paused = false; controls();
    try {
      if (!bridge()?.ensureSavedDraft) throw new Error('Trình biên tập chưa sẵn sàng. Tải lại trang trước khi tải video.');
      const draft = await bridge().ensureSavedDraft(); if (!draft) return;
      contentId = draft.id; root.dataset.contentId = contentId;
      if (create) {
        const mime = { mp4: 'video/mp4', mov: 'video/quicktime', webm: 'video/webm' }[file.name.split('.').pop().toLowerCase()];
        if (!mime) throw new Error('Chọn video MP4, MOV hoặc WebM.');
        const hash = await manifest(file, Number(root.dataset.chunkBytes));
        if (paused) return;
        session = await api('/admin/videos/sessions', 'POST', { contentId, name: file.name, mime, bytes: file.size, chunkManifestSha256: hash, autoAttach: true });
      }
      if (!session) throw new Error('Chọn lần tải cần tiếp tục.');
      await transfer();
    } catch (error) {
      if (error.name !== 'AbortError') throw error;
      processing('Đã tạm dừng. Chọn “Tiếp tục tải” để đối chiếu và tải phần còn lại.');
    } finally { busy = false; abort = null; controls(); await refresh(); }
  }
  async function guarded(action) { try { await action(); } catch (error) { processing(error.message || 'Thao tác chưa thành công; dữ liệu đã nhận được giữ nguyên.'); } }
  fileInput.addEventListener('change', () => { file = fileInput.files[0] || null; });
  button('start').addEventListener('click', () => guarded(() => upload(true)));
  button('resume').addEventListener('click', () => guarded(() => upload(false)));
  button('pause').addEventListener('click', () => { paused = true; abort?.abort(); processing('Đang tạm dừng tải…'); });
  button('cancel').addEventListener('click', () => guarded(async () => { if (busy || attaching || !session) return; await api(`/admin/videos/sessions/${session.id}/cancel`, 'POST'); await refresh(false); }));
  button('attach').addEventListener('click', () => guarded(async () => { if (session) attachFailures.delete(session.id); await refresh(); }));
  button('retry').addEventListener('click', () => guarded(async () => { if (!session?.mediaAssetId || busy || attaching) return; await api(`/admin/videos/${session.mediaAssetId}/retry`, 'POST'); await refresh(false); }));
  window.addEventListener('cms:external-saved', () => { if (!attaching) guarded(() => refresh(false)); });
  window.addEventListener('beforeunload', event => { if (busy) { event.preventDefault(); event.returnValue = ''; } });
  guarded(() => refresh());
  setInterval(() => { if (!document.hidden && !busy && !attaching) guarded(() => refresh()); }, 4000);
})();
