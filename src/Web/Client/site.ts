(() => {
    // Keep installed schema-compatible views in sync with the public copy until
    // their next binary deployment. Only interface chrome/help is adjusted;
    // source articles, test dataset labels and safety warnings are untouched.
    const deploymentCopy = /bản thử nghiệm|bản chạy thử|dành cho BA|phục vụ kiểm tra|chưa phải website vận hành chính thức|môi trường phát triển|LAN_DEMO/iu;
    const sourceNotice = 'Ghi rõ nguồn khi phát hành lại thông tin từ Cổng thông tin điện tử Cục Quản lý đất đai.';
    document.querySelectorAll('.environment-note').forEach(element => element.remove());
    document.querySelectorAll('.site-footer .footer-bottom span').forEach(element => {
        if (deploymentCopy.test(element.textContent || '')) element.remove();
    });
    document.querySelectorAll('.site-footer .footer-note').forEach(element => {
        if (deploymentCopy.test(element.textContent || '')) element.textContent = sourceNotice;
    });
    const description = document.querySelector<HTMLMetaElement>('meta[name="description"]');
    if (description && document.querySelector<HTMLMetaElement>('meta[property="og:type"]')?.content !== 'article' && deploymentCopy.test(description.content)) {
        description.content = 'Thông tin, chính sách và văn bản về quản lý đất đai.';
    }
    document.querySelectorAll<HTMLParagraphElement>('main > p.form-help').forEach(element => {
        if (element.textContent?.startsWith('Tìm trong bài đã xuất bản, metadata')) element.textContent = 'Tra cứu tin, văn bản và hỏi đáp đã công khai theo từ khóa, chuyên mục hoặc ngày đăng.';
        else if (element.textContent?.startsWith('Tìm trong bài đã xuất bản và thông tin văn bản. Chưa tìm')) element.textContent = 'Tra cứu theo trích yếu, số ký hiệu và thông tin văn bản. Ngày đăng và ngày ban hành được lọc riêng.';
    });
    document.querySelectorAll<HTMLParagraphElement>('main > p.result-count').forEach(element => {
        if (element.textContent?.includes('văn bản công khai trong tập dữ liệu đã nhập')) element.textContent = element.textContent.replace('văn bản công khai trong tập dữ liệu đã nhập', 'văn bản công khai');
    });
    document.querySelectorAll<HTMLParagraphElement>('.public-interaction-panel > p').forEach(element => {
        if (element.textContent?.includes('Thời hạn lưu giữ đang chờ đơn vị xác nhận.')) element.textContent = 'Thông tin được tiếp nhận để xử lý. Bình luận và câu hỏi chỉ hiển thị khi được đồng ý công khai và đã qua kiểm duyệt. Góp ý, liên hệ và tệp đính kèm được giữ riêng tư.';
    });
    document.querySelectorAll<HTMLAnchorElement>('.public-interaction-panel > a[href="/tra-cuu-ho-so"]').forEach(element => { element.textContent = 'Tra cứu tình trạng câu hỏi'; });
    if (document.querySelector('#receipt-code')) document.querySelectorAll<HTMLParagraphElement>('.interaction-panel > p').forEach(element => {
        if (element.textContent?.startsWith('Hồ sơ đã được lưu vào cơ sở dữ liệu')) element.textContent = 'Nội dung đã được tiếp nhận và đang chờ xử lý.';
        else if (element.textContent?.startsWith('Thư xác nhận đã được xếp hàng.')) element.textContent = 'Email xác nhận đang chờ gửi. Vui lòng lưu mã tiếp nhận để tra cứu.';
        else if (element.textContent?.startsWith('Dịch vụ thư chưa được bật tại môi trường này;')) element.textContent = 'Hệ thống chưa gửi email xác nhận. Vui lòng lưu mã tiếp nhận để tra cứu.';
    });
    async function showLatestNews(): Promise<void> {
        const navigation = document.querySelector('.main-nav');
        if (!navigation || location.pathname.startsWith('/admin') || document.querySelector('[data-latest-news]')) return;
        const ticker = document.createElement('section');
        ticker.className = 'latest-news-ticker';
        ticker.dataset.latestNews = '';
        ticker.setAttribute('aria-label', 'Tin tức mới nhất');
        ticker.hidden = true;
        ticker.innerHTML = '<div class="latest-news-inner"><div class="latest-news-viewport"><div class="latest-news-track"></div></div></div>';
        navigation.after(ticker);
        try {
            // Reuse the server's public-only, most-recent Article listing. Never
            // infer publication state from home cards or administrative data.
            let page: Document = document;
            if (location.pathname.replace(/\/$/, '') !== '/tin-tuc' || location.search !== '') {
                const controller = new AbortController();
                const timeout = window.setTimeout(() => controller.abort(), 5000);
                try {
                    const response = await fetch('/tin-tuc', { credentials: 'same-origin', cache: 'no-store', signal: controller.signal });
                    if (!response.ok || !response.headers.get('content-type')?.includes('text/html')) throw new Error('News unavailable');
                    page = new DOMParser().parseFromString(await response.text(), 'text/html');
                } finally { window.clearTimeout(timeout); }
            }
            const list = document.createElement('ul');
            list.className = 'latest-news-items';
            const seen = new Set<string>();
            for (const source of Array.from(page.querySelectorAll<HTMLAnchorElement>('.listing-results .listing-lead .lead-copy h3 a[href], .listing-results .listing-grid > .news-card h3 a[href]'))) {
                const url = new URL(source.getAttribute('href') || '', location.origin);
                const title = source.textContent?.replace(/\s+/g, ' ').trim();
                if (!title || url.origin !== location.origin || !/^\/bai-viet\/[a-f0-9-]{36}(?:\/|$)/i.test(url.pathname) || seen.has(url.pathname)) continue;
                seen.add(url.pathname);
                const item = document.createElement('li');
                const link = document.createElement('a');
                link.href = url.pathname;
                link.textContent = title;
                item.append(link); list.append(item);
                if (seen.size === 10) break;
            }
            if (seen.size === 0) { ticker.remove(); return; }
            const viewport = ticker.querySelector<HTMLElement>('.latest-news-viewport')!;
            const track = ticker.querySelector<HTMLElement>('.latest-news-track')!;
            track.append(list);
            // The second visual copy makes a seamless loop, but does not repeat
            // the headlines in keyboard navigation or screen-reader output.
            const repeat = list.cloneNode(true) as HTMLUListElement;
            repeat.setAttribute('aria-hidden', 'true');
            repeat.querySelectorAll('a').forEach(link => link.tabIndex = -1);
            track.append(repeat);
            ticker.dataset.count = String(seen.size);
            const measure = () => track.style.setProperty('--latest-news-duration', `${Math.max(35, Math.round(list.scrollWidth / 60))}s`);
            ticker.hidden = false;
            measure();
            void document.fonts.ready.then(measure);
            if (typeof ResizeObserver !== 'undefined') new ResizeObserver(measure).observe(viewport);
        } catch {
            // This optional strip must never block reading, login or forms.
            ticker.remove();
        }
    }
    void showLatestNews();
    // Native date fields use the browser/OS locale, not <html lang="vi">.
    // Keep field names, IDs and ISO wire values; localize only the editable UI.
    const dateFields = new WeakMap<HTMLInputElement, { kind: string; seconds: boolean; min: string; max: string }>();
    const pad = (value: number) => String(value).padStart(2, '0');
    function dateToDisplay(value: string): string {
        const match = /^(\d{4})-(\d{2})-(\d{2})(?:T(\d{2}):(\d{2})(?::(\d{2}))?)?$/.exec(value);
        return match ? `${match[3]}/${match[2]}/${match[1]}${match[4] ? ` ${match[4]}:${match[5]}${match[6] ? ':' + match[6] : ''}` : ''}` : value;
    }
    function dateToIso(input: HTMLInputElement): string | null {
        const options = dateFields.get(input);
        const value = input.value.trim();
        if (!options) return null;
        if (!value) return '';
        const match = /^(\d{2})\/(\d{2})\/(\d{4})(?:\s(\d{2}):(\d{2})(?::(\d{2}))?)?$/.exec(value);
        if (!match || (options.kind === 'datetime-local') !== !!match[4]) return null;
        const day = Number(match[1]), month = Number(match[2]), year = Number(match[3]);
        const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
        const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
        if (year < 1 || month < 1 || month > 12 || day < 1 || day > days[month - 1]) return null;
        if (match[4] && (Number(match[4]) > 23 || Number(match[5]) > 59 || Number(match[6] || 0) > 59)) return null;
        const date = `${match[3]}-${match[2]}-${match[1]}`;
        return match[4] ? `${date}T${match[4]}:${match[5]}${options.seconds || match[6] ? ':' + pad(Number(match[6] || 0)) : ''}` : date;
    }
    function validateDate(input: HTMLInputElement): void {
        const options = dateFields.get(input)!;
        const iso = dateToIso(input);
        const format = options.kind === 'date' ? 'dd/MM/yyyy' : 'dd/MM/yyyy HH:mm' + (options.seconds ? ':ss' : '');
        const invalid = input.value.trim() !== '' && (iso === null || (options.min && iso < options.min) || (options.max && iso > options.max));
        input.setCustomValidity(invalid ? `Ngày không hợp lệ. Nhập theo ${format} và trong khoảng cho phép.` : '');
    }
    function enhanceDates(root: ParentNode): void {
        root.querySelectorAll<HTMLInputElement>('input[type="date"],input[type="datetime-local"]').forEach(input => {
            if (input.dataset.nativeDatePicker !== undefined || dateFields.has(input)) return;
            const kind = input.type;
            const initial = input.value;
            const seconds = kind === 'datetime-local' && (input.step === '1' || /:\d{2}:\d{2}$/.test(initial));
            dateFields.set(input, { kind, seconds, min: input.min, max: input.max });
            const picker = document.createElement('input');
            picker.type = kind;
            picker.dataset.nativeDatePicker = '';
            picker.className = 'vietnamese-date-picker';
            picker.tabIndex = -1;
            picker.setAttribute('aria-hidden', 'true');
            picker.min = input.min; picker.max = input.max; picker.step = input.step;
            input.type = 'text';
            input.dataset.localizedDateInput = kind;
            input.placeholder = kind === 'date' ? 'dd/mm/yyyy' : 'dd/mm/yyyy HH:mm' + (seconds ? ':ss' : '');
            input.autocomplete = 'off';
            input.value = dateToDisplay(initial);
            input.defaultValue = input.value;
            input.title = kind === 'date' ? 'Nhập ngày theo dd/MM/yyyy.' : 'Nhập ngày giờ Việt Nam theo ' + input.placeholder + '.';
            const wrapper = document.createElement('span');
            wrapper.className = 'vietnamese-date-control';
            input.before(wrapper); wrapper.append(input, picker);
            const button = document.createElement('button');
            button.type = 'button'; button.className = 'vietnamese-date-button';
            button.setAttribute('aria-label', kind === 'date' ? 'Chọn ngày' : 'Chọn ngày giờ');
            button.title = button.getAttribute('aria-label')!;
            button.innerHTML = '<svg aria-hidden="true" viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor" stroke-width="1.7"><rect x="3" y="5" width="18" height="16" rx="2"/><path d="M16 3v4M8 3v4M3 11h18"/></svg>';
            button.disabled = input.disabled || input.readOnly;
            button.hidden = typeof picker.showPicker !== 'function';
            wrapper.append(button);
            button.addEventListener('click', () => {
                if (input.disabled || input.readOnly) return;
                picker.value = dateToIso(input) || '';
                try { picker.showPicker(); } catch { input.focus(); }
            });
            picker.addEventListener('change', () => {
                if (input.disabled || input.readOnly) return;
                input.value = dateToDisplay(picker.value);
                input.dispatchEvent(new Event('input', { bubbles: true }));
                input.dispatchEvent(new Event('change', { bubbles: true }));
            });
            input.addEventListener('input', () => validateDate(input));
            input.addEventListener('change', () => validateDate(input));
            validateDate(input);
        });
    }
    enhanceDates(document);
    new MutationObserver(records => {
        for (const record of records) for (const node of Array.from(record.addedNodes)) {
            if (node instanceof Element && !node.closest('.jodit-container')) {
                // Include a newly added date input itself, as well as whole form rows.
                if (node instanceof HTMLInputElement && node.parentElement) enhanceDates(node.parentElement);
                else enhanceDates(node);
            }
        }
    }).observe(document.body, { childList: true, subtree: true });
    document.addEventListener('formdata', event => {
        const form = event.target;
        if (!(form instanceof HTMLFormElement)) return;
        form.querySelectorAll<HTMLInputElement>('[data-localized-date-input]').forEach(input => {
            if (!dateFields.has(input) || !input.name || input.disabled) return;
            const iso = dateToIso(input);
            // Invalid values remain invalid for server validation; never silently
            // clear a date or reinterpret dd/MM as MM/dd during autosave.
            if (iso !== null) (event as FormDataEvent).formData.set(input.name, iso);
        });
    }, true);
    document.addEventListener('reset', event => {
        const form = event.target;
        if (form instanceof HTMLFormElement) queueMicrotask(() => {
            form.querySelectorAll<HTMLInputElement>('[data-localized-date-input]').forEach(input => validateDate(input));
        });
    }, true);
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
    document.querySelectorAll<HTMLElement>('[data-event-carousel]').forEach(carousel => {
        const slides = Array.from(carousel.querySelectorAll<HTMLElement>('[data-event-slide]'));
        if (slides.length < 2) return;
        const dots = Array.from(carousel.querySelectorAll<HTMLButtonElement>('[data-event-dot]'));
        const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
        let current = 0, timer: number | undefined, hovering = false, touching = false, start: { x: number; y: number } | null = null, suppressClick = false;
        const schedule = () => {
            window.clearTimeout(timer);
            if (!motion.matches && !document.hidden && !hovering && !touching && !carousel.contains(document.activeElement))
                timer = window.setTimeout(() => show(current + 1), 5000);
        };
        const show = (next: number) => {
            current = (next + slides.length) % slides.length;
            slides.forEach((slide, i) => {
                slide.hidden = false;
                slide.classList.toggle('is-active', i === current);
                slide.inert = i !== current;
                slide.setAttribute('aria-hidden', String(i !== current));
            });
            dots.forEach((dot, i) => dot.setAttribute('aria-current', String(i === current)));
            schedule();
        };
        carousel.classList.add('event-carousel-ready');
        slides.forEach(slide => { const img = slide.querySelector('img'); if (img) img.loading = 'eager'; });
        carousel.querySelector<HTMLElement>('[data-event-dots]')!.hidden = false;
        dots.forEach((dot, i) => dot.addEventListener('click', () => show(i)));
        carousel.addEventListener('pointerenter', e => { if (e.pointerType !== 'touch') { hovering = true; schedule(); } });
        carousel.addEventListener('pointerleave', e => { if (e.pointerType !== 'touch') { hovering = false; schedule(); } });
        carousel.addEventListener('focusin', schedule);
        carousel.addEventListener('focusout', () => window.setTimeout(schedule, 0));
        document.addEventListener('visibilitychange', schedule);
        motion.addEventListener('change', schedule);
        const surface = carousel.querySelector<HTMLElement>('.event-slides')!;
        surface.addEventListener('pointerdown', e => {
            if (e.pointerType !== 'touch') return;
            start = { x: e.clientX, y: e.clientY };
            suppressClick = false; touching = true; schedule();
        });
        surface.addEventListener('pointerup', e => {
            if (!start) return;
            const dx = e.clientX - start.x, dy = e.clientY - start.y;
            start = null; touching = false;
            if (Math.abs(dx) >= 45 && Math.abs(dx) > Math.abs(dy) * 1.2) {
                suppressClick = true; show(current + (dx < 0 ? 1 : -1));
            } else schedule();
        });
        surface.addEventListener('pointercancel', () => { start = null; touching = false; schedule(); });
        surface.addEventListener('click', e => { if (suppressClick) { e.preventDefault(); suppressClick = false; } }, true);
        show(0);
    });
    const navigation = document.querySelector<HTMLElement>('[data-portal-navigation]');
    const toggle = navigation?.querySelector<HTMLButtonElement>('.mobile-menu-toggle');
    const links = navigation?.querySelector<HTMLElement>('#primary-links');
    const menus = Array.from(navigation?.querySelectorAll<HTMLDetailsElement>('.nav-dropdown, .site-menu-subgroup') || []);
    const compactViewport = window.matchMedia('(max-width: 1024px)');
    const closeDropdowns = () => menus.forEach(menu => { menu.open = false; });
    function setNavigationExpanded(expanded: boolean) {
        toggle?.setAttribute('aria-expanded', String(expanded));
        links?.classList.toggle('is-open', expanded);
        if (!expanded)
            closeDropdowns();
    }
    toggle?.addEventListener('click', () => setNavigationExpanded(toggle.getAttribute('aria-expanded') !== 'true'));
    const normalizedPath = (path: string) => path.replace(/\/+$/, '') || '/';
    const normalizedQuery = (search: string) => {
        const pairs: [string,string][]=[];
        new URLSearchParams(search).forEach((value,key)=>pairs.push([key,value]));
        return JSON.stringify(pairs.sort(([ak,av],[bk,bv])=>ak.localeCompare(bk)||av.localeCompare(bv)));
    };
    navigation?.querySelectorAll<HTMLAnchorElement>('a[href]').forEach(link => {
        const url = new URL(link.href, location.href);
        const queryMatches = !url.search || normalizedQuery(url.search) === normalizedQuery(location.search);
        if (url.origin === location.origin && normalizedPath(url.pathname) === normalizedPath(location.pathname) && queryMatches) {
            link.setAttribute('aria-current', 'page');
            let parent = link.closest('details');
            while (parent && navigation!.contains(parent)) {
                parent.classList.add('is-active');
                parent = parent.parentElement?.closest('details') ?? null;
            }
        }
    });
    function placeDropdown(menu: HTMLDetailsElement) {
        if (!menu.matches('.nav-dropdown') || menu.matches('.nav-dropdown--wide'))
            return;
        const panel = menu.querySelector<HTMLElement>(':scope > .nav-panel');
        if (!panel)
            return;
        panel.style.removeProperty('--nav-panel-shift');
        if (!navigation || compactViewport.matches || navigation.classList.contains('nav-is-compact'))
            return;
        const boundary = navigation.querySelector('.nav-inner')!.getBoundingClientRect();
        const bounds = panel.getBoundingClientRect();
        const shift = Math.min(0, boundary.right - 12 - bounds.right) + Math.max(0, boundary.left + 12 - bounds.left);
        panel.style.setProperty('--nav-panel-shift', `${shift}px`);
    }
    menus.forEach(menu => {
        const summary = menu.querySelector<HTMLElement>(':scope > summary');
        menu.addEventListener('toggle', () => {
            summary?.setAttribute('aria-expanded', String(menu.open));
            if (menu.open) {
                // Close siblings only; a nested expansion must retain its parents.
                menus.forEach(other => { if (other !== menu && other.parentElement === menu.parentElement)
                    other.open = false; });
                placeDropdown(menu);
            }
        });
        summary?.addEventListener('keydown', event => {
            if (event.key !== 'ArrowDown' && event.key !== 'ArrowUp')
                return;
            event.preventDefault();
            menu.open = true;
            const targets = Array.from(menu.querySelectorAll<HTMLElement>('a[href], summary')).filter(element => element !== summary && element.getClientRects().length > 0);
            (event.key === 'ArrowUp' ? targets[targets.length-1] : targets[0])?.focus();
        });
    });
    document.addEventListener('keydown', event => {
        if (event.key !== 'Escape' || !navigation?.contains(document.activeElement))
            return;
        const opened = document.activeElement?.closest<HTMLDetailsElement>('details[open]') || menus.find(menu => menu.open);
        if (opened) {
            event.preventDefault();
            opened.open = false;
            opened.querySelector<HTMLElement>(':scope > summary')?.focus();
        }
        else if (toggle?.getAttribute('aria-expanded') === 'true') {
            event.preventDefault();
            setNavigationExpanded(false);
            toggle.focus();
        }
    });
    document.addEventListener('click', event => {
        if (event.target instanceof Node && navigation && !navigation.contains(event.target))
            setNavigationExpanded(false);
    });
    navigation?.addEventListener('focusout', event => {
        // During a native summary focus transfer activeElement can briefly be
        // body. Prefer the destination; defer unknown destinations to the next
        // task so a tap cannot close the mobile menu before its click lands.
        if (event.relatedTarget instanceof Node && navigation.contains(event.relatedTarget))
            return;
        window.setTimeout(() => {
            if (!navigation.contains(document.activeElement))
                setNavigationExpanded(false);
        }, 0);
    });
    function fitNavigation() {
        if (!navigation || !links)
            return;
        const wasCompact = compactViewport.matches || navigation.classList.contains('nav-is-compact');
        // CMS labels and root count vary. Collapse before they can wrap, without
        // changing the published order or hiding any configured destination.
        navigation.classList.remove('nav-is-compact');
        const compact = compactViewport.matches || links.scrollWidth > links.clientWidth + 1;
        navigation.classList.toggle('nav-is-compact', compact);
        if (!compact || wasCompact !== compact)
            setNavigationExpanded(false);
        menus.filter(menu => menu.open).forEach(placeDropdown);
    }
    fitNavigation();
    void document.fonts.ready.then(fitNavigation);
    window.addEventListener('resize', fitNavigation);
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
