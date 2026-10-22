"""Exact-byte project backup copy/readback. Windows extended paths; no ACL changes."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import stat

PROJECT = Path(__file__).resolve().parent.parent


def extended(path):
    value = os.path.abspath(path)
    return '\\\\?\\' + value if os.name == 'nt' and not value.startswith('\\\\?\\') else value


def safe(path, *, backup=False):
    path = Path(os.path.abspath(path))
    allowed = PROJECT / '.local' / ('backups' if backup else '')
    if not path.is_relative_to(allowed) and not (not backup and path == PROJECT / '.env'):
        raise ValueError('Path outside permitted project backup/source scope')
    cursor = path
    while cursor != cursor.parent:
        if os.path.lexists(extended(cursor)):
            info = os.lstat(extended(cursor))
            if stat.S_ISLNK(info.st_mode) or getattr(info, 'st_file_attributes', 0) & 0x400:
                raise ValueError('Reparse/symlink refused')
        cursor = cursor.parent
    return path


def digest(path):
    with open(extended(path), 'rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def write_new(path, value):
    with open(extended(path), 'x', encoding='utf-8') as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)


def listing(source, key):
    source = safe(source)
    key = Path(key)
    if key.is_absolute() or '..' in key.parts or not key.parts:
        raise ValueError('Unsafe backup key')
    if os.path.isdir(extended(source)):
        dirs, files = [key.as_posix()], []
        def fail_walk(error):
            raise error  # Never silently omit an unreadable source directory.
        for root, subdirs, names in os.walk(extended(source), followlinks=False, onerror=fail_walk):
            # Strip the extended prefix for the metadata only, retain full names.
            normal = root[4:] if root.startswith('\\\\?\\') else root
            relative = Path(normal).relative_to(source)
            for name in subdirs:
                safe(Path(normal) / name)
                dirs.append((key / relative / name).as_posix())
            for name in names:
                path = safe(Path(normal) / name)
                if not stat.S_ISREG(os.stat(extended(path)).st_mode):
                    raise ValueError('Non-regular source file')
                files.append({'source': str(path), 'key': (key / relative / name).as_posix(),
                              'bytes': os.path.getsize(extended(path)), 'sha256': digest(path)})
        return dirs, files
    if not os.path.isfile(extended(source)):
        raise ValueError('Source does not exist')
    return [], [{'source': str(source), 'key': key.as_posix(),
                 'bytes': os.path.getsize(extended(source)), 'sha256': digest(source)}]


def inventory(spec):
    dirs, files = [], []
    for mapping in spec:
        ds, fs = listing(mapping['source'], mapping['key'])
        dirs.extend(ds)
        files.extend(fs)
    keys = [f['key'].casefold() for f in files]
    if len(keys) != len(set(keys)):
        raise ValueError('Duplicate/case-colliding backup key')
    return sorted(set(dirs)), sorted(files, key=lambda f: f['key'])


def capture(spec, dest):
    dest = safe(dest, backup=True)
    directories, files = inventory(spec)
    for directory in directories:
        target = safe(dest / directory, backup=True)
        os.makedirs(extended(target), exist_ok=True)
    for entry in files:
        target = safe(dest / entry['key'], backup=True)
        os.makedirs(extended(target.parent), exist_ok=True)
        with open(extended(entry['source']), 'rb') as src, open(extended(target), 'xb') as out:
            shutil.copyfileobj(src, out, 1024 * 1024)
            out.flush()
            os.fsync(out.fileno())
        if digest(target) != entry['sha256'] or os.path.getsize(extended(target)) != entry['bytes']:
            raise ValueError('Copied bytes differ')
    if inventory(spec) != (directories, files):
        raise ValueError('Source inventory changed during backup')
    return {'destination': str(dest), 'mappings': spec, 'directories': directories, 'files': files,
            'fileCount': len(files), 'bytes': sum(f['bytes'] for f in files), 'status': 'PASS',
            'maxDestinationPathLength': max((len(str(dest / f['key'])) for f in files), default=0)}


def verify(manifest, sources=False):
    dest = safe(manifest['destination'], backup=True)
    for directory in manifest['directories']:
        if not os.path.isdir(extended(safe(dest / directory, backup=True))):
            raise ValueError('Backup directory missing')
    for entry in manifest['files']:
        target = safe(dest / entry['key'], backup=True)
        if digest(target) != entry['sha256'] or os.path.getsize(extended(target)) != entry['bytes']:
            raise ValueError('Backup readback differs')
    if sources and inventory(manifest['mappings']) != (manifest['directories'], manifest['files']):
        raise ValueError('Original source inventory differs from backed-up manifest')
    return {'status': 'PASS', 'files': len(manifest['files']), 'bytes': manifest['bytes'],
            'mismatches': 0, 'sourceInventoryRechecked': sources, 'sqlRestorePerformed': False}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=['capture', 'verify', 'restore-check'])
    parser.add_argument('--spec')
    parser.add_argument('--manifest')
    parser.add_argument('--dest')
    parser.add_argument('--verify-sources', action='store_true')
    parser.add_argument('--report', required=True)
    args = parser.parse_args()
    safe(args.report)
    if args.action == 'capture':
        spec = json.loads(Path(args.spec).read_text(encoding='utf-8-sig'))
        value = capture(spec, args.dest)
    else:
        manifest = json.loads(Path(args.manifest).read_text(encoding='utf-8-sig'))
        value = verify(manifest, args.verify_sources)
        if args.action == 'restore-check':
            destination = safe(args.dest, backup=True)
            if os.path.exists(extended(destination)):
                raise ValueError('Restore check requires a new isolated destination')
            for directory in manifest['directories']:
                os.makedirs(extended(safe(destination / directory, backup=True)), exist_ok=True)
            spec = [{'source': str(Path(manifest['destination']) / f['key']), 'key': f['key']}
                    for f in manifest['files']]
            restored = capture(spec, destination)
            if [(f['key'], f['bytes'], f['sha256']) for f in restored['files']] != [
                    (f['key'], f['bytes'], f['sha256']) for f in manifest['files']]:
                raise ValueError('File restore differs')
            value = verify(restored)
            value['isolatedFileRestoreDestination'] = str(destination)
    write_new(args.report, value)
    print(f"PASS {args.action}: {value.get('fileCount', value.get('files'))} files")


if __name__ == '__main__':
    main()
