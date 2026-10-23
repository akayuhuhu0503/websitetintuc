(() => {
    const clock=document.querySelector<HTMLTimeElement>('[data-vietnam-clock]');
    const serverTime=Date.parse(clock?.dateTime||'');const started=performance.now();
    const vietnamDate=new Intl.DateTimeFormat('vi-VN',{timeZone:'Asia/Ho_Chi_Minh',weekday:'long',year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',second:'2-digit',hour12:false});
    const tick=()=>{if(clock&&Number.isFinite(serverTime)){const now=new Date(serverTime+performance.now()-started);clock.textContent=vietnamDate.format(now);clock.dateTime=now.toISOString();}};
    if(clock){tick();window.setInterval(tick,1000);}
    document.querySelectorAll<HTMLElement>('[data-activity-slider]').forEach(slider=>{
        const slides=Array.from(slider.querySelectorAll<HTMLElement>('[data-slide]'));let current=0;
        const controls=slider.querySelector<HTMLElement>('[data-slide-controls]');const status=slider.querySelector<HTMLElement>('[data-slide-status]');
        const show=(next:number)=>{current=(next+slides.length)%slides.length;slides.forEach((slide,i)=>{slide.hidden=i!==current;});if(status)status.textContent=`Ảnh ${current+1} / ${slides.length}`;};
        if(slides.length>1){show(0);if(controls)controls.hidden=false;slider.querySelector('[data-slide-prev]')?.addEventListener('click',()=>show(current-1));slider.querySelector('[data-slide-next]')?.addEventListener('click',()=>show(current+1));}
    });
    const toggle = document.querySelector<HTMLButtonElement>('.mobile-menu-toggle');
    const links = document.querySelector<HTMLElement>('#primary-links');
    toggle?.addEventListener('click', () => {
        const expanded = toggle.getAttribute('aria-expanded') === 'true';
        toggle.setAttribute('aria-expanded', String(!expanded));
        links?.classList.toggle('is-open', !expanded);
    });
    const menus = Array.from(document.querySelectorAll<HTMLDetailsElement>('.nav-dropdown'));
    menus.forEach(menu => menu.addEventListener('toggle', () => {
        if (menu.open) menus.forEach(other => { if (other !== menu) other.open = false; });
    }));
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape') return;
        const opened = menus.find(menu => menu.open);
        if (opened) { opened.open = false; opened.querySelector<HTMLElement>('summary')?.focus(); }
        else if (toggle?.getAttribute('aria-expanded') === 'true') { toggle.click(); toggle.focus(); }
    });
    document.addEventListener('click', event => {
        if (!(event.target instanceof Node)) return;
        if (!menus.some(menu => menu.contains(event.target as Node))) menus.forEach(menu => { menu.open = false; });
    });
    const body=document.querySelector<HTMLElement>('.article-detail .article-body');
    let fontSize=18;
    const feedback=document.querySelector<HTMLElement>('.reading-feedback');
    document.querySelectorAll<HTMLButtonElement>('[data-font-size]').forEach(button=>button.addEventListener('click',()=>{
        fontSize=button.dataset.fontSize==='reset'?18:Math.max(16,Math.min(24,fontSize+(button.dataset.fontSize==='increase'?2:-2)));
        if(body)body.style.fontSize=fontSize+'px';if(feedback)feedback.textContent='Cỡ chữ '+fontSize+' pixel.';
    }));
    document.querySelector('[data-print-article]')?.addEventListener('click',()=>window.print());
    const publicUrl=()=>document.querySelector<HTMLLinkElement>('link[rel=canonical]')?.href||location.href;
    async function copyLink(){try{await navigator.clipboard.writeText(publicUrl());if(feedback){feedback.classList.remove('sr-only');feedback.textContent='Đã sao chép liên kết bài đang công khai.';}}catch{if(feedback){feedback.classList.remove('sr-only');feedback.textContent='Không thể truy cập clipboard. Sao chép địa chỉ từ thanh địa chỉ trình duyệt.';}}}
    document.querySelector('[data-copy-link]')?.addEventListener('click',()=>void copyLink());
    document.querySelector('[data-share-article]')?.addEventListener('click',()=>{if(navigator.share)void navigator.share({title:document.querySelector('h1')?.textContent||'',url:publicUrl()}).catch(()=>{});else void copyLink();});
    // Wrap source tables only in the rendered DOM. Original revision HTML stays unchanged.
    body?.querySelectorAll('table').forEach(table=>{const wrapper=document.createElement('div');wrapper.className='article-table-scroll';wrapper.tabIndex=0;wrapper.setAttribute('role','region');wrapper.setAttribute('aria-label','Bảng trong bài viết; cuộn ngang để xem đủ cột');table.before(wrapper);wrapper.append(table);});
})();
