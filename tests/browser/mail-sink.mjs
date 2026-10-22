import net from 'node:net';
import { mkdir,writeFile } from 'node:fs/promises';
import { resolve,relative,isAbsolute } from 'node:path';
import crypto from 'node:crypto';
const port=Number(process.argv[2]),folder=resolve(process.argv[3]||'');
const allowed=resolve('.local/tests/phase2c/mail'),rel=relative(allowed,folder);
if(port<1024||port>65535||!Number.isInteger(port)||rel.startsWith('..')||isAbsolute(rel)||!rel)throw new Error('Sink requires an explicit unique project Test directory and high loopback port.');
await mkdir(folder,{recursive:true});
const server=net.createServer(socket=>{
 socket.setEncoding('utf8');socket.setTimeout(20000,()=>socket.destroy());socket.write('220 LandPortal local Test sink\r\n');let pending='',data=false,body='';
 let chain=Promise.resolve();
 socket.on('error',()=>{});
 socket.on('data',chunk=>{pending+=chunk;if(pending.length+body.length>65536){socket.destroy();return;}let at;while((at=pending.indexOf('\r\n'))>=0){const line=pending.slice(0,at);pending=pending.slice(at+2);chain=chain.then(async()=>{
  if(data){if(line==='.') {const id=crypto.randomUUID();await writeFile(resolve(folder,id+'.eml'),body,'utf8');data=false;body='';socket.write('250 Accepted to local Test sink\r\n');console.log('Accepted local Test mail; private file ID '+id);}else body+=line+'\r\n';return;}
  if(line==='DATA'){data=true;socket.write('354 End with dot\r\n');}else if(line.startsWith('EHLO')||line.startsWith('MAIL FROM:')||line.startsWith('RCPT TO:'))socket.write('250 OK\r\n');else if(line==='QUIT')socket.end('221 Bye\r\n');else socket.write('500 Unsupported\r\n');
 }).catch(()=>socket.destroy());}});
});
server.listen(port,'127.0.0.1',()=>console.log('SMTP Test sink listening on loopback only. No relay to external addresses.'));
process.on('SIGINT',()=>server.close(()=>process.exit(0)));
