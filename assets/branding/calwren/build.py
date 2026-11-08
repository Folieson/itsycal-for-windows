from pathlib import Path
import subprocess, shutil, shutil as sh
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(r"E:\Dev\IdeaProjects\DSH\.dsh-tmp\branding\calwren")
SRC = ROOT / "iter3" / "1-fluent-binding.svg"
OUT = ROOT / "final-fluent"
TMP = OUT / "_render"
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
PAPER = (241, 231, 216); INK = (38, 33, 28); TERRA = (181, 81, 54)
FONT = r"C:\Windows\Fonts\segoeui.ttf"; FONT_SB = r"C:\Windows\Fonts\seguisb.ttf"
FINAL_A = ROOT / "final"

OUT.mkdir(parents=True, exist_ok=True)
for p in ["app", "tray", "wordmark", "lockup", "windows", "proofs", "sources"]:
    (OUT / p).mkdir(parents=True, exist_ok=True)
if TMP.exists(): shutil.rmtree(TMP)
TMP.mkdir(parents=True)

svg = SRC.read_text(encoding="utf-8")
# app tile: full paper rounded tile
def render(svg_text, size, path: Path, bg=(0, 0, 0, 0)):
    head = f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 512 512">'
    body = svg_text.split('\n', 1)[1]
    tmp = TMP / f"{path.stem}.svg"; tmp.write_text(head + "\n" + body, encoding="utf-8")
    out = TMP / f"{path.stem}.png"
    cmd = [CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--force-device-scale-factor=1",
           f"--window-size={size},{size}", "--default-background-color=00000000", f"--screenshot={out}", tmp.as_uri()]
    try:
        subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=25, check=False)
    except subprocess.TimeoutExpired:
        pass
    if not out.exists(): raise RuntimeError(f"Chrome did not write {out}")
    im = Image.open(out).convert("RGBA")
    if im.size != (size, size): raise RuntimeError(f"bad size {im.size}")
    canvas = Image.new("RGBA", (size, size), bg); canvas.alpha_composite(im)
    canvas.save(path)
    return path

app_sizes = [512, 300, 256, 150, 100, 50]
for s in app_sizes:
    render(svg, s, OUT / "app" / f"calwren-app-{s}.png")

# tray: transparent, same mark; 16-48 native renders
for s in [16, 20, 24, 32, 48]:
    render(svg, s, OUT / "tray" / f"calwren-tray-{s}.png")

# ico: largest sizes from app tile
ico_pngs = []
for s in [16, 24, 32, 48, 64, 128, 256]:
    p = OUT / "windows" / f"calwren-ico-{s}.png"
    render(svg, s, p)
    ico_pngs.append(Image.open(p))
base = ico_pngs[-1].copy()
base.save(OUT / "windows" / "calwren.ico", format="ICO", sizes=[(p.width, p.height) for p in ico_pngs])

# copy wordmark from the approved production set
wm = {}
for theme in ("light", "dark"):
    src = FINAL_A / "wordmark" / f"calwren-wordmark-{theme}-1024.png"
    dst = OUT / "wordmark" / f"calwren-wordmark-{theme}-1024.png"
    if src.exists(): sh.copyfile(src, dst)
    for s in (1024, 512, 256):
        p = OUT / "wordmark" / f"calwren-wordmark-{theme}-{s}.png"
        if src.exists():
            im = Image.open(src).convert("RGBA")
            im = im.resize((s, max(1, round(im.height * s / im.width))), Image.LANCZOS)
            im.save(p)
            wm[(theme, s)] = im
    svg_src = FINAL_A / "sources" / "wordmark" / f"calwren-wordmark-{theme}.svg"
    if svg_src.exists(): sh.copyfile(svg_src, OUT / "sources" / f"calwren-wordmark-{theme}.svg")

