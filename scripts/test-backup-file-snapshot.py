"""Focused technical fixtures only; never an application/SQL restore acceptance."""
import importlib.util
from pathlib import Path
import uuid

spec = importlib.util.spec_from_file_location('snapshot', Path(__file__).with_name('backup-file-snapshot.py'))
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
root = m.PROJECT / '.local' / 'backups' / ('fixture-' + uuid.uuid4().hex[:8])
source = root / 'source' / ('long-segment-' * 7) / ('second-segment-' * 7)
import os
os.makedirs(m.extended(source / 'empty'))
file = source / 'tieng-Viet-long-path.txt'
with open(m.extended(file), 'xb') as out:
    out.write('Tệp kiểm thử kỹ thuật; không phải dữ liệu nguồn'.encode('utf-8'))
assert len(str(file)) > 260
manifest = m.capture([{'source': str(root / 'source'), 'key': 'snapshots'}], root / 'copy')
assert m.verify(manifest)['mismatches'] == 0
assert any(key.endswith('/empty') for key in manifest['directories'])
passed = ['extended-path-copy-hash-and-empty-directory']
try:
    m.capture([{'source': str(root / 'source'), 'key': 'snapshots'}], root / 'copy')
    raise AssertionError('Overwrite accepted')
except FileExistsError:
    passed.append('overwrite-refused')
target = root / 'copy' / manifest['files'][0]['key']
with open(m.extended(target), 'ab') as out:
    out.write(b'tamper')
try:
    m.verify(manifest)
    raise AssertionError('Tampering accepted')
except ValueError:
    passed.append('tamper-refused')
try:
    m.safe(m.PROJECT.parent / 'outside')
    raise AssertionError('Escape accepted')
except ValueError:
    passed.append('scope-escape-refused')
print({'status': 'PASS', 'checks': passed, 'count': len(passed), 'fixture': str(root)})
