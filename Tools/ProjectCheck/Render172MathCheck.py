"""Portable math checks only; this does not compile or execute Unity/C#."""
import math, random
from pathlib import Path
rng=random.Random(172)
# Ground culling must retain every chunk admitted by the original exhaustive scan.
for case in range(150):
    left,top=-200,-200;right,bottom=220,230
    x0,y0=rng.uniform(-3500,2000),rng.uniform(-1600,1000)
    x1,y1=x0+rng.uniform(120,1800),y0+rng.uniform(100,900)
    u0,u1=(x0-96)/11,(x1+96)/11
    v0,v1=(y0-96)/5,(y1+96)/5
    firstx=max(left,math.floor((u0-v1)/20)*10-10)
    lastx=min(right,math.ceil((u1-v0)/20)*10+10)
    firsty=max(top,math.floor((u0+v0)/20)*10-10)
    lasty=min(bottom,math.ceil((u1+v1)/20)*10+10)
    for y in range(top,bottom,10):
        for x in range(left,right,10):
            r,b=min(right,x+10),min(bottom,y+10)
            lpx,rpx=(x+y)*11-32,(r+b)*11+32
            tpx,bpx=(y-r)*5-32,(b-x)*5+32
            accepted=rpx>=x0-64 and lpx<=x1+64 and bpx>=y0-64 and tpx<=y1+64
            if accepted:assert firstx<=x<lastx and firsty<=y<lasty
# The overscan depth buffer preserves exact screen texel density (not a downscale).
for w,h in [(2608,1220),(1920,1080),(1080,1920),(4096,4096)]:
    px=min(64,max(0,(4096-w)//2));py=min(64,max(0,(4096-h)//2))
    vw,vh=.24,.32
    coveragew=vw*(w+2*px)/w;coverageh=vh*(h+2*py)/h
    assert math.isclose((w+2*px)/coveragew,w/vw)
    assert math.isclose((h+2*py)/coverageh,h/vh)
    # Coordinate of a world point in the stable buffer is independent of camera pan.
    for shift in [-px,0,px]:
        world=.1+shift*vw/w
        depth_uv=(world-(.1-px*vw/w))/coveragew
        assert -1e-12<=depth_uv<=1+1e-12
print('PASS 150 randomized ground views: no formerly visible chunk omitted')
print('PASS depth texel density: landscape, portrait, device texture limit')
# Lexical delimiter scan, deliberately not advertised as C# compilation.
r=Path(__file__).resolve().parents[2]/'Assets/Rubezh'
paths=list((r/'Runtime').glob('RubezhHdGpu*.cs'))+[r/'Runtime/PerformanceReport.cs',r/'Resources/RubezhHdGpu.shader']
import re
for p in paths:
    s=p.read_text();s=re.sub(r'//[^\n]*|/\*.*?\*/|"(?:\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\'', '', s,flags=re.S)
    stack=[];pairs={')':'(',']':'[','}':'{'}
    for ch in s:
        if ch in '([{':stack.append(ch)
        elif ch in pairs:assert stack and stack.pop()==pairs[ch],str(p)
    assert not stack,str(p)
print('PASS delimiter scan (not a compiler)')
