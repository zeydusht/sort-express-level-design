"""Renders a Unity mesh asset (AssetRipper YAML) with its texture: z-buffered triangles, the texture sampled per pixel,
soft light from the front-left plus a faint rim, transparent background. Used for the painter's alien
(Tools/make_booster_art.py); meshrender.py is the untextured version."""
import re, struct, sys, math
import numpy as np
from PIL import Image

def parse(path):
    t = open(path).read()
    vc = int(re.search(r'm_VertexCount: (\d+)', t).group(1))
    ch_block = t[t.index('m_Channels:'):t.index('m_DataSize')]
    chans = [tuple(map(int,c)) for c in re.findall(r'stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)', ch_block)]
    fmt_size = {0: 4, 1: 2, 2: 1, 3: 1, 4: 2, 5: 2, 6: 1, 7: 1, 8: 2, 9: 2, 10: 4, 11: 4}
    stride = 0
    for s,o,f,d in chans:
        if s==0 and d>0: stride=max(stride,o+fmt_size[f]*(d&0xF))
    data = bytes.fromhex(re.search(r'_typelessdata: ([0-9a-f]+)', t).group(1))
    idx = bytes.fromhex(re.search(r'm_IndexBuffer: ([0-9a-f]+)', t).group(1))
    fmt16 = re.search(r'm_IndexFormat: (\d)', t).group(1) == '0'
    inds = struct.unpack('<%dH'%(len(idx)//2), idx) if fmt16 else struct.unpack('<%dI'%(len(idx)//4), idx)
    def read(ch, i):
        s,o,f,d = chans[ch]
        d &= 0xF
        if f==0: return struct.unpack_from('<%df'%d, data, i*stride+o)
        if f==1:
            return tuple(np.frombuffer(data[i*stride+o:i*stride+o+2*d], dtype=np.float16).astype(float))
        if f==2: return tuple(b/255 for b in data[i*stride+o:i*stride+o+d])
        raise ValueError(f)
    pos = np.array([read(0,i) for i in range(vc)])
    nrm = np.array([read(1,i) for i in range(vc)])
    uvch = 4 if len(chans)>4 and chans[4][3]>0 else None
    uv = np.array([read(uvch,i)[:2] for i in range(vc)]) if uvch else None
    tris = np.array(inds).reshape(-1,3)
    return pos, nrm, uv, tris

def render(pos, nrm, uv, tris, tex, size=512, yaw=0.0, pitch=0.0, pad=0.06):
    a, b = math.radians(yaw), math.radians(pitch)
    Ry = np.array([[math.cos(a),0,math.sin(a)],[0,1,0],[-math.sin(a),0,math.cos(a)]])
    Rx = np.array([[1,0,0],[0,math.cos(b),-math.sin(b)],[0,math.sin(b),math.cos(b)]])
    R = Rx @ Ry
    P = pos @ R.T; N = nrm @ R.T
    mn, mx = P[:,:2].min(0), P[:,:2].max(0)
    span = (mx-mn).max()*(1+2*pad); c = (mn+mx)/2
    S = size*2
    sx = (P[:,0]-c[0])/span*S + S/2; sy = S/2 - (P[:,1]-c[1])/span*S
    z = P[:,2]
    zbuf = np.full((S,S), np.inf); out = np.zeros((S,S,4))
    T = np.asarray(tex.convert('RGB'), dtype=float)/255; th, tw = T.shape[:2]
    L = np.array([-0.35, 0.55, -0.75]); L/=np.linalg.norm(L)
    for (i0,i1,i2) in tris:
        xs = np.array([sx[i0],sx[i1],sx[i2]]); ys = np.array([sy[i0],sy[i1],sy[i2]])
        x0,x1 = int(max(0,math.floor(xs.min()))), int(min(S-1,math.ceil(xs.max())))
        y0,y1 = int(max(0,math.floor(ys.min()))), int(min(S-1,math.ceil(ys.max())))
        if x1<x0 or y1<y0: continue
        X,Y = np.meshgrid(np.arange(x0,x1+1)+0.5, np.arange(y0,y1+1)+0.5)
        d = (ys[1]-ys[2])*(xs[0]-xs[2])+(xs[2]-xs[1])*(ys[0]-ys[2])
        if abs(d)<1e-9: continue
        w0 = ((ys[1]-ys[2])*(X-xs[2])+(xs[2]-xs[1])*(Y-ys[2]))/d
        w1 = ((ys[2]-ys[0])*(X-xs[2])+(xs[0]-xs[2])*(Y-ys[2]))/d
        w2 = 1-w0-w1
        m = (w0>=-1e-4)&(w1>=-1e-4)&(w2>=-1e-4)
        if not m.any(): continue
        zz = w0*z[i0]+w1*z[i1]+w2*z[i2]
        sub = zbuf[y0:y1+1, x0:x1+1]
        m &= zz < sub
        if not m.any(): continue
        sub[m] = zz[m]
        n = (w0[...,None]*N[i0]+w1[...,None]*N[i1]+w2[...,None]*N[i2])
        n /= np.linalg.norm(n,axis=-1,keepdims=True)+1e-9
        lam = np.clip(-(n@L),0,1)
        if uv is not None:
            u = w0*uv[i0,0]+w1*uv[i1,0]+w2*uv[i2,0]; v = w0*uv[i0,1]+w1*uv[i1,1]+w2*uv[i2,1]
            tx = np.clip((u%1.0)*tw,0,tw-1).astype(int); ty = np.clip((1-(v%1.0))*th,0,th-1).astype(int)
            col = T[ty,tx]
        else: col = np.ones(X.shape+(3,))*np.array([0.4,0.9,0.3])
        shade = 0.62+0.48*lam
        # rim light from the back like the game's item icons
        rim = np.clip(1-np.abs(n[...,2]),0,1)**3*0.25
        rgb = np.clip(col*shade[...,None]+rim[...,None],0,1)
        o = out[y0:y1+1, x0:x1+1]
        o[m,:3] = rgb[m]; o[m,3] = 1
    img = Image.fromarray((out*255).astype(np.uint8),'RGBA').resize((size,size), Image.LANCZOS)
    return img

if __name__ == '__main__':
    # preview: python3 meshrender_textured.py mesh.asset texture.png out.png  (front, back, side)
    pos, nrm, uv, tris = parse(sys.argv[1])
    tex = Image.open(sys.argv[2])
    sheet = Image.new('RGBA', (900, 300), (40, 60, 110, 255))
    for i, yaw in enumerate((180, 0, 90)):
        sheet.alpha_composite(render(pos, nrm, uv, tris, tex, 300, yaw, 8), (i * 300, 0))
    sheet.save(sys.argv[3])
