# Procedural textures: glows, rings, slash arc, dust, shadow, circle, edge fade, wheel detail.
import os
import numpy as np
from PIL import Image
from scipy.ndimage import gaussian_filter
# Project root, three levels above this file (.playbot/tools/<script>.py).
ROOT = os.path.abspath(__file__).replace("\\", "/").rsplit("/", 3)[0] + "/"
OUT = ROOT + "Assets/Art/"
def save(arr, path):
    Image.fromarray(np.clip(arr*255,0,255).astype(np.uint8), "RGBA").save(OUT+path)
def grid(n):
    y,x = np.mgrid[0:n,0:n].astype(np.float32); c=(n-1)/2
    return (x-c)/c, (y-c)/c
def rgba(rgb, a):
    out = np.zeros(a.shape+(4,), np.float32); out[...,:3]=rgb; out[...,3]=a; return out

x,y = grid(256); r = np.sqrt(x*x+y*y)
save(rgba((1,1,1), np.clip(1-r,0,1)**2.2), "VFX/vfx_glow.png")
save(rgba((1,1,1), np.exp(-((r-0.8)/0.08)**2)*(r<1)), "VFX/vfx_ring.png")
# circle (AA solid) for pegs / dots
x,y = grid(128); r=np.sqrt(x*x+y*y)
save(rgba((1,1,1), np.clip((1-r)*40,0,1)), "VFX/vfx_circle.png")
# shadow ellipse
x,y = grid(256); r=np.sqrt(x*x+(y*1.0)**2)
save(rgba((0,0,0), np.clip(1-r,0,1)**1.5*0.8), "VFX/shadow.png")
# dust puff: noisy soft blob
rng = np.random.default_rng(3)
n = gaussian_filter(rng.random((256,256)), 10); n=(n-n.min())/(n.max()-n.min())
x,y = grid(256); r=np.sqrt(x*x+y*y)
a = np.clip(1-r*1.1,0,1)**1.2 * (0.55+0.45*n)
save(rgba((1,1,1), np.clip(a*1.4,0,1)), "VFX/vfx_dust.png")
# slash crescent arc (white core, gold edges) 512x512
N=512; x,y = grid(N)
r1 = np.sqrt((x+0.15)**2+(y+0.05)**2); r2 = np.sqrt((x+0.32)**2+(y+0.18)**2)
ang = np.arctan2(y, x)
band = np.clip((0.92-r1)*12,0,1)*np.clip((r2-0.95)*12,0,1)
taper = np.clip(np.sin(np.clip((ang+2.4)/3.2,0,1)*np.pi),0,1)**0.8
a = gaussian_filter(band*taper, 2)
core = gaussian_filter(np.clip((0.86-r1)*14,0,1)*np.clip((r2-1.02)*14,0,1)*taper, 1.5)
col = np.zeros((N,N,4),np.float32)
col[...,0]=1.0; col[...,1]=0.82+0.18*core; col[...,2]=0.45+0.55*core; col[...,3]=np.clip(a*1.2,0,1)
save(col, "VFX/vfx_slash.png")
# horizontal edge fade (black -> transparent), 256x16
g = np.linspace(1,0,256)[None,:].repeat(16,0)**1.6
save(rgba((0,0,0), g), "Backgrounds/edge_fade.png")
# wheel detail texture: parchment-like grain, mid grey average 0.5
n1 = gaussian_filter(rng.random((512,512)), 1.2); n2 = gaussian_filter(rng.random((512,512)), 6)
d = 0.5 + (n1-n1.mean())*1.4 + (n2-n2.mean())*2.2
d = np.clip(d, 0.25, 0.75)
save(rgba(np.stack([d,d,d],-1), np.ones_like(d)), "Wheel/wheel_detail.png")
# gem glow for rim lights (soft core + halo)
x,y = grid(128); r=np.sqrt(x*x+y*y)
a = np.clip(np.exp(-(r/0.22)**2)*1.0 + np.exp(-(r/0.6)**2)*0.45,0,1)*(r<1)
save(rgba((1,1,1), a), "Wheel/bulb_glow.png")
print("ok")
