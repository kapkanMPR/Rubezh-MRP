"""Pack finished whole-character atlases; never resize them below HD for Ultra.
Run: python Tools/PawnAtlas/rebuild.py --check   (requires Pillow and NumPy)
Omit --check to rebuild the custom runtime pages and green fallback portraits.
"""
from pathlib import Path
import gzip, hashlib, json, struct, sys, io
import numpy as np
from PIL import Image
root=Path(__file__).resolve().parents[2]
source=root/'Assets/Rubezh/Art/PawnAtlases'
resources=root/'Assets/Rubezh/Resources'
layout=json.loads((source/'layout.json').read_text())
expected_path=root/'Tools/ProjectCheck/payload-sha256.json'
expected=json.loads(expected_path.read_text())
check='--check' in sys.argv
minimum=9999
for character in layout['characters']:
 sheet=Image.open(source/character['atlas']).convert('RGBA')
 assert sheet.size==(1280,1792),character['atlas']
 chunks=[]
 for frame,direction in enumerate(layout['frameOrder']):
  x=frame%2*640;y=frame//2*896
  whole=sheet.crop((x,y,x+640,y+896))
  box=whole.getchannel('A').point(lambda v:255 if v>=64 else 0).getbbox()
  assert box and box[3]-box[1]>=720,(character['atlas'],direction,box)
  minimum=min(minimum,box[3]-box[1])
  # Two pixels preserve the antialiased silhouette around the strong ink.
  image=whole.crop((max(0,box[0]-2),max(0,box[1]-2),min(640,box[2]+2),min(896,box[3]+2)))
  sizes=[];w,h=image.size
  while True:
   sizes.append((w,h))
   if w==1 and h==1:break
   w,h=max(1,w//2),max(1,h//2)
  sid=character['firstSprite']+frame
  parts=[struct.pack('<ii',sid,len(sizes))]
  for w,h in sizes:
   rgba=np.array(image.resize((w,h),Image.Resampling.LANCZOS),dtype=np.uint16)
   rgba[:,:,:3]=(rgba[:,:,:3]*rgba[:,:,3:4]+127)//255
   parts.extend((struct.pack('<ii',w,h),rgba.astype('uint8').tobytes()))
  chunk=b''.join(parts);chunks.append(chunk)
  if check:assert expected[str(sid)]==hashlib.sha256(chunk).hexdigest(),('payload',sid)
  else:expected[str(sid)]=hashlib.sha256(chunk).hexdigest()
  if character['side']=='green':
   menu=resources/f"FieldCamp/Art/pawns/green-{character['head']}-{direction}.png"
   if check:assert Image.open(menu).convert('RGBA').tobytes()==whole.tobytes(),('menu',menu)
   else:
    menu.parent.mkdir(parents=True,exist_ok=True)
    encoded=io.BytesIO();whole.save(encoded,format='PNG')
    data=encoded.getvalue();Image.open(io.BytesIO(data)).load()
    temporary=menu.with_suffix('.tmp');temporary.write_bytes(data);temporary.replace(menu)
 payload=struct.pack('<ii',0x32475152,4)+b''.join(chunks)
 page=resources/f"SpritePacks/p{character['page']:04d}.bytes"
 if check:assert gzip.decompress(page.read_bytes())==payload,('page',page)
 else:page.write_bytes(gzip.compress(payload,mtime=0))
if not check:expected_path.write_text(json.dumps(expected,indent=2)+'\n')
print(f"PASS {len(layout['characters'])} whole character atlases / 112 frames; visible height >= {minimum}px; exact runtime and menu parity")