# lockups: icon + wordmark on light/dark background
for theme, bg in (("light", (247, 244, 239, 255)), ("dark", (30, 28, 26, 255))):
    ic = Image.open(OUT / "app" / "calwren-app-256.png").convert("RGBA")
    icon = ic.resize((160, 160), Image.LANCZOS)
    word = wm.get((theme, 512))
    if word is None: continue
    word = word.resize((720, round(word.height * 720 / word.width)), Image.LANCZOS)
    H = 256
    canvas = Image.new("RGBA", (1024, H), bg)
    canvas.alpha_composite(icon, (24, (H - 160) // 2))
    canvas.alpha_composite(word, (216, (H - word.height) // 2))
    canvas.convert("RGB").save(OUT / "lockup" / f"calwren-lockup-{theme}-1024.png")
    for s in (512, 256):
        im = canvas.convert("RGB").resize((s, round(H * s / 1024)), Image.LANCZOS)
        im.save(OUT / "lockup" / f"calwren-lockup-{theme}-{s}.png")

# contact sheet
sheet = Image.new("RGB", (1180, 900), (247, 244, 239))
d = ImageDraw.Draw(sheet)
f1 = ImageFont.truetype(FONT_SB, 26); f2 = ImageFont.truetype(FONT, 16)
d.text((32, 24), "Calwren — Fluent Binding / approved mark", font=f1, fill=INK)
# app tile light/dark
tile = Image.open(OUT / "app" / "calwren-app-256.png").convert("RGBA")
for i, bg in enumerate([(247, 244, 239), (30, 28, 26)]):
    c = Image.new("RGBA", (256, 256), bg + (255,)); c.alpha_composite(tile)
    sheet.paste(c.convert("RGB"), (48 + i * 300, 90))
    d.text((48 + i * 300, 356), "app tile light" if i == 0 else "app tile dark", font=f2, fill=(90, 80, 70) if i == 0 else (220, 210, 200))
# tray strip
tray_y = 430
for i, s in enumerate([16, 20, 24, 32, 48]):
    im = Image.open(OUT / "tray" / f"calwren-tray-{s}.png").convert("RGBA")
    c = Image.new("RGBA", (s, s), (247, 244, 239, 255)); c.alpha_composite(im)
    sheet.paste(c.convert("RGB"), (48 + i * 90, tray_y))
    d.text((48 + i * 90, tray_y + 60), f"{s}px", font=f2, fill=(90, 80, 70))
# wordmark
if ("light", 512) in wm:
    w = wm[("light", 512)].convert("RGBA")
    c = Image.new("RGBA", (w.width, w.height), (247, 244, 239, 255)); c.alpha_composite(w)
    sheet.paste(c.convert("RGB"), (560, 90))
    d.text((560, 90 + w.height + 8), "wordmark calwren (lowercase)", font=f2, fill=(90, 80, 70))
# lockup
lp = OUT / "lockup" / "calwren-lockup-light-512.png"
if lp.exists():
    sheet.paste(Image.open(lp).convert("RGB"), (560, 250))
    d.text((560, 250 + 140), "lockup", font=f2, fill=(90, 80, 70))
# small proof enlarged
big = Image.open(OUT / "tray" / "calwren-tray-16.png").convert("RGBA")
c = Image.new("RGBA", (16, 16), (247, 244, 239, 255)); c.alpha_composite(big)
sheet.paste(c.convert("RGB").resize((256, 256), Image.NEAREST), (48, 600))
d.text((48, 860), "16 px native, nearest 16x", font=f2, fill=(90, 80, 70))
sheet.save(OUT / "final-sheet.png")

# assets doc
doc = f"""# Calwren — final assets (Fluent Binding)

Approved mark: **Fluent Binding** — warm wren perched on an ink binding bar, paper page with
7-column / 2-row grid and a terracotta rounded date cell under the feet. Fluent 2 cues:
rounded surfaces, layered shadow, accent fill, pixel-snapped tray masters.

Palette: paper #F1E7D8 · page #FBF6EC · ink #26211C · terracotta #B55136 · stone #9C8873.
Contrast: ink/paper 13.03:1; terracotta/paper 4.09:1.

## Files
- app/ — 512, 300 (Store), 256, 150, 100, 50 px RGBA tiles.
- tray/ — 16, 20, 24, 32, 48 px native renders (transparent corners).
- windows/calwren.ico — 16/24/32/48/64/128/256; PNG rasters in windows/.
- wordmark/ — lowercase `calwren` wordmark, 1024/512/256, light/dark.
- lockup/ — icon+wordmark, 1024/512/256, light/dark.
- sources/ — icon SVG and wordmark SVGs.
- final-sheet.png — contact sheet; proofs/ — tray proof.

## Notes
- Wordmark comes from the previously approved custom-outline production set; no font is embedded.
- The app tile is paper at every size; no dark-tile variant.
- 16 px uses the same geometry with pixel-snapped edges; binding holes and shadow flatten by design.
- `.ico` is generated from the PNG rasters; verify the consumer loads the sizes it needs.
- Pending: product integration (v2.0.0), MSIX/Store metadata, tray implementation using the 16/32 masters.
"""
(OUT / "final-assets.md").write_text(doc, encoding="utf-8")
print("OK", OUT)
