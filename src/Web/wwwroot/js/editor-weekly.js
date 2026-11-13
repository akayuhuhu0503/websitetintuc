"use strict";
(() => {
 const date=document.querySelector('#weekly-date'),form=document.querySelector('#editor-form');if(!date||!form)return;
 const year=form.elements.namedItem('Metadata.WeeklyYear'),week=form.elements.namedItem('Metadata.WeeklyWeek');
 const rows=()=>Array.from(form.querySelectorAll('[data-weekly-row]'));
 const fields=['Date','Period','Time','Content','Chair','Attendees','Location','Note'];
 const get=(row,name)=>row.querySelector('[name$=".'+name+'"]');
 function monday(y,w){const d=new Date(Date.UTC(y,0,4));d.setUTCDate(d.getUTCDate()-((d.getUTCDay()+6)%7)+(w-1)*7);return d;}
 function range(){if(!year.value||!week.value||+week.value<1||+week.value>53)return;const d=monday(+year.value,+week.value),end=new Date(d);if(!Number.isFinite(d.getTime()))return;end.setUTCDate(end.getUTCDate()+6);document.querySelector('#weekly-range').textContent='Từ '+d.toLocaleDateString('vi-VN',{timeZone:'UTC'})+' đến '+end.toLocaleDateString('vi-VN',{timeZone:'UTC'});date.value=d.toISOString().slice(0,10);}
 date.addEventListener('change',()=>{const d=new Date(date.value+'T00:00:00Z');if(!Number.isFinite(d.getTime()))return;d.setUTCDate(d.getUTCDate()+3-((d.getUTCDay()+6)%7));year.value=String(d.getUTCFullYear());week.value=String(1+Math.round((d-monday(+year.value,1))/604800000));range();year.dispatchEvent(new Event('change',{bubbles:true}));});
 year.addEventListener('change',range);week.addEventListener('change',range);range();
 function next(){let row=rows().find(r=>r.hidden);if(!row&&rows().length<100){const index=rows().length;row=rows()[0].cloneNode(true);row.querySelectorAll('[name],[id],[for]').forEach(el=>{for(const key of ['name','id','for'])if(el.hasAttribute(key))el.setAttribute(key,el.getAttribute(key).replace(/ScheduleRows\[\d+\]/g,'ScheduleRows['+index+']').replace(/ScheduleRows_\d+__/g,'ScheduleRows_'+index+'__'));});fields.forEach(k=>get(row,k).value='');row.querySelector('summary').textContent='Buổi '+(index+1);rows().at(-1).after(row);}if(row){row.hidden=false;row.open=true;}else document.querySelector('#weekly-range').textContent='Một lịch tối đa 100 buổi.';return row;}
 document.querySelector('[data-add-weekly]')?.addEventListener('click',()=>{const row=next();get(row||form,'Date')?.focus();});
 form.addEventListener('click',e=>{const copy=e.target.closest('[data-copy-weekly]');if(!copy)return;const src=copy.closest('[data-weekly-row]'),dest=next();if(!dest)return;fields.forEach(k=>get(dest,k).value=get(src,k).value);get(dest,'Content').focus();get(dest,'Content').dispatchEvent(new Event('input',{bubbles:true}));});
 window.addEventListener('cms:weekly-import',e=>{const data=e.detail;if(data.rows.length>100){document.querySelector('#weekly-range').textContent='Tài liệu vượt quá 100 buổi; chưa thay dữ liệu.';return;}if(rows().some(r=>get(r,'Content').value)&&!window.confirm('Thay các dòng lịch đang soạn bằng nội dung Word vừa đọc? Bản công khai không thay đổi.'))return;
 rows().forEach(row=>row.hidden=false);while(rows().length<data.rows.length)next();
 rows().forEach((row,i)=>{const item=data.rows[i];fields.forEach(k=>get(row,k).value=item?.[k.charAt(0).toLowerCase()+k.slice(1)]||'');row.hidden=!item;row.open=!!item;});
 form.elements.namedItem('Metadata.WeeklyOriginalAssetId').value=data.assetId;
 document.querySelector('#weekly-original-review').hidden=false;
 document.querySelector('#weekly-original-link').href='/admin/documents/file/'+encodeURIComponent(data.assetId);
 const review=form.elements.namedItem('Metadata.WeeklyOriginalReviewEvidence');if(review)review.value='';
 document.querySelector('#weekly-range').textContent='Đã đọc '+data.rows.length+' buổi. Kiểm tra từng dòng trước khi lưu. '+data.warnings.join(' ');
 get(rows()[0],'Content').dispatchEvent(new Event('input',{bubbles:true}));
 });
})();
