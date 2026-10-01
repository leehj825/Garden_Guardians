import struct,json,io,sys,numpy as np
from PIL import Image,ImageDraw,ImageFont
import meshoptimizer as mo
Image.MAX_IMAGE_PIXELS=None
R='/home/user/Garden_Guardians/Assets/Models/Bramblekin/'
def load(path):
    d=open(path,'rb').read(); l,_=struct.unpack('<I4s',d[12:20]); js=json.loads(d[20:20+l]); b=d[20+l+8:]
    p=js['meshes'][0]['primitives'][0]; a=js['accessors']; bv=js['bufferViews']
    def g(i,dt,w):
        ac=a[i]; v=bv[ac['bufferView']]; o=v.get('byteOffset',0)+ac.get('byteOffset',0)
        arr=np.frombuffer(b[o:o+ac['count']*w*np.dtype(dt).itemsize],dt)
        return arr.reshape(-1,w) if w>1 else arr
    pos=g(p['attributes']['POSITION'],np.float32,3).astype(np.float64); uv=g(p['attributes']['TEXCOORD_0'],np.float32,2).astype(np.float64)
    ic=a[p['indices']]; idx=g(p['indices'],np.uint16 if ic['componentType']==5123 else np.uint32,1).astype(np.int64)
    v=bv[js['images'][0]['bufferView']]; tex=Image.open(io.BytesIO(b[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']])).convert('RGB')
    return pos,uv,idx.reshape(-1,3),np.asarray(tex)
def sample(tex,uv):
    h,w,_=tex.shape
    x=np.clip((uv[...,0]%1)*w,0,w-1).astype(int); y=np.clip((uv[...,1]%1)*h,0,h-1).astype(int)
    return tex[y,x].astype(float)
def raster(pos,tris,colorfn,H=640,W=380,view=1.0):
    # camera looks along +y (from -y side) when view=1; x right, z up
    zmin,zmax=pos[:,2].min(),pos[:,2].max(); sc=(H-30)/(zmax-zmin)
    img=np.zeros((H,W,3),np.uint8); img[:]=(205,225,245); zb=np.full((H,W),1e9)
    cx=W/2
    P=pos*np.array([view,view,1.0])  # mirror option
    X=cx+P[:,0]*sc*(1 if view>0 else -1); Y=H-15-(P[:,2]-zmin)*sc; D=P[:,1]*view
    for t in tris:
        tx,ty,td=X[t],Y[t],D[t]
        x0,x1=int(max(0,np.floor(tx.min()))),int(min(W-1,np.ceil(tx.max())))
        y0,y1=int(max(0,np.floor(ty.min()))),int(min(H-1,np.ceil(ty.max())))
        if x1<x0 or y1<y0: continue
        den=(ty[1]-ty[2])*(tx[0]-tx[2])+(tx[2]-tx[1])*(ty[0]-ty[2])
        if abs(den)<1e-12: continue
        gx,gy=np.meshgrid(np.arange(x0,x1+1)+0.5,np.arange(y0,y1+1)+0.5)
        l0=((ty[1]-ty[2])*(gx-tx[2])+(tx[2]-tx[1])*(gy-ty[2]))/den
        l1=((ty[2]-ty[0])*(gx-tx[2])+(tx[0]-tx[2])*(gy-ty[2]))/den
        l2=1-l0-l1
        m=(l0>=-1e-6)&(l1>=-1e-6)&(l2>=-1e-6)
        if not m.any(): continue
        dep=l0*td[0]+l1*td[1]+l2*td[2]
        sub=zb[y0:y1+1,x0:x1+1]; ok=m&(dep<sub)
        if not ok.any(): continue
        col=colorfn(t,l0[ok],l1[ok],l2[ok])
        sub[ok]=dep[ok]; img[y0:y1+1,x0:x1+1][ok]=np.clip(col,0,255).astype(np.uint8)
    return Image.fromarray(img)
def textured(pos,uv,tris,tex,H=640,view=1.0):
    def fn(t,l0,l1,l2):
        u=uv[t[0]][None,:]*l0[:,None]+uv[t[1]][None,:]*l1[:,None]+uv[t[2]][None,:]*l2[:,None]
        return sample(tex,u)
    return raster(pos,tris,fn,H,view=view)
def lowpoly(pos,uv,idx,tex,target=700):
    key=np.round(np.hstack([pos,uv*0])*1e4).astype(np.int64)   # weld on position only: shape matters, not the texture
    _,first,inv=np.unique(key,axis=0,return_index=True,return_inverse=True); inv=inv.reshape(-1)
    wpos=pos[first].astype(np.float32); widx=inv[idx.reshape(-1)].astype(np.uint32)
    dest=np.zeros(len(widx),np.uint32)
    got=mo.simplify(dest,widx,wpos,target_index_count=target*3,target_error=1.0,options=mo.SIMPLIFY_PRUNE,result_error=np.zeros(1,np.float32)) if False else None
    import ctypes
    fn=mo.simplifier.lib.meshopt_simplify; fn.restype=ctypes.c_size_t
    fn.argtypes=[ctypes.c_void_p,ctypes.c_void_p,ctypes.c_size_t,ctypes.c_void_p,ctypes.c_size_t,ctypes.c_size_t,ctypes.c_size_t,ctypes.c_float,ctypes.c_uint,ctypes.c_void_p]
    wpos=np.ascontiguousarray(wpos); widx=np.ascontiguousarray(widx)
    best=None
    for err in (0.05,0.1,0.2,0.4,0.8,1.6,3.2,6.4,12.8,25.6):
        d2=np.zeros(len(widx),np.uint32)
        got=fn(d2.ctypes.data,widx.ctypes.data,len(widx),wpos.ctypes.data,len(wpos),12,target*3,err,mo.SIMPLIFY_PRUNE|mo.SIMPLIFY_SPARSE,None)
        if got>0 and (best is None or abs(got-target*3)<abs(best[0]-target*3)): best=(got,d2.copy())
        if 0<got<=target*3*1.1: break
    got,dest=best
    kept=dest[:got].reshape(-1,3)
    # colour of each kept face: the texture averaged over the ORIGINAL faces nearest its centre
    return wpos.astype(np.float64),kept,first,inv
def flat_colours(wpos,kept,pos,uv,idx,tex):
    # original face centres and colours
    oc=pos[idx].mean(axis=1); ouv=uv[idx].mean(axis=1); ocol=sample(tex,ouv)
    # grid lookup by nearest original face centre (chunked)
    kc=wpos[kept].mean(axis=1); cols=np.zeros((len(kept),3))
    from math import inf
    for s in range(0,len(kc),64):
        d=((kc[s:s+64,None,:]-oc[None,:,:])**2).sum(-1); near=np.argsort(d,axis=1)[:,:40]
        cols[s:s+64]=ocol[near].mean(axis=1)
    return cols
def flat(wpos,kept,cols,H=640,view=1.0):
    tp=wpos[kept]; n=np.cross(tp[:,1]-tp[:,0],tp[:,2]-tp[:,0]); n/=np.maximum(np.linalg.norm(n,axis=1,keepdims=True),1e-9)
    light=np.array([0.3,-0.6,0.75]); light/=np.linalg.norm(light)
    shade=0.68+0.32*np.abs(n@light)
    # brighten a touch, keep saturation
    face_col=np.clip(cols*1.06*shade[:,None],0,255)
    order={tuple(t):k for k,t in enumerate(kept)}
    def fn(t,l0,l1,l2):
        return np.tile(face_col[order[tuple(t)]],(len(l0),1))
    return raster(wpos,kept,fn,H,view=view)
def label(img,text):
    im=Image.new('RGB',(img.width,img.height+34),(245,240,228)); im.paste(img,(0,34))
    ImageDraw.Draw(im).text((10,8),text,fill=(60,40,20))
    return im
if __name__=='__main__':
    view=1.0; H=560; W=380
    rows=[]
    for name,tag in (('Walking','Male'),('Walking_female','Female')):
        pos,uv,tris,tex=load(R+name+'.glb')
        full=textured(pos,uv,tris,tex,H=H,view=view)
        lpos,luv,ltris,ltex=load(R+name+'_lod2.glb')
        mid=textured(lpos,luv,ltris,ltex,H=H,view=view)
        wpos,kept,first,inv=lowpoly(pos,uv,tris,tex,700)
        cols=flat_colours(wpos,kept,pos,uv,tris,tex)
        low=flat(wpos,kept,cols,H=H,view=view)
        rows.append([label(full,f'{tag}: full detail - {len(tris):,} triangles, full texture'),
                     label(mid,f'{tag}: low poly, textured - {len(ltris):,} triangles (in the game now)'),
                     label(low,f'{tag}: low poly, flat colours - {len(kept):,} triangles')])
        print(tag,len(tris),len(ltris),len(kept),flush=True)
    sheet=Image.new('RGB',(3*(W+10),2*rows[0][0].height+10),(245,240,228))
    for r,row in enumerate(rows):
        for c,im in enumerate(row): sheet.paste(im.resize((W,im.height)),(c*(W+10),r*(rows[0][0].height+10)))
    sheet.save('/tmp/claude-0/x/kin_compare.png')
