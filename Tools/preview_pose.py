"""Pose a rigged Bramblekin mesh with one of the clips, in software, and draw it - no Blender, no GPU.

    pip install numpy pillow
    python3 Tools/preview_pose.py Assets/Models/Bramblekin/Guard_male.glb out Assets/Models/Bramblekin/GuardWalk.glb 0.0 0.3 0.6

Writes out_<time>_<view>.png (front and side) and out_sheet.png for each time (seconds) given; '-' as the clip uses the mesh glb's own
animation. skinned() and render() can be imported to pose with overridden bone rotations (see make_guard_clips.py).
"""
import sys,math,io,numpy as np
import os
sys.path.insert(0,os.path.dirname(os.path.abspath(__file__)))
import convert_female as cf
from PIL import Image
def quat_mat(q):
    x,y,z,w=q
    return np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
def slerp(a,b,t):
    d=np.dot(a,b)
    if d<0: b=-b; d=-d
    if d>0.9995:
        r=a+t*(b-a); return r/np.linalg.norm(r)
    th=math.acos(d); return (math.sin((1-t)*th)*a+math.sin(t*th)*b)/math.sin(th)
def load(path):
    doc,b=cf.read_glb(path)
    return doc,b
def pose(doc,b,t,adoc=None,ab=None,override=None):
    nodes=doc['nodes']; skin=doc['skins'][0]
    adoc=adoc or doc; ab=ab if ab is not None else b
    anim=adoc['animations'][0]
    byname={n['name']:i for i,n in enumerate(nodes)}
    local=[{'t':np.array(n.get('translation',[0,0,0]),float),'r':np.array(n.get('rotation',[0,0,0,1]),float),'s':np.array(n.get('scale',[1,1,1]),float)} for n in nodes]
    for ch in anim['channels']:
        s=anim['samplers'][ch['sampler']]
        times=cf.accessor(adoc,ab,s['input']).astype(float); vals=cf.accessor(adoc,ab,s['output']).astype(float)
        tt=t%times[-1] if times[-1]>0 else 0
        i=int(np.searchsorted(times,tt,side='right')-1); i=max(0,min(i,len(times)-2))
        f=(tt-times[i])/max(times[i+1]-times[i],1e-9)
        path=ch['target']['path']; n=byname.get(adoc['nodes'][ch['target']['node']]['name'])
        if n is None: continue
        if path=='rotation': local[n]['r']=slerp(vals[i],vals[i+1],f)
        elif path=='translation': local[n]['t']=vals[i]+(vals[i+1]-vals[i])*f
        elif path=='scale': local[n]['s']=vals[i]+(vals[i+1]-vals[i])*f
    for nm,q in (override or {}).items():
        local[byname[nm]]['r']=np.array(q(t) if callable(q) else q,float)
    parent={}
    for i,n in enumerate(nodes):
        for c in n.get('children',[]): parent[c]=i
    cache={}
    def glob(i):
        if i in cache: return cache[i]
        L=np.eye(4); L[:3,:3]=quat_mat(local[i]['r'])*local[i]['s']; L[:3,3]=local[i]['t']
        G=glob(parent[i])@L if i in parent else L
        cache[i]=G; return G
    ibm=cf.accessor(doc,b,skin['inverseBindMatrices']).reshape(-1,4,4)
    mats=[glob(j)@ibm[k].T for k,j in enumerate(skin['joints'])]
    return mats
