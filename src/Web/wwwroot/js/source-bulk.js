(() => {
    const toolbar = document.querySelector('.source-bulk'), dialog = document.getElementById('source-bulk-dialog');
    if (!toolbar || !dialog) return;
    const el = id => document.getElementById('source-bulk-' + id), key = 'gdla.source-bulk.v1';
    let scope = '', selected = {}, attempts = {}, busy = false, plan = [], action = '';
    const getScope = () => { const p = new URLSearchParams(location.search); p.delete('page'); p.delete('fragment');
        [...p].forEach(([k,v]) => { if (!v) p.delete(k); }); if (!p.has('tab')) p.set('tab','pending'); p.sort(); return toolbar.dataset.user + '|' + p; };
    const persist = () => { try { sessionStorage.setItem(key, JSON.stringify({scope,selected,attempts})); } catch { el('notice').textContent = 'Không lưu được lựa chọn qua trang; giữ trang này khi thực hiện.'; } };
    function syncScope() { const next = getScope(); if (next !== scope) { selected = {}; attempts = {}; scope = next;
        try { const saved = JSON.parse(sessionStorage.getItem(key) || 'null'); if (saved?.scope === scope) { selected = saved.selected || {}; attempts = saved.attempts || {}; } } catch {}
    } }
    const rows = () => [...document.querySelectorAll('.source-bulk-row:not(:disabled)')];
    const suitable = row => el('action').value === 'Ignore' || row.dataset.state === 'Ready';
    function update() {
        syncScope(); rows().forEach(r => {r.checked = !!selected[r.dataset.id];});
        const page = el('page'), current = rows().filter(suitable);
        if (page) { page.checked = current.length > 0 && current.every(r=>r.checked); page.indeterminate = current.some(r=>r.checked) && !page.checked; page.disabled = busy || !current.length; }
        el('count').textContent = `Đã chọn ${Object.keys(selected).length} bài trong bộ lọc (gồm các trang đã chọn)`;
        toolbar.querySelectorAll('[data-source-bulk-action]').forEach(b=>b.disabled=busy||!Object.keys(selected).length);
        el('action').disabled = busy; el('clear').disabled = busy; persist();
    }
    function choose(row, checked) { const id = row.dataset.id; if (!checked) delete selected[id];
        else if (!selected[id]) { if (Object.keys(selected).length >= 100) { el('notice').textContent = 'Mỗi lượt tối đa 100 bài.'; return; }
            selected[id] = {id,stamp:row.dataset.stamp,title:row.dataset.title}; } }
    document.addEventListener('change', e => { if(busy) return; const t=e.target;
        if(t.matches('.source-bulk-row')) choose(t,t.checked);
        if(t.id==='source-bulk-page') rows().filter(suitable).forEach(r=>choose(r,t.checked)); update(); });
    el('clear').addEventListener('click',()=>{if(!busy){selected={};update();}});
    document.addEventListener('source-sync-refreshed', update);
    function setBusy(value) {busy=value;document.documentElement.dataset.sourceBulkBusy=String(value);update();}
    async function post(path, body) {
        const r=await fetch('/admin/source-sync/bulk/'+path,{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':toolbar.querySelector('[name=__RequestVerificationToken]').value},body:JSON.stringify(body)});
        const data=await r.json().catch(()=>null);if(!r.ok||!data)throw Error(data?.message||'Không xác nhận được kết quả. Giữ mã thao tác để thử lại, không tạo bài mới.');return data;
    }
    const line=(item,message,url)=>{const li=document.createElement('li');li.dataset.id=item.id;const a=document.createElement('a');a.href=url||'/admin/source-sync/'+item.id;a.textContent=selected[item.id]?.title||item.title||'Mở bài';a.target='_blank';a.rel='noopener';li.append(a);const p=document.createElement('p');p.textContent=message;li.append(p);return li;};
    toolbar.querySelectorAll('[data-source-bulk-action]').forEach(button=>button.addEventListener('click',async()=>{if(busy)return;action=button.dataset.sourceBulkAction;el('action').value=action;setBusy(true);el('notice').textContent='Đang kiểm tra quyền, phiên bản và khả năng trùng bài…';
        try{const data=await post('preview',{action,items:Object.values(selected).map(({id,stamp})=>({id,stamp,requestId:attempts[action+'|'+id+'|'+stamp]?.requestId}))});plan=data.items;
            el('heading').textContent=el('action').selectedOptions[0].textContent;
            el('summary').textContent=`Đã chọn ${plan.length} · Đủ điều kiện ${plan.filter(x=>x.eligible).length} · Bị loại ${plan.filter(x=>!x.eligible).length}. Tệp và quyền được kiểm tra lại khi thực hiện.`;
            el('results').replaceChildren(...plan.map(p=>line(p,p.reason||'Đủ điều kiện tiếp tục.')));el('confirm').hidden=false;el('confirm').disabled=!plan.some(p=>p.eligible);el('progress').hidden=true;dialog.showModal();el('notice').textContent='';
        }catch(e){el('notice').textContent=e.message;}finally{setBusy(false);}}));
    el('close').addEventListener('click',()=>{if(!busy)dialog.close();});dialog.addEventListener('cancel',e=>{if(busy)e.preventDefault();});
    window.addEventListener('beforeunload',e=>{if(busy){e.preventDefault();e.returnValue='';}});
    el('confirm').addEventListener('click',async()=>{if(busy)return;setBusy(true);el('confirm').disabled=true;el('close').disabled=true;
        const progress=el('progress');progress.hidden=false;progress.max=plan.length;progress.value=0;let published=0,drafted=0,ignored=0,failed=0,excluded=0,reconciled=0;
        for(const p of plan){let message=p.reason,url=p.url;
            if(!p.eligible)excluded++;
            else{const item=selected[p.id],k=action+'|'+item.id+'|'+item.stamp;const attempt=attempts[k] ||= {action,item:{id:item.id,stamp:item.stamp},requestId:crypto.randomUUID()};persist();
                try{const data=await post('execute',attempt);message=data.message;url=data.publicUrl||data.editUrl||url;
                    if(data.outcome==='Published')published++;else if(data.outcome==='Ignored')ignored++;else if(data.outcome==='AlreadyProcessed')reconciled++;else drafted++;
                    if(data.outcome==='DraftedWithError')failed++;delete selected[p.id];
                }catch(e){failed++;message=e.message;}}
            const old=[...el('results').children].find(x=>x.dataset.id===p.id);old?.replaceWith(line({...p,title:old?.querySelector('a')?.textContent},message,url));progress.value++;persist();
            el('summary').textContent=`${progress.value}/${plan.length} · Đã đăng ${published} · Chuyển nháp ${drafted} · Bỏ qua ${ignored} · Lỗi ${failed} · Không đủ điều kiện ${excluded} · Đã xử lý trước đó ${reconciled}`;
        }
        setBusy(false);el('close').disabled=false;el('confirm').hidden=true;document.dispatchEvent(new Event('source-sync-refresh'));
    }); update();
})();
