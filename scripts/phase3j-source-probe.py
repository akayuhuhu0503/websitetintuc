"""Finite sequential public gdla transport diagnosis; no TLS bypass or source writes."""
import datetime
import hashlib
import http.client
import ipaddress
import json
from pathlib import Path
import socket
import ssl
import sys
import time

out = Path(sys.argv[1]).resolve()
root = Path(__file__).resolve().parents[1]
if not out.is_relative_to((root / '.local/evidence/phase3j').resolve()):
    raise SystemExit('Private Phase3J output required')
report = out / 'gdla-probe.json'
if report.exists():
    raise SystemExit('Unique evidence required')
host = 'gdla.gov.vn'
result = {'utc': datetime.datetime.now(datetime.timezone.utc).isoformat(), 'host': host,
          'tlsVerification': 'default system certificate/hostname verification; never disabled',
          'maximumHttpRequests': 4, 'concurrency': 1, 'minimumPauseSeconds': 2, 'attempts': []}
try:
    addresses = sorted({x[4][0] for x in socket.getaddrinfo(host, None, type=socket.SOCK_STREAM)})
    if any(not ipaddress.ip_address(a).is_global for a in addresses):
        raise ValueError('Non-public DNS address refused')
    result['dns'] = {'status': 'PASS', 'addresses': addresses}
except Exception as e:
    result['dns'] = {'status': 'FAIL', 'error': type(e).__name__, 'detail': str(e)}
    addresses = []
if addresses:
    try:
        with socket.create_connection((host,443),timeout=6) as raw:
            result['tcp443'] = 'CONNECTED'
            with ssl.create_default_context().wrap_socket(raw,server_hostname=host) as tls:
                cert = tls.getpeercert()
                result['tls'] = {'status':'PASS','version':tls.version(),'expires':cert.get('notAfter')}
    except Exception as e:
        result.setdefault('tcp443','NOT_CONNECTED')
        result['tls'] = {'status':'FAIL' if result['tcp443']=='CONNECTED' else 'NOT_RUN_TCP_FAILED',
                         'error':type(e).__name__,'detail':str(e)}
    for scheme in ['https','http']:
        robots_allowed = False
        for path in ['/robots.txt','/']:
            if path == '/' and not robots_allowed:
                continue
            time.sleep(2)
            attempt = {'url':f'{scheme}://{host}{path}', 'utc':datetime.datetime.now(datetime.timezone.utc).isoformat()}
            connection = (http.client.HTTPSConnection(host,timeout=6,context=ssl.create_default_context())
                          if scheme=='https' else http.client.HTTPConnection(host,timeout=6))
            try:
                connection.request('GET',path,headers={'User-Agent':'LandPortalMigration/3J (public source readiness; bounded)','Connection':'close'})
                response = connection.getresponse()
                data = response.read(512*1024+1)
                attempt.update(status=response.status,contentType=response.getheader('Content-Type'),
                               location=response.getheader('Location'),bytes=len(data),sha256=hashlib.sha256(data).hexdigest())
                if len(data)>512*1024:
                    raise ValueError('Response exceeds bounded probe size')
                if response.status==200:
                    name='gdla-'+scheme+('-robots.txt' if path=='/robots.txt' else '-home.html')
                    with (out/name).open('xb') as f:
                        f.write(data)
                    attempt['snapshot']=name
                if path=='/robots.txt':
                    if response.status==404:
                        robots_allowed=True
                    elif response.status==200:
                        import urllib.robotparser
                        robots=urllib.robotparser.RobotFileParser()
                        robots.parse(data.decode('utf-8',errors='replace').splitlines())
                        robots_allowed=robots.can_fetch('LandPortalMigration',f'{scheme}://{host}/')
                if response.status in [429,503]:
                    attempt['stop']='SOURCE_OVERLOAD_NO_RETRY'
            except Exception as e:
                attempt.update(error=type(e).__name__,detail=str(e))
            finally:
                connection.close()
            result['attempts'].append(attempt)
            if attempt.get('stop'):
                break
result['realNukeVietContentsInventoried']=0
result['sqlImports']=0
with report.open('x',encoding='utf-8') as f:
    json.dump(result,f,ensure_ascii=False,indent=2)
print(json.dumps(result,ensure_ascii=False,indent=2))
