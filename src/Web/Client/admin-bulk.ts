(() => {
    interface Selection { id: string; revisionId: string; rowVersion: string; title: string; }
    interface Planned { id: string; title: string; eligible: boolean; reason: string | null; url: string | null; }
    interface Attempt { action: string; item: Selection; requestId: string; }
    const dialog = document.querySelector<HTMLDialogElement>("#bulk-dialog");
    const toolbar = document.querySelector<HTMLElement>(".bulk-toolbar");
    if (!dialog || !toolbar) return;
    const scopeParams = new URLSearchParams(location.search); scopeParams.delete("page");
    // Razor pagination emits empty optional filters; these are equivalent to
    // omitted filters on the initial /admin page.
    const emptyKeys: string[] = []; scopeParams.forEach((v, k) => { if (!v) emptyKeys.push(k); });
    emptyKeys.forEach(k => scopeParams.delete(k)); scopeParams.sort();
    // One tab/account/filter scope. Never persist credentials or choose unseen rows.
    const scope = `${toolbar.dataset.user}|${scopeParams.toString()}`;
    const key = "gdla.bulk-selection.v1";
    let selected: Record<string, Selection> = {}, attempts: Record<string, Attempt> = {};
    let scopeChanged = false, busy = false, plan: Planned[] = [], action = "";
    try { const saved = JSON.parse(sessionStorage.getItem(key) || "null"); if (saved?.scope === scope) { selected = saved.selected || {}; attempts = saved.attempts || {}; } else if (saved) scopeChanged = true; } catch { /* Storage unavailable: current page still works. */ }
    const el = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
    const persist = () => { try { sessionStorage.setItem(key, JSON.stringify({ scope, selected, attempts })); } catch { el("bulk-notice").textContent = "Trình duyệt không lưu được lựa chọn qua trang; hãy xử lý trên trang hiện tại."; } };
    const rows = () => Array.from(document.querySelectorAll<HTMLInputElement>(".bulk-row:not(:disabled)"));
    function update() {
        rows().forEach(r => r.checked = !!selected[r.dataset.id!]);
        const n = Object.keys(selected).length, all = el<HTMLInputElement>("bulk-select-page"), current = rows();
        if (all) { all.checked = current.length > 0 && current.every(r => r.checked); all.indeterminate = current.some(r => r.checked) && !all.checked; }
        el("bulk-count").textContent = `Đã chọn ${n} bài trong bộ lọc hiện tại (gồm các trang đã chọn)`;
        el<HTMLButtonElement>("bulk-preview").disabled = busy || !n || !el<HTMLSelectElement>("bulk-action").value;
        persist();
    }
    function select(row: HTMLInputElement, checked: boolean) {
        const id = row.dataset.id!;
        if (!checked) { delete selected[id]; return; }
        if (selected[id]) return; // Preserve selected version; do not silently refresh it after pagination.
        if (Object.keys(selected).length >= 100) { el("bulk-notice").textContent = "Mỗi nhóm tối đa 100 bài."; return; }
        selected[id] = { id, revisionId: row.dataset.revision!, rowVersion: row.dataset.version!, title: row.dataset.title! };
    }
    document.addEventListener("change", e => {
        const target = e.target as HTMLInputElement;
        if (busy) return;
        if (target.matches(".bulk-row")) select(target, target.checked);
        if (target.id === "bulk-select-page") rows().forEach(r => select(r, target.checked));
        if (target.matches(".bulk-row, #bulk-select-page, #bulk-action")) update();
    });
    document.addEventListener("click", e => {
        if ((e.target as Element).closest("#bulk-clear") && !busy) { selected = {}; update(); }
        if ((e.target as Element).closest("#bulk-preview") && !busy) void preview();
    });
    async function post(path: string, data: unknown) {
        const token = document.querySelector<HTMLInputElement>(".bulk-toolbar input[name=__RequestVerificationToken]")!.value;
        const response = await fetch(path, { method: "POST", credentials: "same-origin", headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token, "X-Editor-Request": "true" }, body: JSON.stringify(data) });
        const body = await response.json().catch(() => null);
        if (!response.ok || !body) throw new Error(body?.message || (response.status === 403 ? "Không còn quyền thực hiện; mở bài để kiểm tra." : "Chưa xác nhận kết quả. Giữ nguyên lựa chọn để đối soát hoặc thử lại cùng mã thao tác."));
        return body;
    }
    function resultRow(p: Planned, message: string) {
        const li = document.createElement("li"); li.dataset.id = p.id;
        if (p.url) { const link = document.createElement("a"); link.href = p.url; link.textContent = p.title; link.target = "_blank"; link.rel = "noopener"; li.append(link); }
        else li.append(document.createTextNode(p.title));
        const reason = document.createElement("p"); reason.textContent = message; li.append(reason); return li;
    }
    async function preview() {
        busy = true; update(); el("bulk-notice").textContent = "Đang kiểm tra từng bài…";
        action = el<HTMLSelectElement>("bulk-action").value;
        try {
            const data = await post("/admin/bulk/preview", { action, items: Object.values(selected) });
            plan = data.items; el("bulk-dialog-title").textContent = data.label;
            el("bulk-summary").textContent = `Đã chọn ${plan.length} · Đủ điều kiện ${plan.filter(p => p.eligible).length} · Bỏ qua ${plan.filter(p => !p.eligible).length}`;
            el("bulk-results").replaceChildren(...plan.map(p => resultRow(p, p.reason || "Đủ điều kiện để tiếp tục kiểm tra và thực hiện.")));
            el<HTMLButtonElement>("bulk-confirm").hidden = false;
            el<HTMLButtonElement>("bulk-confirm").disabled = !plan.some(p => p.eligible);
            el<HTMLProgressElement>("bulk-progress").hidden = true;
            dialog!.showModal(); el("bulk-notice").textContent = "";
        } catch (e) { el("bulk-notice").textContent = (e as Error).message; }
        finally { busy = false; update(); }
    }
    el("bulk-close").addEventListener("click", () => { if (!busy) dialog.close(); });
    dialog.addEventListener("cancel", e => { if (busy) e.preventDefault(); });
    window.addEventListener("beforeunload", e => { if (busy) { e.preventDefault(); e.returnValue = ""; } });
    el("bulk-confirm").addEventListener("click", async () => {
        if (busy) return;
        busy = true; el<HTMLButtonElement>("bulk-confirm").disabled = true; el<HTMLButtonElement>("bulk-close").disabled = true;
        const progress = el<HTMLProgressElement>("bulk-progress"); progress.hidden = false; progress.max = plan.length; progress.value = 0;
        let success = 0, skipped = 0, errors = 0;
        for (const p of plan) {
            let message = p.reason || "";
            if (!p.eligible) skipped++;
            else {
                const item = selected[p.id], attemptKey = `${action}|${item.id}|${item.revisionId}|${item.rowVersion}`;
                // Store the id before sending. A lost response/reload reuses it.
                const attempt = attempts[attemptKey] ||= { action, item, requestId: crypto.randomUUID() }; persist();
                try {
                    const data = await post("/admin/bulk/execute", attempt);
                    success++; message = data.alreadyApplied ? "Đã thực hiện trước đó; không lặp lại." : "Thành công.";
                    delete selected[p.id];
                } catch (e) { errors++; message = (e as Error).message; }
            }
            const old = Array.from(el("bulk-results").children).find(x => (x as HTMLElement).dataset.id === p.id); old?.replaceWith(resultRow(p, message));
            progress.value++; persist();
            el("bulk-summary").textContent = `${progress.value}/${plan.length} · Thành công ${success} · Bỏ qua ${skipped} · Lỗi/chưa xác nhận ${errors}`;
        }
        // Refresh only list/count/filter markup; preserve the visible result dialog.
        try {
            const response = await fetch(location.href, { credentials: "same-origin", cache: "no-store" });
            const doc = new DOMParser().parseFromString(await response.text(), "text/html"); const fresh = doc.querySelector("#bulk-list-fragment");
            if (!response.ok || !fresh) throw new Error();
            el("bulk-list-fragment").replaceWith(fresh);
        } catch { el("bulk-summary").append(" Danh sách chưa làm mới được; tải lại trang để xem trạng thái hiện tại."); }
        busy = false; el<HTMLButtonElement>("bulk-close").disabled = false; el<HTMLButtonElement>("bulk-confirm").hidden = true;
        if (document.querySelector(".bulk-toolbar")) update();
    });
    if (scopeChanged) el("bulk-notice").textContent = "Bộ lọc hoặc tài khoản đã đổi: đã bỏ lựa chọn của phạm vi trước.";
    update();
})();
