import os
from PIL import Image
import numpy as np
from scipy.ndimage import map_coordinates
# Project root, three levels above this file (.playbot/tools/<script>.py).
ROOT = os.path.abspath(__file__).replace("\\", "/").rsplit("/", 3)[0] + "/"
src = np.asarray(Image.open(ROOT + ".playbot/art-src/wheel_rim_original.png").convert("RGBA")).astype(np.float32)
H,W,_ = src.shape; cx, cy, R = 471.0, 470.0, 470.0
N = 1024; out_c = N/2; OR = N/2 - 2
yy, xx = np.mgrid[0:N, 0:N].astype(np.float32)
dx, dy = xx-out_c, yy-out_c
r = np.sqrt(dx*dx+dy*dy)/OR; th = np.arctan2(dy, dx)
# output radial bands -> source radial ratio
# [0.80,0.86] -> src [0.585,0.645] inner gold lip ; [0.86,1.0] -> src [0.865,1.0] outer bulb band
src_r = np.where(r < 0.86, 0.585 + (r-0.80)/(0.06)*(0.645-0.585), 0.865 + (r-0.86)/(0.14)*(1.0-0.865))
sx = cx + src_r*R*np.cos(th); sy = cy + src_r*R*np.sin(th)
out = np.zeros((N,N,4), np.float32)
for c in range(4):
    out[...,c] = map_coordinates(src[...,c], [sy, sx], order=1, mode='constant', cval=0)
out[...,3] *= (r >= 0.80)  # hole
# soft AA at hole edge
edge = np.clip((r-0.80)/0.004, 0, 1); out[...,3] *= edge
Image.fromarray(out.clip(0,255).astype(np.uint8)).save(ROOT + "Assets/Art/Wheel/wheel_rim.png")
# detect bulbs on src ring ~0.955
lum = 0.3*src[...,0]+0.59*src[...,1]+0.11*src[...,2]
best=None
for rr in np.arange(0.93,0.985,0.005):
    t = np.linspace(-np.pi, np.pi, 2880, endpoint=False)
    v = lum[(cy+rr*R*np.sin(t)).astype(int), (cx+rr*R*np.cos(t)).astype(int)]
    peaks=[]; thr = np.percentile(v, 85)
    above = v>thr
    # group contiguous segments
    i=0; n=len(v)
    segs=[]
    while i<n:
        if above[i]:
            j=i
            while j<n and above[j]: j+=1
            segs.append((i,j)); i=j
        else: i+=1
    cnt=len(segs)
    if best is None or abs(cnt-0)>=0: 
        print(round(rr,3), "segments", cnt)
