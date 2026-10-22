"""Offline, read-only coverage and isolated restore readback. No SQL/network writes."""
import argparse
import collections
import csv
import hashlib
import html
from html.parser import HTMLParser
import json
from pathlib import Path
import re
from urllib.parse import unquote, urlsplit, urlunsplit
import brotli

ROOT = Path(__file__).resolve().parents[1]
KINDS = ['Article', 'Announcement', 'StaticPage', 'LegalDocument', 'DraftDocument', 'Video']
CHECKPOINT = '.local/phase3a/9d95c26f85a54a8aa2df61def03cf360/checkpoints/739776f08d05a72048eeff9d10e34e318a58411873e6b2656d0fc53758996e8e.bin'
PREP = '.local/evidence/phase3f/phase3i-20261021T080800Z-c4f2ee3b'
BULK = '.local/evidence/phase3f/phase3i-bulk-20261021T084908Z-f93fdf57'


def read(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def unwrap(value):
    return value['value'] if isinstance(value, dict) and set(value) == {'value', 'Count'} else value


def write(path, value):
    with Path(path).open('x', encoding='utf-8') as f:
        json.dump(value, f, ensure_ascii=False, indent=2)


def sha(path):
    with Path(path).open('rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()


def urlkey(value):
    u = urlsplit(html.unescape(value))
    return urlunsplit((u.scheme.lower(), u.netloc.lower(), unquote(u.path), unquote(u.query), ''))


def identity(kind, source_id):
    return f'{KINDS[kind]}:{source_id}'


class Text(HTMLParser):
    def __init__(self):
        super().__init__()
        self.parts = []
    def handle_data(self, data):
        self.parts.append(data)


def text(value):
    p = Text()
    p.feed(value or '')
    return re.sub(r'\s+', '', ''.join(p.parts))


def audit(out, dest, checkpoint=CHECKPOINT, coverage=None, test_manifest=None):
    before = read(out / 'dev-before.json')
    cp = ROOT / checkpoint
    assert cp.resolve().is_relative_to((ROOT / '.local/phase3a').resolve()), 'Private checkpoint required'
    assert sha(cp) == cp.stem, 'Checkpoint hash mismatch'
    state = json.loads(brotli.decompress(cp.read_bytes()[8:]))
    sources = {identity(x['Kind'], x['SourceId']): x for x in before['groups']['identities']}
    content = {x['Id'].lower(): x for x in read(out / 'contents.json')}
    public = {x['Id'].lower(): x for x in before['groups']['publicPayload']}
    category_links = collections.defaultdict(set)
    for x in before['groups']['categoryMappings']:
        category_links[x['ContentId'].lower()].add(x['CategoryId'])
    canonical_owners = collections.defaultdict(list)
    for key, record in sources.items():
        canonical_owners[urlkey(record['CanonicalUrl'])].append(key)
    media = {x['Id'].lower(): x for x in before['groups']['media']}
    media_sha = collections.defaultdict(list)
    media_url = collections.defaultdict(list)
    for m in media.values():
        media_sha[m['Sha256']].append(m)
        media_url[urlkey(m['SourceUrl'])].append(m)
    nodes = {urlkey(x['Url']): x for x in state['Urls'] if x['Role'] == 'File'}
    captures = collections.defaultdict(list)
    for c in state['Captures']:
        if c['Status'] == 200:
            captures[urlkey(c['Url'])].append(c)
    links = collections.defaultdict(set)
    for a in read(out / 'content-assets.json'):
        links[a['RevisionId'].lower()].add(a['MediaAssetId'].lower())
    raw_root = ROOT / '.local/phase3a' / state['Id'].replace('-', '')
    verified_blobs = {}
    def blob_valid(capture):
        key = capture['Key']
        if key not in verified_blobs:
            path = (raw_root / key).resolve()
            verified_blobs[key] = (path.is_relative_to(raw_root.resolve()) and path.is_file()
                                   and path.stat().st_size == capture['Bytes'] and sha(path) == capture['Sha256'])
        return verified_blobs[key]
    rows = []
    for item in state['Items']:
        s = item['Source']; key = identity(s['Kind'], s['SourceId']); old = sources.get(key)
        c = content.get(old['ContentId'].lower()) if old else None
        published_links = links[c['PublishedRevisionId'].lower()] if c and c['PublishedRevisionId'] else set()
        files = []
        for u in sorted(set(s['AssetUrls'])):
            node = nodes.get(urlkey(u))
            cached = captures.get(urlkey(u), [])
            possible = {x['Id'].lower(): x for x in media_url.get(urlkey(u), [])}
            for cap in cached:
                possible.update({m['Id'].lower(): m for m in media_sha.get(cap['Sha256'], [])})
            used = [m for k, m in possible.items() if k in published_links]
            expected_hash = node.get('CaptureSha') if node else (cached[-1]['Sha256'] if cached else None)
            exact = [m for m in used if m['Sha256'] == expected_hash]
            file_state = 'CLEAN_LINKED' if any(m['ScanStatus'] == 'Clean' and not m['IsQuarantined'] for m in exact) else 'NOT_SCANNED_LINKED' if exact else 'SOURCE_BYTES_DIFFER' if used else 'CACHED_NOT_LINKED' if cached else 'MISSING_BYTES'
            if file_state == 'CLEAN_LINKED' and not any(c['Sha256'] == expected_hash and blob_valid(c) for c in cached):
                file_state = 'CAPTURE_BYTES_UNVERIFIED'
            if node and node.get('FormatStatus') not in (None, 'Validated'):
                file_state = 'FORMAT_REVIEW'
            files.append({'url': u, 'state': file_state, 'nodeStatus': node.get('Status') if node else None,
                          'format': node.get('FormatStatus') if node else None,
                          'error': node.get('Error') if node else None,
                          'cachedHashes': sorted({x['Sha256'] for x in cached}),
                          'linkedMedia': [{'id': m['Id'], 'sha256': m['Sha256'], 'scan': m['ScanStatus']} for m in used]})
        metadata = bool(c and c['Title'] == s['Title'] and (c['Author'] or '') == s['Author']
                        and public[c['Id'].lower()]['Summary'] == s['Summary']
                        and c['OriginalDateText'] == s['OriginalDateText'] and c['DatePrecision'] == s['DatePrecision']
                        and set(s['CategoryIds']).issubset(category_links[c['Id'].lower()])
                        and c['SourcePublishedDate'] == s['PublishedDate'] and text(c['BodyHtml']) == text(s['BodyHtml']))
        issues = sorted(set(x['state'] for x in files if x['state'] != 'CLEAN_LINKED'))
        if s['Kind'] == 5:
            issues.append('VIDEO_METADATA_NO_FILE')
        if c and not metadata:
            issues.append('PUBLIC_SOURCE_TEXT_OR_METADATA_REVIEW')
        if not any(cap['Sha256'] == item['RawSha256'] and blob_valid(cap) for cap in state['Captures']):
            issues.append('DETAIL_RAW_BYTES_UNVERIFIED')
        complete = bool(c and metadata and not issues)
        rows.append({'identity': key, 'canonical': s['CanonicalUrl'], 'kind': KINDS[s['Kind']],
                     'title': s['Title'], 'contentId': old['ContentId'] if old else None,
                     'categories': s['CategoryIds'], 'metadataPublicMatches': metadata,
                     'canonicalDevOwners': canonical_owners.get(urlkey(s['CanonicalUrl']), []),
                     'status': 'COMPLETE_VERIFIED_CACHE' if complete else 'DEV_PRESENT_WITH_GAPS' if old else 'NOT_IN_DEV',
                     'issues': issues, 'files': files})
    write(dest / 'content-completeness.json', rows)
    ledger = read(ROOT / PREP / 'remaining-52.json')['rows']
    row_index = {x['identity']: x for x in rows}
    classified = []
    aliases = {urlkey(x['Url']): x['ContentId'].lower() for x in before['groups']['aliases']}
    for b in ledger:
        current = row_index.get(b['identity'])
        reasons = []
        for reason in b['historicalReasons']:
            code, _, u = reason.partition(':')
            related = next((x for x in current['files'] if urlkey(x['url']) == urlkey(u)), None) if current and u.startswith('http') else None
            reasons.append({'historicalCode': code, 'url': u if u.startswith('http') else None,
                            'currentResourceState': related['state'] if related else None,
                            'aliasOwner': aliases.get(urlkey(u)) if u.startswith('http') else None})
        actionable = 'PROTECTED_DEV_NO_SCAN_AUTHORITY' if b['alreadyDev'] else 'CANONICAL_ALREADY_DEV_OTHER_TYPE_NEEDS_MAPPING_REVIEW' if current and current['canonicalDevOwners'] else 'STAGING_NEEDS_RESOURCE_OR_ALIAS_REVIEW'
        classified.append({**b, 'current': current, 'reasonReadback': reasons, 'decision': actionable,
                           'safeToImportNow': False,
                           'nextAction': 'Cần chính sách/quyền riêng cho bản gốc Dev; giữ nguyên published.' if b['alreadyDev'] else 'Khôi phục đúng tài nguyên/đối chiếu canonical, scanner thật, fresh Test preview; không bỏ guard.'})
    write(dest / 'blocked-52.json', classified)
    with (dest / 'blocked-52.csv').open('x', encoding='utf-8-sig', newline='') as f:
        w = csv.writer(f); w.writerow(['SourceIdentity','ExistsDev','Decision','HistoricalGroups','CurrentFileStates','Reasons','NextAction'])
        for x in classified:
            w.writerow([x['identity'], x['alreadyDev'], x['decision'], '|'.join(x['groups']),
                        '|'.join(sorted({f['state'] for f in x['current']['files']})) if x['current'] else '',
                        '|'.join(x['historicalReasons']), x['nextAction']])
    cov = read(coverage or ROOT / '.local/evidence/phase3f/phase3f-20261019T070725Z-fa27c9d0/final-source-audit/coverage.json')
    pending = [x for x in cov['Candidates'] if x['Status'] != 'VERIFIED']
    write(dest / 'pending-candidates.json', pending)
    category = []
    for cat in before['groups']['categories']:
        items = [x for x in rows if cat['Id'] in x['categories']]
        listed = [x for x in state['Lists'] if x['CategoryId'] == cat['Id']]
        observed = sorted({x['Page'] for x in listed})
        declared = max([x['DeclaredPages'] for x in listed] or [0])
        category.append({'id': cat['Id'], 'name': cat['Name'], 'knownStagedTyped': len(items),
                         'devPresentTyped': sum(bool(x['contentId']) for x in items),
                         'completeTyped': sum(x['status'] == 'COMPLETE_VERIFIED_CACHE' for x in items),
                         'observedPages': observed, 'maxDeclaredPages': declared,
                         'unobservedDeclaredPages': [i for i in range(1, declared+1) if i not in observed],
                         'listResults': sorted({x['Result'] for x in listed}), 'sourceTotal': None,
                         'coverage': 'UNKNOWN_NO_FULL_COVERAGE_ACCEPTANCE'})
    write(dest / 'category-coverage.json', category)
    kinds = []
    for kind in KINDS:
        selected = [x for x in rows if x['kind'] == kind]
        kinds.append({'kind': kind, 'stagedTyped': len(selected), 'devPresent': sum(bool(x['contentId']) for x in selected),
                      'complete': sum(x['status'] == 'COMPLETE_VERIFIED_CACHE' for x in selected),
                      'absent': sum(not x['contentId'] for x in selected), 'sourceTotal': None})
    ledger_canon = {urlkey(x['current']['canonical']) for x in classified if x['current']}
    imported = read(test_manifest) if test_manifest else None
    summary = {'status': 'PARTIAL', 'checkpoint': str(checkpoint), 'checkpointSha256': sha(cp),
               'dev': before['counts'], 'urls': len(state['Urls']), 'typedStaged': len(rows),
               'canonicalStaged': len({x['canonical'] for x in rows}), 'candidateIdentities': cov['CandidateIdentities'],
               'completeVerifiedCache': sum(x['status'] == 'COMPLETE_VERIFIED_CACHE' for x in rows),
               'devPresentWithGaps': sum(x['status'] == 'DEV_PRESENT_WITH_GAPS' for x in rows),
               'absentDevTyped': sum(not x['contentId'] for x in rows),
               'absentDevCanonical': len({x['canonical'] for x in rows if not x['contentId']}),
               'absentTypedCanonicalAlreadyDev': sum(not x['contentId'] and bool(x['canonicalDevOwners']) for x in rows),
               'genuinelyAbsentCanonical': len({urlkey(x['canonical']) for x in rows if not x['contentId'] and not x['canonicalDevOwners']}),
               'pending': len(pending), 'missingDetailSnapshot': sum(not x['CaptureHashes'] for x in pending),
               'parserReview': sum(bool(x['CaptureHashes']) for x in pending),
               'blockedRows': len(classified), 'blockedDistinctTyped': len({x['identity'] for x in classified}),
               'blockedDistinctCanonical': len(ledger_canon),
               'blockedCanonicalAlreadyDev': len(ledger_canon & {key for key, owners in canonical_owners.items() if owners}),
               'blockedExistingDev': sum(x['alreadyDev'] for x in classified),
               'blockedStaging': sum(not x['alreadyDev'] for x in classified),
               'historicalOverlappingCauses': dict(collections.Counter(g for x in classified for g in x['groups'])),
               'safeAdditionalImportsNotYetImported': 0, 'newTestImports': imported['counts']['contents'] if imported else 0, 'newDevImports': 0,
               'newTestManifest': str(test_manifest) if test_manifest else None,
               'devBatchApproval': 'NOT_READY; needs fresh strict recovery proof/Dev preview/exact approval',
               'sourceBlobHashesChecked': len(verified_blobs),
               'sourceTotal': 'UNKNOWN', 'byKind': kinds,
               'uniqueUnresolvedAssetUrls': len({urlkey(f['url']) for x in rows for f in x['files'] if f['state'] != 'CLEAN_LINKED'}),
               'uniqueMissingBytesUrls': len({urlkey(f['url']) for x in rows for f in x['files'] if f['state'] == 'MISSING_BYTES'}),
               'resourceReferenceStates': dict(collections.Counter(f['state'] for x in rows for f in x['files'] if f['state'] != 'CLEAN_LINKED')),
               'originalNotScanned': sum(m['ScanStatus'] == 'NotScanned' for m in media.values()),
               'knownAliasRows': len(before['groups']['aliases']),
               'categoryCount': len(category), 'fullCategoryCoverageProved': 0,
               'warning': 'Typed identities, canonical URLs, aliases and reasons overlap. Do not add them. Complete means cached snapshot/text/metadata and required clean linked bytes; not complete live website coverage.'}
    write(dest / 'coverage-summary.json', summary)
    print(json.dumps(summary, ensure_ascii=False, indent=2))


def verify_restore(out):
    r = out / 'restore'; actual = read(r / 'actual-restore.json')
    expected = read(ROOT / BULK / 'b13/all-records-before.json')
    errors = []
    for old, new in [('contents','contents'),('revisions','revisions'),('sourceRecords','source-records')]:
        if unwrap(expected[old]) != read(r / (new+'.json')):
            errors.append('ALL_COLUMNS:'+old)
    base = read(ROOT / BULK / 'b13/dev-before.json')
    assets = read(r / 'media.json'); media = {x['Id'].lower(): x for x in assets}
    contents = {x['Id'].lower(): x for x in read(r / 'contents.json')}
    revisions = {x['Id'].lower(): x for x in read(r / 'revisions.json')}
    root = Path(actual['fileRoot']); checks = []
    for m in assets:
        p = root / 'media' / m['StorageKey']
        resolved = p.resolve()
        if not resolved.is_relative_to((root / 'media').resolve()) or not p.is_file() or p.stat().st_size != m['ByteLength'] or sha(p) != m['Sha256']:
            errors.append('MEDIA_BYTES:'+m['Id'])
    for snap in read(r / 'snapshots.json'):
        p = root / 'snapshots' / snap['StorageKey']
        if not p.resolve().is_relative_to((root / 'snapshots').resolve()) or not p.is_file() or sha(p) != snap['Sha256']:
            errors.append('SNAPSHOT_BYTES:'+str(snap['Id']))
    for c in contents.values():
        for key in ['CurrentRevisionId','PublishedRevisionId']:
            if c[key] and (c[key].lower() not in revisions or revisions[c[key].lower()]['ContentId'].lower() != c['Id'].lower()):
                errors.append('REVISION_POINTER:'+c['Id'])
    for link in read(r / 'links.json'):
        if link['RevisionId'].lower() not in revisions or link['MediaAssetId'].lower() not in media:
            errors.append('LINK:'+str(link))
    expected_media = {x['Id'].lower(): x for x in base['groups']['media']}
    for key, m in media.items():
        if m['ScanStatus'] != expected_media[key]['ScanStatus'] or m['IsQuarantined'] != expected_media[key]['IsQuarantined']:
            errors.append('SCAN_OR_QUARANTINE:'+key)
    schema = read(r / 'schema.json'); live_schema = read(out / 'schema-columns.json')
    if schema != live_schema:
        errors.append('SCHEMA_COLUMNS')
    if read(r / 'migrations.json') != read(out / 'schema-migrations.json'):
        errors.append('MIGRATIONS')
    # Offline representative reads use actual restored revisions and restored media bytes.
    for kind in [0,3,4]:
        c = next(x for x in contents.values() if x['Kind'] == kind and x['PublishedRevisionId'])
        rev = revisions[c['PublishedRevisionId'].lower()]
        assert rev['Title'] and rev['BodyHtml']
        linked = [media[x['MediaAssetId'].lower()] for x in read(r / 'links.json') if x['RevisionId'].lower() == rev['Id'].lower()]
        checks.append({'kind': KINDS[kind], 'contentId': c['Id'], 'revisionId': rev['Id'], 'title': rev['Title'],
                       'bodySha256': hashlib.sha256(rev['BodyHtml'].encode()).hexdigest(),
                       'assets': len(linked), 'scans': dict(collections.Counter(x['ScanStatus'] for x in linked))})
    result = {'status': 'PASS' if not errors else 'FAIL', 'database': actual['database'], 'actualSqlRestore': True,
              'backupContents': len(contents), 'backupRevisions': len(revisions), 'backupMedia': len(media),
              'backupAliases': len(read(r / 'aliases.json')), 'snapshots': len(read(r / 'snapshots.json')),
              'originalNotScannedPreserved': sum(x['ScanStatus'] == 'NotScanned' for x in assets),
              'allColumnsTables': ['Contents','Revisions','SourceRecords'], 'allColumnsMatch': not any(e.startswith('ALL_COLUMNS') for e in errors),
              'mediaByteHashesChecked': len(assets), 'checkdb': read(r / 'checkdb.json')['status'],
              'schemaMatches': schema == live_schema, 'readOnly': read(r / 'state.json')[0]['is_read_only'],
              'fileRestore': read(r / 'file-restore.json'), 'representativeReads': checks,
              'httpOnRestoredDb': 'NOT_RUN; isolated read-only DB is never bound to public Web/Worker',
              'errors': errors, 'currentDevContents': 250, 'backupDoesNotContainLast15': True}
    write(r / 'independent-readback.json', result)
    print(json.dumps({k:v for k,v in result.items() if k != 'fileRestore'}, ensure_ascii=False, indent=2))
    if errors:
        raise SystemExit(2)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('action', choices=['coverage','restore-readback'])
    parser.add_argument('--run', required=True, type=Path)
    parser.add_argument('--output', type=Path, help='New subdirectory for a new coverage audit revision')
    parser.add_argument('--checkpoint', type=Path, default=Path(CHECKPOINT))
    parser.add_argument('--coverage', type=Path)
    parser.add_argument('--test-manifest', type=Path)
    args = parser.parse_args()
    out = args.run.resolve()
    if not out.is_relative_to((ROOT / '.local/evidence/phase3j').resolve()):
        parser.error('A private Phase3J run path is required')
    if args.action == 'coverage':
        dest = args.output.resolve() if args.output else out
        if dest != out:
            if not dest.is_relative_to(out):
                parser.error('Output must be within the same private run')
            dest.mkdir(exist_ok=False)
        audit(out, dest, args.checkpoint, args.coverage, args.test_manifest)
    else:
        verify_restore(out)
