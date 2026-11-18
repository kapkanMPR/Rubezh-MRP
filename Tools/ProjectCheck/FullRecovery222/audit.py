"""Offline source/content audit. Does not invoke Unity or claim native GPU/build validation."""
from pathlib import Path
from collections import defaultdict
from PIL import Image
import gzip,hashlib,json,re,struct

root=Path(__file__).resolve().parents[3]
out=root/'Build/Reports/Recovery222';out.mkdir(parents=True,exist_ok=True)
errors=[];warnings=[]
report={'version':'AI-0.12.222-RecoveredFull','unityBuildExecuted':False,'gpuShadersExecuted':False}
def check(condition,message):
    if not condition:errors.append(message)
def sha(p):return hashlib.sha256(p.read_bytes()).hexdigest()
maps=[]
for mode,count,folder,stem in [('story',20,'Regenerated','Campaign_'),('war',50,'War','war-')]:
    for i in range(1,count+1):
        p=root/f'Assets/Rubezh/Resources/{folder}/{stem}{i:02}.bytes'
        try:
            raw=p.read_bytes();data=json.loads(gzip.decompress(raw))
            check(data['columns']>0 and data['rows']>0,'Invalid map dimensions '+str(p))
            maps.append({'mode':mode,'index':i,'columns':data['columns'],'rows':data['rows'],'bytes':len(raw),'sha256':hashlib.sha256(raw).hexdigest(),'buildings':len(data.get('dynamicBuildings') or [])})
        except Exception as e:errors.append(str(p)+': '+str(e))
report['sourceMaps']=maps
native=root/'Assets/Rubezh/NativeContent'
catalog=json.loads((native/'catalog.json').read_text());ids=set();pages=set()
for e in catalog['entries']:
    check(e['id'] not in ids,'Duplicate sprite id '+str(e['id']));ids.add(e['id']);pages.add(e['page'])
    p=native/f"Sprites/p{e['page']:04}/s{e['id']:05}.png"
    try:
        with Image.open(p) as im:check(im.size==(e['w'],e['h']),f'Sprite dimensions mismatch {p}: {im.size}');im.verify()
    except Exception as x:errors.append(str(p)+': '+str(x))
atlases_pending=[page for page in sorted(pages) if not (native/f'Atlases/p{page:04}.spriteatlas').is_file()]
# NativeContentPreparation generates these Unity assets from the verified PNG
# source folders. Absence is explicitly reported, never disguised as a ready build.
report['nativeSprites']=len(ids);report['nativeAtlasPagesExpected']=len(pages)
report['nativeAtlasPagesPresent']=len(pages)-len(atlases_pending)
for kind in ['NativeExterior','NativeUI']:
    folder=root/'Assets/Rubezh/Resources'/kind;c=json.loads((folder/'catalog.json').read_text());seen={};keys=set()
    for e in c['entries']:
        check(e['key'] not in keys,kind+' duplicate key '+e['key']);keys.add(e['key'])
        p=folder/f"page-{e['page']:02}.png"
        if p not in seen:
            try:
                with Image.open(p) as im:seen[p]=im.size;im.verify()
            except Exception as x:errors.append(str(p)+': '+str(x));seen[p]=(0,0)
        w,h=seen[p];check(e['x']>=0 and e['y']>=0 and e['x']+e['w']<=w and e['y']+e['h']<=h,kind+' region outside page '+e['key'])
        meta=Path(str(p)+'.meta');check(meta.is_file(),'Missing importer '+str(meta))
        if meta.exists():check(re.search(r'^\s*textureShape:\s*1\s*$',meta.read_text(),re.M) is not None,'Atlas is not explicit Texture2D '+str(meta))
        if kind=='NativeExterior':
            source=root/'Assets/Rubezh/Editor/LegacySources/Exterior'/(e['key']+'.bytes')
            check(source.is_file(),'Missing Exterior source '+str(source))
            if source.exists():check(sha(source)==e['sourceSha256'],'Exterior source/catalog SHA mismatch '+e['key'])
    report[kind]={'entries':len(keys),'pages':len(seen)}
all_guids=defaultdict(list);orphan=[];missing=[]
for p in (root/'Assets').rglob('*'):
    if p.name.endswith('.meta'):
        if not Path(str(p)[:-5]).exists():orphan.append(str(p.relative_to(root)))
        match=re.search(r'^guid:\s*([a-f0-9]{32})\s*$',p.read_text(errors='replace'),re.M)
        if match:all_guids[match[1]].append(str(p.relative_to(root)))
        else:errors.append('Invalid GUID '+str(p.relative_to(root)))
    elif not Path(str(p)+'.meta').exists():missing.append(str(p.relative_to(root)))
duplicates=[v for v in all_guids.values() if len(v)>1]
check(not duplicates,'Duplicate asset GUIDs: '+str(duplicates[:10]));check(not orphan,'Orphan metadata: '+str(orphan[:10]))
report['metadata']={'duplicateGuids':duplicates,'orphans':orphan,'missingMeta':missing}
if missing:warnings.append('Unity will create metadata for '+str(len(missing))+' new assets/folders; see missingMeta')
for legacy in ['SpritePacks','BuildingMaterialData','Exterior','UnityTestForest']:
    check(not (root/'Assets/Rubezh/Resources'/legacy).exists(),'Legacy sources remain in runtime Resources: '+legacy)
report['derivedUnityContent']={'state':'requires preparation in Unity Editor','sourceMapsComplete':len(maps)==70,'atlasPagesToGenerate':atlases_pending,'reason':'Native map conversion, SpriteAtlas assets, depth Texture2D assets, Addressables catalog and platform bundles must be produced by the real Unity Editor; no fake readiness stamp is provided.'}
resources=defaultdict(list)
for p in (root/'Assets').rglob('*'):
    if not p.is_file() or p.name.endswith('.meta') or 'Resources' not in p.parts or 'Editor' in p.parts:continue
    index=p.parts.index('Resources');key=str(Path(*p.parts[index+1:]).with_suffix('')).lower();resources[key].append(str(p.relative_to(root)))
duplicates=[v for v in resources.values() if len(v)>1]
check(not duplicates,'Duplicate Resources load keys: '+str(duplicates[:8]));report['duplicateResourceKeys']=duplicates
report['passed']=not errors;report['errors']=errors;report['warnings']=warnings
(out/'full-source-content-audit.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
print(json.dumps({'passed':report['passed'],'maps':len(maps),'sprites':len(ids),'atlasPagesExpected':len(pages),'atlasPagesToGenerate':len(atlases_pending),'NativeUI':report['NativeUI'],'NativeExterior':report['NativeExterior'],'errors':sorted(set(errors)),'warnings':warnings},ensure_ascii=False,indent=2))
raise SystemExit(0 if not errors else 1)
