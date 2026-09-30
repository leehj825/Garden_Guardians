import sys; sys.path.insert(0,'/tmp/claude-0/x'); sys.path.insert(0,'/home/user/Garden_Guardians/Tools')
import struct,json,io,numpy as np
from PIL import Image
Image.MAX_IMAGE_PIXELS=None
from kin_preview import raster,sample,label
def glb(path):
    d=open(path,'rb').read(); l,_=struct.unpack('<I4s',d[12:20]); js=json.loads(d[20:20+l]); return js,d[20+l+8:]
def acc(js,b,i):
    a=js['accessors'][i]; v=js['bufferViews'][a['bufferView']]; w={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
    dt={5126:'<f4',5125:'<u4',5123:'<u2',5121:'u1'}[a['componentType']]
    arr=np.frombuffer(b,dt,a['count']*w,v.get('byteOffset',0)+a.get('byteOffset',0)); return arr.reshape(-1,w) if w>1 else arr
def qmat(q):
    x,y,z,w=q; return np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]])
def pose(path,t,anim_path=None):
    js,b=glb(path); ajs,ab=(js,b) if anim_path is None else glb(anim_path)
    nodes=js['nodes']; skin=js['skins'][0]; ibm=acc(js,b,skin['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1)  # stored column-major
    # local TRS
    T=[np.array(n.get('translation',[0,0,0]),float) for n in nodes]; Rq=[np.array(n.get('rotation',[0,0,0,1]),float) for n in nodes]; S=[np.array(n.get('scale',[1,1,1]),float) for n in nodes]
    parent={}
    for i,n in enumerate(nodes):
        for c in n.get('children',[]): parent[c]=i
    if t is not None:
        anim=ajs['animations'][0]
        for ch in anim['channels']:
            s=anim['samplers'][ch['sampler']]; times=acc(ajs,ab,s['input']); vals=acc(ajs,ab,s['output'])
            tt=t%times[-1]; k=max(0,min(np.searchsorted(times,tt)-1,len(times)-2)); f=(tt-times[k])/max(times[k+1]-times[k],1e-9)
            nd=ch['target']['node']; path_=ch['target']['path']
            # animation file's node indices refer to ITS nodes; same skeleton order in these files
            if path_=='translation': T[nd]=vals[k]*(1-f)+vals[k+1]*f
            elif path_=='scale': S[nd]=vals[k]*(1-f)+vals[k+1]*f
            elif path_=='rotation':
                q0,q1=vals[k],vals[k+1]
                if np.dot(q0,q1)<0: q1=-q1
                q=q0*(1-f)+q1*f; Rq[nd]=q/np.linalg.norm(q)
    world={}
    def W(i):
        if i in world: return world[i]
        m=np.eye(4); m[:3,:3]=qmat(Rq[i])*S[i][None,:]; m[:3,3]=T[i]
        world[i]=(W(parent[i])@m) if i in parent else m; return world[i]
    p=js['meshes'][0]['primitives'][0]['attributes']
    pos=acc(js,b,p['POSITION']); J=acc(js,b,p['JOINTS_0']).astype(int); Wt=acc(js,b,p['WEIGHTS_0'])
    skinm=np.stack([W(j)@ibm[k] for k,j in enumerate(skin['joints'])])
    # glTF: jointMatrix = inverse(globalTransformOfNodeThatTheMeshIsAttachedTo) * globalJointTransform * inverseBindMatrix ; mesh node at identity here
    ph=np.hstack([pos,np.ones((len(pos),1))])
    out=np.zeros((len(pos),3))
    for k in range(4):
        m=skinm[J[:,k]]   # (n,4,4) using column-vector convention (glTF stores column-major; frombuffer gives row-major of transposed)
        out+=Wt[:,k:k+1]*np.einsum('nij,nj->ni',m,ph)[:,:3]
    return out,js,b
if __name__=='__main__':
    path=sys.argv[1]; anim=sys.argv[2]
    js,b=glb(path)
    uv=acc(js,b,js['meshes'][0]['primitives'][0]['attributes']['TEXCOORD_0']); idx=acc(js,b,js['meshes'][0]['primitives'][0]['indices']).reshape(-1,3).astype(int)
    v=js['bufferViews'][js['images'][0]['bufferView']]; tex=np.asarray(Image.open(io.BytesIO(b[v.get('byteOffset',0):v.get('byteOffset',0)+v['byteLength']])).convert('RGB'))
    ims=[]
    for t in (None,0.25,0.5,0.75):
        out,_,_=pose(path,t,anim)
        if t is not None: out=np.stack([out[:,0],-out[:,2],out[:,1]],axis=1)   # animated poses come out Y-up
        def fn(tr,l0,l1,l2):
            u=uv[tr[0]][None,:]*l0[:,None]+uv[tr[1]][None,:]*l1[:,None]+uv[tr[2]][None,:]*l2[:,None]
            return sample(tex,u)
        front=raster(out,idx,fn,H=420,W=300)
        side=np.stack([out[:,1],-out[:,0],out[:,2]],axis=1); sd=raster(side,idx,fn,H=420,W=300)
        ims.append((label(front,'bind' if t is None else 't=%.2f front'%t),label(sd,'side')))
    sheet=Image.new('RGB',(8*305,ims[0][0].height),(245,240,228)); x=0
    for a,c in ims:
        sheet.paste(a,(x,0)); sheet.paste(c,(x+305,0)); x+=610
    sheet.save(sys.argv[3])
