import sys, os
from PIL import Image, ImageDraw
out = sys.argv[1]; files = sys.argv[2:]
cell = 300; cols = 6
rows = (len(files)+cols-1)//cols
sheet = Image.new("RGB", (cols*cell, rows*(cell+22)), (40,40,48))
d = ImageDraw.Draw(sheet)
for i,f in enumerate(files):
    im = Image.open(f).convert("RGBA")
    w,h = im.size
    bg = Image.new("RGBA", im.size, (90,90,100,255))
    # checker
    ck = Image.new("RGBA", im.size, (120,120,130,255)); cd = ImageDraw.Draw(ck)
    s = max(8, w//24)
    for y in range(0,h,s):
        for x in range(0,w,s):
            if (x//s+y//s)%2: cd.rectangle([x,y,x+s-1,y+s-1], fill=(80,80,90,255))
    comp = Image.alpha_composite(ck, im)
    comp.thumbnail((cell-6, cell-6))
    cx, cy = (i%cols)*cell, (i//cols)*(cell+22)
    sheet.paste(comp.convert("RGB"), (cx+3, cy+3))
    d.text((cx+4, cy+cell+3), f"{os.path.basename(f)[:34]} {w}x{h}", fill=(230,230,230))
sheet.save(out)
