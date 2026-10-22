import {chromium} from 'playwright';
import {writeFile,mkdir} from 'node:fs/promises';
import {dirname,resolve} from 'node:path';
import {createHash} from 'node:crypto';
const target=resolve(process.argv[2]),withAudio=process.argv[3]==='audio';const browser=await chromium.launch({channel:'msedge',headless:true,args:['--autoplay-policy=no-user-gesture-required']});
try{
 const page=await browser.newPage();const result=await page.evaluate(async withAudio=>{
  const canvas=document.createElement('canvas');canvas.width=320;canvas.height=180;const ctx=canvas.getContext('2d');const nonce=crypto.getRandomValues(new Uint8Array(32));
  const canvasStream=canvas.captureStream(10);let audio=null,oscillator=null;const tracks=canvasStream.getVideoTracks();
  if(withAudio){audio=new AudioContext({sampleRate:48000});const dest=audio.createMediaStreamDestination(),gain=audio.createGain();gain.gain.value=.01;oscillator=audio.createOscillator();oscillator.frequency.value=440;oscillator.connect(gain);gain.connect(dest);oscillator.start();await audio.resume();tracks.push(...dest.stream.getAudioTracks());}
  const stream=new MediaStream(tracks);const recorder=new MediaRecorder(stream,{mimeType:withAudio?'video/webm;codecs=vp8,opus':'video/webm;codecs=vp8',videoBitsPerSecond:150000});const chunks=[];
  const done=new Promise(resolve=>{recorder.ondataavailable=e=>chunks.push(e.data);recorder.onstop=resolve;});
  recorder.start();for(let i=0;i<25;i++){ctx.fillStyle=i%2?'#184e72':'#d8e7f0';ctx.fillRect(0,0,320,180);ctx.fillStyle='white';ctx.fillRect((i*11)%280,45,40,60);for(let j=0;j<32;j++){ctx.fillStyle=`rgb(${nonce[j]},${nonce[(j+1)%32]},${nonce[(j+2)%32]})`;ctx.fillRect(j*10,140,10,40);}await new Promise(r=>setTimeout(r,100));}recorder.stop();await done;stream.getTracks().forEach(t=>t.stop());oscillator?.stop();if(audio)await audio.close();return Array.from(new Uint8Array(await new Blob(chunks).arrayBuffer()));
 },withAudio);
 const bytes=Buffer.from(result);if(bytes.length<2048||bytes.length>1024*1024)throw new Error('Unexpected anonymous fixture size.');await mkdir(dirname(target),{recursive:true});await writeFile(target,bytes,{flag:'wx'});await writeFile(target+'.json',JSON.stringify({utc:new Date().toISOString(),generator:'Installed Microsoft Edge MediaRecorder canvas, random anonymous pixels; optional synthetic oscillator, no microphone/external media/name/PII',withAudio,browser:browser.version(),bytes:bytes.length,sha256:createHash('sha256').update(bytes).digest('hex'),rendition:false,sourceImported:false},null,2),{flag:'wx'});console.log(`Anonymous real WebM fixture ${bytes.length} bytes; not a rendition or FFmpeg proof.`);
}finally{await browser.close();}
