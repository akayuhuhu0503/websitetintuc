"""Offline PDF derivatives only; never changes a source archive, Excel date, CMS or SQL."""
import argparse, collections, hashlib, json, re, zipfile
from pathlib import Path
import fitz

def sha(path):
    with open(path, 'rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def dump(path, data):
    with open(path, 'x', encoding='utf-8') as stream:
        json.dump(data, stream, ensure_ascii=False, indent=2)

def compare(original, derivative):
    pages, image_diffs, text_lost, render_diffs, geometry_diffs, text_extra = [], [], [], [], [], []
    with fitz.open(original) as a, fitz.open(derivative) as b:
        if len(a) != len(b):
            raise ValueError('Page count changed')
        for n in range(len(a)):
            left, right = a[n], b[n]
            if (left.rect != right.rect or left.rotation != right.rotation or left.cropbox != right.cropbox or left.mediabox != right.mediabox):
                geometry_diffs.append(n+1)
            # Complete page appearance in memory, including visible signature widgets; no screenshots saved.
            pa = left.get_pixmap(dpi=96, alpha=False, annots=True)
            pb = right.get_pixmap(dpi=96, alpha=False, annots=True)
            ha, hb = hashlib.sha256(pa.samples).hexdigest(), hashlib.sha256(pb.samples).hexdigest()
            if (pa.width,pa.height,ha) != (pb.width,pb.height,hb): render_diffs.append(n+1)
            ta = collections.Counter(w[4] for w in left.get_text('words'))
            tb = collections.Counter(w[4] for w in right.get_text('words'))
            if ta-tb: text_lost.append(n+1)
            if tb-ta: text_extra.append(n+1)
            # Every decoded image digest is checked at original resolution, not just preview resolution.
            ia = collections.Counter((i['width'],i['height'],i['digest'].hex()) for i in left.get_image_info(hashes=True))
            ib = collections.Counter((i['width'],i['height'],i['digest'].hex()) for i in right.get_image_info(hashes=True))
            if ia-ib: image_diffs.append(n+1)
            pages.append({'page':n+1,'sourceRenderSha256':ha,'derivedRenderSha256':hb,'sourceWordCount':sum(ta.values()),'derivedWordCount':sum(tb.values()),'sourceImages':sum(ia.values()),'derivedImages':sum(ib.values())})
    return {'pages':len(pages),'allPagesCompared':True,'renderDpi':96,'geometryMismatchPages':geometry_diffs,'renderMismatchPages':render_diffs,
        'textMissingPages':text_lost,'textAddedPages':text_extra,'imageMissingPages':image_diffs,
        'exactContentPass':not (geometry_diffs or render_diffs or text_lost or text_extra or image_diffs),'pageChecks':pages}

def normalize(output, folder='normalized', selected=None, diagnostics=None):
    details = json.loads((output/'source-details.json').read_text(encoding='utf-8'))
    if selected is not None: details = [x for x in details if x['stt'] in selected]
    derived, proofs = output/folder, output/(folder+'-proofs')
    derived.mkdir(exist_ok=False)
    proofs.mkdir(exist_ok=False)
    checks, results = [], []
    fitz.TOOLS.mupdf_display_errors(False)
    fitz.TOOLS.mupdf_display_warnings(False)
    for item in details:
        stt = item['stt']; original = output/'originals'/item['file']; target = derived/item['file']
        if sha(original) != item['sha256']: raise ValueError('Source copy changed')
        result = {'stt':stt,'sourceEntry':item['sourceEntry'],'sourceSha256':item['sha256'],'sourceBytes':item['bytes'], 'path':str(target.resolve()),
            'method':'lossless-clean-deflate','signatureObjectsInSource':len(item['signatureObjects']),'cryptographicSignaturePreserved':not bool(item['signatureObjects']),
            'scanStatus':'NotScanned','cmsWrites':0}
        print(f'Normalize STT {stt} ...',flush=True)
        try:
            with fitz.open(original) as doc:
                if doc.needs_pass or doc.embfile_count(): raise ValueError('Password/embedded documents require separate review')
                # Keep visible widget/annotation appearances as static page content; originals retain signed bytes.
                if item['widgets'] or item['annotations']: doc.bake(annots=True, widgets=True)
                root = doc.pdf_catalog()
                removed = []
                for key in ['StructTreeRoot','MarkInfo','AcroForm','Perms']:
                    if doc.xref_get_key(root,key)[0] != 'null':
                        doc.xref_set_key(root,key,'null'); removed.append(key)
                # A local GoTo action can be represented as a direct destination, retaining bookmark/link navigation.
                converted = 0
                for xref in range(1,doc.xref_length()):
                    if doc.xref_is_stream(xref): continue
                    obj = doc.xref_object(xref)
                    if not re.search(r'/A\s',obj): continue
                    typ, val = doc.xref_get_key(xref,'A')
                    if typ == 'dict':
                        kind = doc.xref_get_key(xref,'A/S'); dest = doc.xref_get_key(xref,'A/D')
                    elif typ == 'xref':
                        action = int(val.split()[0]); kind=doc.xref_get_key(action,'S'); dest=doc.xref_get_key(action,'D')
                    else: continue
                    if kind == ('name','/GoTo') and dest[0] in ('array','string','name'):
                        if dest[0] == 'string': continue  # Do not guess PDF string escaping; retain and let CMS reject.
                        doc.xref_set_key(xref,'Dest',dest[1]); doc.xref_set_key(xref,'A','null'); converted += 1
                if diagnostics is not None:
                    bad_pages = next((x['invalidPages'] for x in diagnostics if x['Stt']==stt),[])
                    for bad in bad_pages:
                        if bad['page'] < 1: raise ValueError('Cannot target malformed page')
                        doc[bad['page']-1].clean_contents(sanitize=1)
                    result['cleanedPages']=[x['page'] for x in bad_pages]
                doc.save(target,garbage=4,clean=diagnostics is None,deflate=True,deflate_images=True,deflate_fonts=True)
                result.update({'removedNonPageKeys':removed,'localGoToConverted':converted})
            result.update({'sha256':sha(target),'bytes':target.stat().st_size})
            proof = compare(original,target)
            dump(proofs/f'{stt:02d}-lossless.json',proof)
            result['comparison']={k:v for k,v in proof.items() if k!='pageChecks'}
            checks.append({'Stt':stt,'Path':str(target.resolve()),'ExpectedSha256':result['sha256'],'ExpectedPages':item['pages'],'SourceEntry':item['sourceEntry']})
            print(f"STT {stt}: {result['sourceBytes']} -> {result['bytes']} bytes; exact={proof['exactContentPass']}; render differences={len(proof['renderMismatchPages'])}",flush=True)
        except Exception as exc:
            result['error']=str(exc)
            print(f'STT {stt}: ERROR {exc}',flush=True)
        result['parserWarnings']=fitz.TOOLS.mupdf_warnings(reset=True)[:2000]
        if sha(original)!=item['sha256']: raise ValueError('Source modified')
        results.append(result)
        dump(output/f'{folder}-{stt:02d}.json',result)
    dump(output/(folder+'-files.json'),checks)
    dump(output/(folder+'-results.json'),results)

def refine_visual(original, derivative, proof):
    from PIL import Image, ImageChops, ImageStat
    checked, blocked = [], []
    with fitz.open(original) as a, fitz.open(derivative) as b:
        for n in proof['renderMismatchPages']:
            # Eliminate cross-document raster/font cache effects before comparing the same page.
            fitz.TOOLS.store_shrink(100)
            pa = a[n-1].get_pixmap(dpi=96, alpha=False, annots=True)
            fitz.TOOLS.store_shrink(100)
            pb = b[n-1].get_pixmap(dpi=96, alpha=False, annots=True)
            if (pa.width,pa.height)!=(pb.width,pb.height): raise ValueError('Geometry changed')
            diff=ImageChops.difference(Image.frombytes('RGB',(pa.width,pa.height),pa.samples),Image.frombytes('RGB',(pb.width,pb.height),pb.samples))
            maximum=max(x[1] for x in diff.getextrema())
            checked.append({'page':n,'maxChannelDelta':maximum,'meanChannelDelta':ImageStat.Stat(diff).mean})
            if maximum>2: blocked.append(n)
    return {'method':'fresh-render-cache; at most 2/255 channel rounding; full text/images also checked',
        'checkedPages':checked,'remainingMismatchPages':blocked,'preservationPass':not (blocked or proof['geometryMismatchPages'] or proof['textMissingPages'] or proof['textAddedPages'] or proof['imageMissingPages'])}

def finalize(output):
    final = output/'finalized'
    final.mkdir(exist_ok=True)
    sources=json.loads((output/'source-details.json').read_text(encoding='utf-8'))
    checks, results = [], []
    fitz.TOOLS.mupdf_display_errors(False);fitz.TOOLS.mupdf_display_warnings(False)
    for item in sources:
        stt=item['stt']
        candidate=output/f'selective-{stt:02d}.json'
        if not candidate.exists(): candidate=output/f'normalized-{stt:02d}.json'
        record=json.loads(candidate.read_text(encoding='utf-8'))
        path=Path(record['path']); target=final/item['file']
        if sha(path)!=record['sha256']: raise ValueError('Candidate hash changed')
        receipt=output/f'final-{stt:02d}.json'
        if receipt.exists():
            prior=json.loads(receipt.read_text(encoding='utf-8'))
            if prior['sourceSha256']!=item['sha256'] or sha(target)!=prior['sha256']: raise ValueError('Resume output/source hash changed')
            results.append(prior)
            checks.append({'Stt':stt,'Path':str(target.resolve()),'ExpectedSha256':prior['sha256'],'ExpectedPages':item['pages'],'SourceEntry':item['sourceEntry']})
            print(f'Verified existing final STT {stt}',flush=True)
            continue
        if target.exists(): raise ValueError('Unreceipted final output; no overwrite')
        with fitz.open(path) as doc:
            before=[(tuple(p.rect),tuple(p.mediabox),tuple(p.cropbox),p.rotation,hashlib.sha256(p.read_contents()).hexdigest()) for p in doc]
            toc=doc.get_toc()
            if stt==23:
                tree=[]; stack=[tree]
                for level,title,page in toc:
                    if level>len(stack): raise ValueError('Invalid source outline depth')
                    stack=stack[:level]
                    node={'title':title,'page':page,'children':[],'original':True}
                    stack[-1].append(node);stack.append(node['children'])
                def balance(nodes):
                    for node in nodes: node['children']=balance(node['children'])
                    if len(nodes)<=8: return nodes
                    return balance([{'title':f'Nhóm mục {i+1}–{min(i+8,len(nodes))}','page':nodes[i]['page'],'children':nodes[i:i+8],'original':False} for i in range(0,len(nodes),8)])
                balanced=balance(tree);grouped=[];retained=[]
                def emit(nodes,level,original_level):
                    for node in nodes:
                        grouped.append([level,node['title'],node['page']])
                        if node['original']: retained.append([original_level,node['title'],node['page']])
                        emit(node['children'],level+1,original_level+int(node['original']))
                emit(balanced,1,1)
                if retained!=toc: raise ValueError('Source bookmark hierarchy/order changed')
                doc.set_toc(grouped)
                if doc.get_toc()!=grouped: raise ValueError('Bookmark content/order changed')
            pdf=fitz._as_pdf_document(doc)
            removed=0
            for xref in range(1,doc.xref_length()):
                if doc.xref_is_stream(xref): continue
                keys=doc.xref_get_keys(xref)
                if 'A' in keys:
                    typ,val=doc.xref_get_key(xref,'A')
                    if typ=='dict':
                        kind,dest=doc.xref_get_key(xref,'A/S'),doc.xref_get_key(xref,'A/D')
                        if kind==('name','/GoTo') and dest[0] in ('array','name','string'):
                            doc.xref_set_key(xref,'Dest',fitz.get_pdf_str(dest[1]) if dest[0]=='string' else dest[1])
                            obj=fitz.mupdf.pdf_load_object(pdf,xref);fitz.mupdf.pdf_dict_dels(obj,'A');fitz.mupdf.pdf_update_object(pdf,xref,obj);removed+=1
                for key in ('A','AA','AcroForm','StructTreeRoot','MarkInfo','Perms'):
                    if key in keys and doc.xref_get_key(xref,key)[0]=='null':
                        obj=fitz.mupdf.pdf_load_object(pdf,xref);fitz.mupdf.pdf_dict_dels(obj,key);fitz.mupdf.pdf_update_object(pdf,xref,obj);removed+=1
            doc.save(target,garbage=0,clean=False,deflate=True)
        with fitz.open(target) as doc:
            after=[(tuple(p.rect),tuple(p.mediabox),tuple(p.cropbox),p.rotation,hashlib.sha256(p.read_contents()).hexdigest()) for p in doc]
            if before!=after: raise ValueError('Finalization changed page stream/geometry')
        preservation=refine_visual(output/'originals'/item['file'],target,record['comparison'])
        result={**record,'path':str(target.resolve()),'sha256':sha(target),'bytes':target.stat().st_size,'inertKeysRemoved':removed,
            'finalPageStreamsAndGeometryUnchanged':True,'bookmarkGroupsAdded':stt==23,'preservation':preservation}
        results.append(result)
        dump(output/f'final-{stt:02d}.json',result)
        checks.append({'Stt':stt,'Path':str(target.resolve()),'ExpectedSha256':result['sha256'],'ExpectedPages':item['pages'],'SourceEntry':item['sourceEntry']})
        print(f"Final STT {stt}: bytes={result['bytes']}; preservation={preservation['preservationPass']}",flush=True)
    dump(output/'final-files.json',checks)
    dump(output/'final-results.json',results)

def share_resources(output):
    fitz.TOOLS.mupdf_display_errors(False);fitz.TOOLS.mupdf_display_warnings(False)
    # Canonical PDF optimization: reuse identical resource dictionaries instead of repeating them on each page.
    record=json.loads((output/'final-23.json').read_text(encoding='utf-8'))
    details=next(x for x in json.loads((output/'source-details.json').read_text(encoding='utf-8')) if x['stt']==23)
    if sha(record['path'])!=record['sha256'] or sha(output/'originals'/details['file'])!=details['sha256']: raise ValueError('Source/candidate hash changed')
    folder=output/'shared-resources';folder.mkdir(exist_ok=False)
    target=folder/Path(record['path']).name
    with fitz.open(record['path']) as doc:
        before=[hashlib.sha256(p.read_contents()).hexdigest() for p in doc]
        counts=collections.Counter(doc.xref_get_key(p.xref,'Resources')[1] for p in doc if doc.xref_get_key(p.xref,'Resources')[0]=='dict')
        shared={};replaced=0
        for page in doc:
            typ,value=doc.xref_get_key(page.xref,'Resources')
            if typ=='dict' and counts[value]>1:
                if value not in shared:
                    xref=doc.get_new_xref();doc.update_object(xref,value);shared[value]=xref
                doc.xref_set_key(page.xref,'Resources',f'{shared[value]} 0 R');replaced+=1
        doc.save(target,garbage=0,clean=False,deflate=True)
    with fitz.open(target) as doc:
        if before!=[hashlib.sha256(p.read_contents()).hexdigest() for p in doc]: raise ValueError('Resource optimization changed page content stream')
    proof=compare(output/'originals'/details['file'],target)
    preservation=refine_visual(output/'originals'/details['file'],target,proof)
    result={**record,'path':str(target.resolve()),'sha256':sha(target),'bytes':target.stat().st_size,
        'sharedResourceDictionaries':len(shared),'pagesUsingSharedResources':replaced,'comparison':{k:v for k,v in proof.items() if k!='pageChecks'},'preservation':preservation}
    dump(output/'shared-23-proof.json',proof);dump(output/'shared-23.json',result)
    dump(output/'shared-23-files.json',[{'Stt':23,'Path':str(target.resolve()),'ExpectedSha256':result['sha256'],'ExpectedPages':details['pages'],'SourceEntry':details['sourceEntry']}])
    print(f"STT 23 shared {len(shared)} dictionaries on {replaced} pages; preservation={preservation['preservationPass']}",flush=True)

def subset_fonts(output):
    fitz.TOOLS.mupdf_display_errors(False);fitz.TOOLS.mupdf_display_warnings(False)
    record=json.loads((output/'shared-23.json').read_text(encoding='utf-8'))
    details=next(x for x in json.loads((output/'source-details.json').read_text(encoding='utf-8')) if x['stt']==23)
    if sha(record['path'])!=record['sha256'] or sha(output/'originals'/details['file'])!=details['sha256']: raise ValueError('Source/candidate hash changed')
    folder=output/'subset-fonts';folder.mkdir(exist_ok=False)
    target=folder/Path(record['path']).name
    with fitz.open(record['path']) as doc:
        doc.subset_fonts()
        doc.save(target,garbage=4,clean=False,deflate=True)
    proof=compare(output/'originals'/details['file'],target)
    preservation=refine_visual(output/'originals'/details['file'],target,proof)
    result={**record,'path':str(target.resolve()),'sha256':sha(target),'bytes':target.stat().st_size,'fontSubset':True,
        'comparison':{k:v for k,v in proof.items() if k!='pageChecks'},'preservation':preservation}
    dump(output/'subset-23-proof.json',proof);dump(output/'subset-23.json',result)
    dump(output/'subset-23-files.json',[{'Stt':23,'Path':str(target.resolve()),'ExpectedSha256':result['sha256'],'ExpectedPages':details['pages'],'SourceEntry':details['sourceEntry']}])
    print(f"STT 23 font subset: {result['bytes']} bytes; preservation={preservation['preservationPass']}",flush=True)

def compact_widths(output):
    from decimal import Decimal
    fitz.TOOLS.mupdf_display_errors(False);fitz.TOOLS.mupdf_display_warnings(False)
    record=json.loads((output/'shared-23.json').read_text(encoding='utf-8'))
    details=next(x for x in json.loads((output/'source-details.json').read_text(encoding='utf-8')) if x['stt']==23)
    if sha(record['path'])!=record['sha256'] or sha(output/'originals'/details['file'])!=details['sha256']: raise ValueError('Source/candidate hash changed')
    folder=output/'compact-widths';folder.mkdir(exist_ok=False);target=folder/Path(record['path']).name
    def decode(raw):
        tokens=re.findall(r'\[|\]|[-+]?(?:\d+\.?\d*|\.\d+)',raw)
        if ''.join(tokens)!=re.sub(r'\s','',raw): raise ValueError('Unsupported font width representation')
        pos=0
        def array():
            nonlocal pos
            if tokens[pos]!='[': raise ValueError('Invalid width array')
            pos+=1;values=[]
            while tokens[pos]!=']':
                if tokens[pos]=='[': values.append(array())
                else: values.append(Decimal(tokens[pos]));pos+=1
            pos+=1;return values
        entries=array()
        if pos!=len(tokens): raise ValueError('Trailing font tokens')
        widths={};i=0
        while i<len(entries):
            start=int(entries[i]);i+=1;val=entries[i];i+=1
            if isinstance(val,list):
                pairs=enumerate(val,start)
            else:
                end=int(val);width=entries[i];i+=1
                if end<start or end>65535: raise ValueError('Invalid font CID range')
                pairs=((cid,width) for cid in range(start,end+1))
            for cid,width in pairs:
                if cid in widths or cid>65535: raise ValueError('Duplicate font CID')
                widths[cid]=width
        return widths
    changed=0
    with fitz.open(record['path']) as doc:
        for xref in range(1,doc.xref_length()):
            if doc.xref_get_key(xref,'Type')!=('name','/Font'): continue
            typ,raw=doc.xref_get_key(xref,'W')
            if typ!='array': continue
            widths=decode(raw)
            if not widths: continue
            kind,default=doc.xref_get_key(xref,'DW');dw=Decimal(default) if kind in ('int','float') else Decimal(1000)
            runs=[]
            for cid,width in sorted(widths.items()):
                if runs and cid-runs[-1][0]-len(runs[-1][1])<=2:
                    start,values=runs[-1]
                    values.extend([dw]*(cid-start-len(values)));values.append(width)
                else: runs.append((cid,[width]))
            new='[ '+' '.join(str(start)+' [ '+' '.join(str(v) for v in values)+' ]' for start,values in runs)+' ]'
            if len(re.findall(r'\[|\]|[-+]?[\d.]+',new))>=len(re.findall(r'\[|\]|[-+]?[\d.]+',raw)): continue
            verify=decode(new)
            if any(verify.get(cid)!=width for cid,width in widths.items()) or any(cid not in widths and width!=dw for cid,width in verify.items()): raise ValueError('Font metrics changed')
            doc.xref_set_key(xref,'W',new);changed+=1
        doc.save(target,garbage=4,clean=False,deflate=True)
    proof=compare(output/'originals'/details['file'],target)
    preservation=refine_visual(output/'originals'/details['file'],target,proof)
    result={**record,'path':str(target.resolve()),'sha256':sha(target),'bytes':target.stat().st_size,'compactedFontWidthArrays':changed,
        'comparison':{k:v for k,v in proof.items() if k!='pageChecks'},'preservation':preservation}
    dump(output/'compact-23-proof.json',proof);dump(output/'compact-23.json',result)
    dump(output/'compact-23-files.json',[{'Stt':23,'Path':str(target.resolve()),'ExpectedSha256':result['sha256'],'ExpectedPages':details['pages'],'SourceEntry':details['sourceEntry']}])
    print(f"STT 23 compacted {changed} equivalent width arrays; preservation={preservation['preservationPass']}",flush=True)

def prepare(source, inventory, output):
    expected = json.loads(inventory.read_text(encoding='utf-8-sig'))
    if sha(source) != expected['ZipSha256']:
        raise ValueError('Source ZIP hash mismatch')
    output.mkdir(parents=True, exist_ok=False)
    originals = output/'originals'
    originals.mkdir()
    checks, details = [], []
    with zipfile.ZipFile(source) as archive:
        for row in expected['rows']:
            if row['CanImport']:
                continue
            for item in row['Files']:
                if not item['Blocks']:
                    continue
                name = item['Name']
                if Path(name).name != name or not name.lower().endswith('.pdf'):
                    raise ValueError('Unexpected source attachment')
                target = originals/name
                with open(target, 'xb') as file:
                    file.write(archive.read(item['Entry']))
                if sha(target) != item['Sha256']:
                    raise ValueError('PDF hash mismatch')
                with fitz.open(target) as doc:
                    widgets, annotations, images = [], [], set()
                    for page in doc:
                        widgets.extend({'page':page.number+1,'type':w.field_type_string,'name':w.field_name} for w in (page.widgets() or []))
                        annotations.extend({'page':page.number+1,'type':a.type[1]} for a in (page.annots() or []))
                        images.update(i[0] for i in page.get_images())
                    forbidden = []
                    signed = []
                    for xref in range(1, doc.xref_length()):
                        obj = doc.xref_object(xref)
                        keys = re.findall(r'/(AcroForm|OpenAction|JavaScript|JS|EmbeddedFiles|AF|AA|Launch|XFA|ByteRange)\b',obj)
                        if keys: forbidden.append({'xref':xref,'keys':keys})
                        if '/ByteRange' in obj: signed.append(xref)
                    details.append({'stt':row['Stt'],'file':name,'pages':len(doc),'bytes':target.stat().st_size,'sha256':item['Sha256'],
                        'sourceEntry':item['Entry'],'widgets':widgets,'annotations':annotations,'embeddedFiles':doc.embfile_count(),
                        'imageCount':len(images),'xrefCount':doc.xref_length(),'specialObjects':forbidden,'signatureObjects':signed,
                        'tocEntries':len(doc.get_toc()),'originalBlocks':item['Blocks']})
                checks.append({'Stt':row['Stt'],'Path':str(target.resolve()),'ExpectedSha256':item['Sha256'],'ExpectedPages':item['Pages'],'SourceEntry':item['Entry']})
                print(f"Extracted STT {row['Stt']}: {item['Pages']} pages; widgets={len(widgets)}, signatures={len(signed)}",flush=True)
    dump(output/'source-files.json',checks)
    dump(output/'source-details.json',details)
    dump(output/'source-binding.json',{'sourceZip':str(source.resolve()),'sha256':expected['ZipSha256'],'inventorySha256':sha(inventory),'dateChanges':0,'sourceWrites':0})

if __name__ == '__main__':
    import sys
    if len(sys.argv)==3 and sys.argv[1]=='normalize':
        normalize(Path(sys.argv[2])); sys.exit(0)
    if len(sys.argv)==3 and sys.argv[1]=='selective':
        target=Path(sys.argv[2])
        normalize(target,'selective',[15,18,19,20,23,28,29,30],json.loads((target/'repair-pages.json').read_text(encoding='utf-8-sig'))); sys.exit(0)
    if len(sys.argv)==3 and sys.argv[1]=='finalize':
        finalize(Path(sys.argv[2])); sys.exit(0)
    if len(sys.argv)==3 and sys.argv[1]=='share-resources':
        share_resources(Path(sys.argv[2])); sys.exit(0)
    if len(sys.argv)==3 and sys.argv[1]=='subset-fonts':
        subset_fonts(Path(sys.argv[2])); sys.exit(0)
    if len(sys.argv)==3 and sys.argv[1]=='compact-widths':
        compact_widths(Path(sys.argv[2])); sys.exit(0)
    parser = argparse.ArgumentParser()
    parser.add_argument('source',type=Path)
    parser.add_argument('inventory',type=Path)
    parser.add_argument('output',type=Path)
    args = parser.parse_args()
    prepare(args.source,args.inventory,args.output)
