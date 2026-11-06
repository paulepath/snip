"""Build site pictures: python tools/make_site_images.py

Needs Pillow and Edge/Chrome (or BROWSER_EXE). Source screenshots belong in
docs/screenshots/{editor,steps,capture}.png. Missing captures are skipped; no
mock screenshots are generated. Rerun after taking or replacing a capture.
"""

import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "site" / "img"
LOGOS = ROOT / "design" / "logo"
BG = (18, 20, 23)
MAX_WIDTH = 1600


def browser() -> str:
    candidates = [
        os.environ.get("BROWSER_EXE", ""),
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        shutil.which("msedge"), shutil.which("chromium"),
        shutil.which("chromium-browser"), shutil.which("google-chrome"),
    ]
    for candidate in candidates:
        if candidate and Path(candidate).is_file():
            return str(candidate)
    raise SystemExit("Edge or Chrome is needed to render the SVG logo. Set BROWSER_EXE to its executable.")


def render_svg(exe: str, source: Path, size: tuple[int, int]) -> Image.Image:
    """Draw the real SVG on a transparent page in an isolated headless profile."""
    width, height = size
    svg = ET.parse(source).getroot()
    svg.set("width", str(width))
    svg.set("height", str(height))
    ET.register_namespace("", "http://www.w3.org/2000/svg")
    with tempfile.TemporaryDirectory(prefix="snip-svg-") as directory:
        temp = Path(directory)
        page, png = temp / "logo.html", temp / "logo.png"
        page.write_text(
            '<!doctype html><meta charset="utf-8"><style>'
            'html,body{margin:0;background:transparent}svg{display:block}'
            '</style>' + ET.tostring(svg, encoding="unicode"), encoding="utf-8")
        command = [
            exe, "--headless=new", "--disable-gpu", "--hide-scrollbars",
            "--no-first-run", "--disable-extensions", "--disable-background-networking",
            "--default-background-color=00000000", "--force-device-scale-factor=1",
            "--run-all-compositor-stages-before-draw", "--virtual-time-budget=1000",
            f"--user-data-dir={temp / 'profile'}",
            f"--window-size={max(800, width)},{max(800, height + 150)}",
            f"--screenshot={png}", page.as_uri(),
        ]
        result = subprocess.run(command, capture_output=True, timeout=45)
        if result.returncode or not png.exists():
            raise RuntimeError(f"Browser could not rasterise {source.name}: {result.stderr.decode(errors='replace')[-1200:]}")
        with Image.open(png) as capture:
            if capture.width < width or capture.height < height:
                raise RuntimeError(f"Browser viewport was too small: {capture.size}, expected at least {size}")
            return capture.convert("RGBA").crop((0, 0, width, height))


def font(size: int) -> ImageFont.ImageFont:
    candidates = [
        Path(os.environ.get("WINDIR", r"C:\Windows")) / "Fonts" / "segoeui.ttf",
        Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
    ]
    for candidate in candidates:
        if candidate.is_file():
            return ImageFont.truetype(str(candidate), size)
    return ImageFont.load_default(size=size)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for name in ("mark-dark.svg", "snip-logo-dark.svg"):
        shutil.copyfile(LOGOS / name, OUT / name)

    editor = None
    for name in ("editor", "steps", "capture"):
        source = ROOT / "docs" / "screenshots" / f"{name}.png"
        if not source.is_file():
            print(f"Skip {source.relative_to(ROOT)} (not captured yet)")
            continue
        with Image.open(source) as original:
            image = original.convert("RGB")
        if image.width > MAX_WIDTH:
            image = image.resize((MAX_WIDTH, round(image.height * MAX_WIDTH / image.width)), Image.Resampling.LANCZOS)
        image.save(OUT / f"{name}.webp", "WEBP", quality=90, method=6)
        print(f"Written {name}.webp: {image.width} x {image.height}")
        if name == "editor":
            editor = image

    exe = browser()
    mark = render_svg(exe, LOGOS / "mark-dark.svg", (512, 512))
    if mark.getextrema()[3] == (0, 0):
        raise RuntimeError("The SVG rendered empty")
    mark.resize((64, 64), Image.Resampling.LANCZOS).save(OUT / "favicon.png", optimize=True)
    touch = Image.new("RGB", (180, 180), BG)
    small_mark = mark.resize((148, 148), Image.Resampling.LANCZOS)
    touch.paste(small_mark, (16, 16), small_mark)
    touch.save(OUT / "apple-touch-icon.png", optimize=True)

    # The SVG lockup supplies the lettering as paths, rather than substituting a font.
    lockup = render_svg(exe, LOGOS / "snip-logo-dark.svg", (816, 280))
    lockup = lockup.resize((408, 140), Image.Resampling.LANCZOS)
    og = Image.new("RGB", (1200, 630), BG)
    og.paste(lockup, (54, 32), lockup)
    draw = ImageDraw.Draw(og)
    draw.text((65, 195), "Capture it. Mark it up. Make your point.", font=font(38), fill="#E6E8EB")
    draw.text((65, 255), "A screenshot tool for Windows", font=font(24), fill="#A9AFB8")
    draw.text((895, 77), "snip.e-path.co.uk", font=font(24), fill="#5AA9F2")
    if editor is not None:
        shot = editor.resize((1072, round(editor.height * 1072 / editor.width)), Image.Resampling.LANCZOS)
        og.paste(shot, (64, 320))
        fade = Image.new("L", og.size, 0)
        fade_draw = ImageDraw.Draw(fade)
        for y in range(500, 630):
            fade_draw.line((0, y, 1200, y), fill=round(255 * (y - 500) / 129))
        og = Image.composite(Image.new("RGB", og.size, BG), og, fade)
    else:
        # A branded preview is still useful before real screenshots arrive.
        draw.line((65, 345, 1135, 345), fill="#30353C", width=2)
        draw.text((65, 402), "Print Screen. Pick a region. Add the explanation.", font=font(30), fill="#5AA9F2")
    og.save(OUT / "og.png", optimize=True)
    print("Written SVG copies, favicon.png, apple-touch-icon.png and og.png (1200 x 630)")


if __name__ == "__main__":
    main()
