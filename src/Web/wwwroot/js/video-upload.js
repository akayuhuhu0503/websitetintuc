(() => {
  const root=document.querySelector('#video-upload');if(!root)return;
  const el=id=>document.getElementById(id),content=root.dataset.contentId,token=root.querySelector('[name=__RequestVerificationToken]').value;
  let session=null,selected=null,paused=true,busy=false,abort=null,rowVersion=root.dataset.rowVersion,command=null;
  const text=(id,value)=>{el(id).textContent=value;};
  const api=async(path,method='GET',body=null,headers={},signal=null)=>{
    const r=await fetch(path,{method,headers:{'X-CSRF-TOKEN':token,'X-Editor-Request':'true',...(body&&!(body instanceof Blob)?{'Content-Type':'application/json'}:{}),...headers},body:body instanceof Blob?body:body?JSON.stringify(body):null,signal});
    let value;try{value=await r.json();}catch{throw new Error(`Yêu cầu chưa thành công (${r.status}). Kiểm tra phiên đăng nhập và trạng thái tải.`);}
    if(!r.ok)throw new Error(value.error||value.message||`Yêu cầu bị từ chối (${r.status}).`);return value;
  };
  const digest=async blob=>Array.from(new Uint8Array(await crypto.subtle.digest('SHA-256',await blob.arrayBuffer()))).map(x=>x.toString(16).padStart(2,'0')).join('');
  const manifest=async(file,chunkBytes)=>{
    const receipts=[];for(let index=0;index*chunkBytes<file.size;index++){
      const part=file.slice(index*chunkBytes,Math.min(file.size,(index+1)*chunkBytes));
      receipts.push(`${index}:${part.size}:${await digest(part)}`);
      text('video-job',`Đang xác minh tệp: ${Math.min(file.size,(index+1)*chunkBytes).toLocaleString('vi-VN')} / ${file.size.toLocaleString('vi-VN')} byte; chưa xác nhận tải lên.`);
    }return digest(new Blob([receipts.join('\n')]));
  };
  const labels={Uploading:'Đang tải',Completed:'Đã tải xong',Cancelled:'Đã hủy',Expired:'Hết hạn',Pending:'Chờ quét',Scanning:'Đang quét',Processing:'Đang xử lý',Ready:'Sẵn sàng',Infected:'Cách ly do phát hiện nguy cơ',ScanFailed:'Quét chưa thành công',Failed:'Xử lý chưa thành công',Blocked:'Bị chặn'};
  const label=v=>window.landPortalStateLabel ? window.landPortalStateLabel(v) : (labels[v] || 'Cần kiểm tra trạng thái');
  const show=state=>{
    session=state;el('video-progress').value=state.expectedBytes?state.receivedBytes/state.expectedBytes*100:0;
    text('video-status',`${state.receivedBytes.toLocaleString('vi-VN')} / ${state.expectedBytes.toLocaleString('vi-VN')} byte đã xác nhận (${Math.floor(state.receivedBytes/state.expectedBytes*100)}%). Phiên: ${label(state.state)}.`);
    text('video-job',state.job?`Tệp: ${label(state.job.state)}. ${state.job.error?'Cần kiểm tra nhật ký xử lý.':''} · Lần thử ${state.job.attempts}/3`:'Chưa có kết luận quét/xử lý.');
    el('video-cancel-job').hidden=!state.mediaAssetId||['Ready','Infected','Cancelled'].includes(state.job?.state);
    const preview=el('video-preview');preview.replaceChildren();if(state.job?.state==='Ready'){const v=document.createElement('video');v.controls=true;v.playsInline=true;v.preload='metadata';v.className='article-video';v.setAttribute('aria-label','Xem trước video trong bản nháp');v.src=`/admin/videos/media/${state.mediaAssetId}/play?contentId=${content}`;v.poster=`/admin/videos/media/${state.mediaAssetId}/poster?contentId=${content}`;preview.append(v);}
    el('video-select').hidden=!state.mediaAssetId;el('video-retry').hidden=!state.mediaAssetId||!['Blocked','ScanFailed','Failed'].includes(state.job?.state)||state.job.attempts>=3;
    el('video-cancel').disabled=state.state!=='Uploading';el('video-resume').disabled=busy||state.state!=='Uploading';
  };
  const status=async()=>{if(session){show(await api(`/admin/videos/sessions/${session.id}`));await tracks();}};
  const sessions=async()=>{
    const list=await api(`/admin/videos/sessions?contentId=${content}`),box=el('video-sessions');box.replaceChildren();
    if(!list.length){box.textContent='Chưa có phiên tải.';return;}
    const ul=document.createElement('ul');for(const s of list){const li=document.createElement('li'),b=document.createElement('button');b.type='button';b.className='button secondary';b.textContent=`Mở phiên ${s.id.slice(0,8)} · ${s.receivedBytes}/${s.expectedBytes} byte · ${label(s.state)}`;b.onclick=()=>guard(async()=>{if(busy)throw new Error('Tạm dừng trước khi đổi phiên.');session=s;await status();});li.append(b);ul.append(li);}box.append(ul);
  };
  const upload=async()=>{
    if(busy||!session)return;if(!selected)throw new Error('Chọn lại đúng tệp trên máy trước khi tiếp tục.');
    busy=true;paused=false;el('video-pause').disabled=false;el('video-resume').disabled=true;el('video-start').disabled=true;
    try{
      await status();if(session.state!=='Uploading')throw new Error('Phiên không còn nhận phần tải.');
      if(selected.size!==session.expectedBytes||!selected.name.toLowerCase().endsWith(session.extension))throw new Error('Tệp chọn lại khác dung lượng hoặc định dạng của phiên.');
      if(await manifest(selected,session.chunkBytes)!==session.chunkManifestSha256)throw new Error('Tệp chọn lại khác tệp đã cam kết; không tiếp tục.');
      text('video-job','Đang xác minh tính toàn vẹn các phần tệp đã nhận; chưa gửi byte mới.');
      for(const chunk of session.chunks){if(paused)return;const local=selected.slice(chunk.index*session.chunkBytes,chunk.index*session.chunkBytes+chunk.bytes);if(await digest(local)!==chunk.sha256)throw new Error('Tệp đã thay đổi: mã kiểm tra phần tệp đã nhận không khớp. Không tiếp tục.');}
      for(let index=session.chunks.length;index<session.chunkCount;index++){
        if(paused)break;const part=selected.slice(index*session.chunkBytes,Math.min(selected.size,(index+1)*session.chunkBytes));const hash=await digest(part);if(paused)break;
        abort=new AbortController();await api(`/admin/videos/sessions/${session.id}/chunks/${index}`,'PUT',part,{'Content-Type':'application/octet-stream','X-Chunk-SHA256':hash},abort.signal);await status();
      }
      if(!paused&&session.receivedBytes===session.expectedBytes){await api(`/admin/videos/sessions/${session.id}/complete`,'POST');await status();await sessions();}
    }catch(e){paused=true;if(e.name!=='AbortError')throw e;await status();text('video-job','Đã tạm dừng. Byte SQL xác nhận được giữ; tiếp tục sẽ đối chiếu lại.');}
    finally{busy=false;abort=null;el('video-pause').disabled=true;el('video-start').disabled=false;el('video-resume').disabled=!session||session.state!=='Uploading';}
  };
  const guard=async f=>{try{await f();}catch(e){text('video-job',e.message||'Có lỗi thật; chưa xác nhận thao tác.');}};
  const select=async (asset,usage="Video")=>{command??=crypto.randomUUID();const result=await api(`/admin/videos/${content}/select`,'POST',{assetId:asset,rowVersion,commandId:command,usage});rowVersion=result.rowVersion;command=null;text('video-job',result.notice);};
  const tracks=async()=>{
    const box=el('video-subtitles');box.replaceChildren();if(!session?.mediaAssetId){box.textContent='Mở phiên video đã tải xong trước.';return;}
    for(const track of await api(`/admin/videos/${session.mediaAssetId}/subtitles`)){const p=document.createElement('p');p.textContent=`Phụ đề ${track.mediaAssetId.slice(0,8)} · ${label(track.state)} · ${track.error?'Cần kiểm tra nhật ký xử lý.':''}`;const b=document.createElement('button');b.type='button';b.className='button secondary';b.textContent='Gắn phụ đề vào nháp (video phải được gắn trước)';b.onclick=()=>guard(()=>select(track.mediaAssetId,'Subtitle'));p.append(b);box.append(p);}
  };
  el('video-subtitle-upload').onclick=()=>guard(async()=>{const file=el('video-subtitle').files[0];if(!session?.mediaAssetId||!file)throw new Error('Chọn phụ đề và mở phiên video đã tải xong.');if(file.size>1024*1024)throw new Error('Phụ đề tối đa1 MiB.');const result=await api(`/admin/videos/${session.mediaAssetId}/subtitle`,'PUT',file,{'Content-Type':'text/vtt','X-Subtitle-Name':'subtitle.vtt'});text('video-job',result.notice);await tracks();});
  el('video-file').onchange=()=>{selected=el('video-file').files[0]||null;};
  el('video-start').onclick=()=>guard(async()=>{if(!selected)throw new Error('Chọn tệp video từ máy.');if(selected.size>Number(root.dataset.maxBytes))throw new Error('Tệp vượt giới hạn cấu hình.');const mime={mp4:'video/mp4',mov:'video/quicktime',webm:'video/webm'}[selected.name.split('.').pop().toLowerCase()];const s=await api('/admin/videos/sessions','POST',{contentId:content,name:selected.name,mime:mime||'',bytes:selected.size,chunkManifestSha256:await manifest(selected,Number(root.dataset.chunkBytes))});session=s;await upload();});
  el('video-pause').onclick=()=>{paused=true;abort?.abort();text('video-job','Đang tạm dừng; sẽ đối chiếu phần tệp đã xác nhận sau yêu cầu đang chạy.');};
  el('video-resume').onclick=()=>guard(upload);
  el('video-cancel').onclick=()=>guard(async()=>{paused=true;abort?.abort();if(busy)throw new Error('Chờ trạng thái tạm dừng rồi hủy phiên.');await api(`/admin/videos/sessions/${session.id}/cancel`,'POST');await status();await sessions();});
  el('video-select').onclick=()=>guard(()=>select(session.mediaAssetId));
  el('video-retry').onclick=()=>guard(async()=>{await api(`/admin/videos/${session.mediaAssetId}/retry`,'POST');await status();});
  el('video-cancel-job').onclick=()=>guard(async()=>{const r=await api(`/admin/videos/${session.mediaAssetId}/cancel-job`,'POST');await status();text('video-job',r.notice||'Job đã hủy.');});
  el('video-refresh').onclick=()=>guard(async()=>{await status();await sessions();});
  const library=async(page=1)=>{const r=await api(`/admin/videos/library?contentId=${content}&page=${page}`),box=el('video-library');box.replaceChildren();if(!r.items.length){box.textContent='Chưa có video sẵn sàng trong phạm vi.';return;}for(const item of r.items){const button=document.createElement('button');button.type='button';button.className='button secondary';button.textContent=`Chọn video ${item.mediaAssetId.slice(0,8)} · ${item.durationSeconds?.toFixed(1)} giây`;button.onclick=()=>guard(()=>select(item.mediaAssetId));box.append(button);}for(const [label,next] of [['Trang trước',page-1],['Trang sau',page+1]]){if(next<1||next>Math.ceil(r.total/15))continue;const b=document.createElement('button');b.type='button';b.className='button secondary';b.textContent=label;b.onclick=()=>guard(()=>library(next));box.append(b);}};
  el('video-library-load').onclick=()=>guard(()=>library());
  guard(sessions);setInterval(()=>{if(!busy&&session)guard(status);},5000);
})();