def skinned(path,t,clip=None,override=None):
    doc,b=load(path)
    adoc=ab=None
    if clip: adoc,ab=load(clip)
    pr=doc['meshes'][0]['primitives'][0]; at=pr['attributes']
    P=cf.accessor(doc,b,at['POSITION']).astype(float); N=cf.accessor(doc,b,at['NORMAL']).astype(float)
    J=cf.accessor(doc,b,at['JOINTS_0']).astype(int); W=cf.accessor(doc,b,at['WEIGHTS_0']).astype(float)
    uv=cf.accessor(doc,b,at['TEXCOORD_0']).astype(float); I=cf.accessor(doc,b,pr['indices']).reshape(-1,3).astype(int)
    mats=pose(doc,b,t,adoc,ab,override)
    Q=np.zeros_like(P); QN=np.zeros_like(N)
    Ph=np.hstack([P,np.ones((len(P),1))])
    for k in range(4):
        M=np.array([mats[j] for j in J[:,k]])
        Q+=W[:,k:k+1]*np.einsum('nij,nj->ni',M,Ph)[:,:3]
        QN+=W[:,k:k+1]*np.einsum('nij,nj->ni',M[:,:3,:3],N)
    img=doc['images'][0]; v=doc['bufferViews'][img['bufferView']]
    tex=np.array(Image.open(io.BytesIO(b[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']])).convert('RGB'))
    return Q,QN,uv,I,tex
def render(Q,QN,uv,I,tex,out,yaw,pitch,size=420):
    # bind space Z-up facing -Y -> Y-up: (x,z,-y)
    P=Q; N=QN
    cy,sy=math.cos(yaw),math.sin(yaw); cp,sp=math.cos(pitch),math.sin(pitch)
    R=np.array([[cy,0,sy],[0,1,0],[-sy,0,cy]])@np.array([[1,0,0],[0,cp,-sp],[0,sp,cp]])
    c=np.array([0,0.45,0]); Qr=(P-c)@R.T; s=size*0.8
    X=Qr[:,0]*s+size/2; Y=-Qr[:,1]*s+size/2; Z=Qr[:,2]; Nr=N@R.T
    th,tw=tex.shape[:2]
    buf=np.full((size,size,3),235,np.uint8); zb=np.full((size,size),1e9)
    for tri in I:
        x=X[tri];y=Y[tri];z=Z[tri]
        mnx,mxx=max(int(x.min()),0),min(int(x.max())+1,size-1); mny,mxy=max(int(y.min()),0),min(int(y.max())+1,size-1)
        if mnx>=mxx or mny>=mxy: continue
        d=(y[1]-y[2])*(x[0]-x[2])+(x[2]-x[1])*(y[0]-y[2])
        if abs(d)<1e-9: continue
        gx,gy=np.meshgrid(np.arange(mnx,mxx+1)+.5,np.arange(mny,mxy+1)+.5)
        w0=((y[1]-y[2])*(gx-x[2])+(x[2]-x[1])*(gy-y[2]))/d; w1=((y[2]-y[0])*(gx-x[2])+(x[0]-x[2])*(gy-y[2]))/d; w2=1-w0-w1
        m=(w0>=0)&(w1>=0)&(w2>=0)
        if not m.any(): continue
        zz=w0*z[0]+w1*z[1]+w2*z[2]
        u=w0*uv[tri[0],0]+w1*uv[tri[1],0]+w2*uv[tri[2],0]; v=w0*uv[tri[0],1]+w1*uv[tri[1],1]+w2*uv[tri[2],1]
        nz=w0*Nr[tri[0],2]+w1*Nr[tri[1],2]+w2*Nr[tri[2],2]
        sub=zb[mny:mxy+1,mnx:mxx+1]; ok=m&(zz<sub)
        if not ok.any(): continue
        ti=np.clip((u*tw).astype(int)%tw,0,tw-1); tj=np.clip((v*th).astype(int)%th,0,th-1)
        col=tex[tj,ti]*(0.6+0.4*np.clip(-nz,0,1))[...,None]
        bb=buf[mny:mxy+1,mnx:mxx+1]; bb[ok]=np.clip(col[ok]*1.6,0,255).astype(np.uint8); sub[ok]=zz[ok]
    Image.fromarray(buf).save(out)
if __name__=='__main__':
    path=sys.argv[1]; tag=sys.argv[2]; clip=sys.argv[3] if sys.argv[3]!='-' else None
    times=[float(x) for x in sys.argv[4:]] or [0.15,0.45]
    ims=[]
    for t in times:
        Q,QN,uv,I,tex=skinned(path,t,clip)
        for k,(yaw,pc) in enumerate([(math.pi,0.15),(math.pi*0.6,0.15)]):
            f=f'{tag}_{t}_{k}.png'; render(Q,QN,uv,I,tex,f,yaw,pc); ims.append(Image.open(f))
    W=Image.new('RGB',(420*len(ims),420)); [W.paste(im,(420*i,0)) for i,im in enumerate(ims)]; W.save(tag+'_sheet.png')
