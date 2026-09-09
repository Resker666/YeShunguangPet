"""Compose promotional media from native WPF exports, without editing source skins."""
from __future__ import annotations

import argparse
from functools import lru_cache
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys

from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[1]
PAPER = "#F5F7F9"
INK = "#202427"
MUTED = "#637077"
ACCENT = "#24796E"
REGULAR = Path("C:/Windows/Fonts/msyh.ttc")
BOLD = Path("C:/Windows/Fonts/msyhbd.ttc")


@lru_cache(maxsize=64)
def font(size: int, bold: bool = False):
    return ImageFont.truetype(str(BOLD if bold else REGULAR), size)


def text(canvas, xy, value, size=24, fill=INK, bold=False):
    draw = ImageDraw.Draw(canvas)
    box = draw.textbbox(xy, value, font=font(size, bold))
    if box[2] > canvas.width - 20 or box[3] > canvas.height - 12:
        raise ValueError(f"Text exceeds artwork bounds: {value}")
    draw.text(xy, value, font=font(size, bold), fill=fill)


@lru_cache(maxsize=256)
def picture(path: str, width: int):
    with Image.open(path) as image:
        image = image.convert("RGBA")
        height = round(image.height * width / image.width)
        return image.resize((width, height), Image.Resampling.LANCZOS)


def paste(canvas, path: Path, xy, width: int):
    image = picture(str(path), width)
    if xy[0] < 0 or xy[1] < 0 or xy[0] + image.width > canvas.width or xy[1] + image.height > canvas.height:
        raise ValueError(f"Image exceeds artwork bounds: {path}")
    canvas.paste(image, xy, image)


def sprite(capture, skin, action, seconds):
    durations = animation_map(capture, skin)[action]
    position = int(seconds * 1000) % sum(durations)
    for index, duration in enumerate(durations):
        if position < duration:
            return capture / skin / f"{action}-{index}.png"
        position -= duration
    raise AssertionError("Missing sprite frame")


@lru_cache(maxsize=4)
def animation_map(capture: Path, skin: str):
    return json.loads((capture / skin / "animations.json").read_text(encoding="utf-8"))


def base():
    canvas = Image.new("RGB", (1280, 720), PAPER)
    text(canvas, (54, 32), "叶瞬光桌面宠物", 46, bold=True)
    text(canvas, (56, 98), "Windows 桌面陪伴 · v2.4.0", 19, MUTED)
    ImageDraw.Draw(canvas).line((54, 672, 1226, 672), fill="#D7DFE2", width=1)
    text(canvas, (54, 684), "核心功能离线 · 无需登录", 16, MUTED)
    text(canvas, (898, 684), "非官方同人项目 · 演示数据", 16, MUTED)
    return canvas


def video_frame(capture: Path, seconds: float):
    canvas = base()
    if seconds < 7:
        text(canvas, (64, 220), "桌面上，多一个陪伴", 40, bold=True)
        text(canvas, (66, 292), "点击互动，也可以安静待着。", 24, MUTED)
        action = "Idle" if seconds < 2 else "Waving" if seconds < 5 else "Jumping"
        paste(canvas, sprite(capture, "ye-shunguang", action, seconds), (730, 166), 390)
    elif seconds < 13:
        text(canvas, (64, 226), "开始一次专注", 42, bold=True)
        text(canvas, (66, 302), "圆环调时，专注与休息分别设置。", 24, MUTED)
        paste(canvas, capture / "focus-running.png", (814, 128), 368)
    elif seconds < 21:
        text(canvas, (64, 206), "收起成迷你番茄钟", 38, bold=True)
        text(canvas, (66, 274), "可拖动、可置顶，切换不中断计时。", 23, MUTED)
        step = min(8, int(seconds - 13))
        paste(canvas, capture / f"mini-{step:02}.png", (644, 286), 544)
        paste(canvas, sprite(capture, "ye-shunguang", "Running", seconds), (304, 352), 244)
    elif seconds < 27:
        text(canvas, (54, 180), "预览不同角色", 36, bold=True)
        text(canvas, (56, 246), "支持导入完整皮肤包。", 23, MUTED)
        robin = seconds >= 24
        skin = "robin" if robin else "ye-shunguang"
        paste(canvas, capture / ("skin-robin.png" if robin else "skin-leaf.png"), (518, 126), 710)
        paste(canvas, sprite(capture, skin, "Idle", seconds), (110, 356), 246)
    else:
        text(canvas, (64, 220), "欢迎来试用", 46, bold=True)
        text(canvas, (66, 299), "完整解压 ZIP，运行 EXE。", 26, MUTED)
        text(canvas, (66, 350), "github.com/Resker666/YeShunguangPet", 23, ACCENT)
        paste(canvas, capture / "mini-00.png", (684, 322), 504)
        paste(canvas, sprite(capture, "ye-shunguang", "Waving", seconds), (470, 445), 172)
    return canvas


def covers(capture: Path, output: Path):
    canvas = Image.new("RGB", (1280, 640), PAPER)
    text(canvas, (64, 54), "叶瞬光桌面宠物", 64, bold=True)
    text(canvas, (68, 151), "离线陪伴 · 迷你番茄钟 · 自定义皮肤", 27, MUTED)
    paste(canvas, sprite(capture, "ye-shunguang", "Idle", 0), (110, 228), 300)
    paste(canvas, capture / "mini-00.png", (544, 280), 648)
    text(canvas, (64, 584), "Windows 10 / 11 x64", 19, ACCENT)
    text(canvas, (890, 584), "v2.4.0 · 非官方同人项目", 18, MUTED)
    canvas.save(output / "social-preview.png", optimize=True)
    canvas = Image.new("RGB", (1080, 1080), PAPER)
    text(canvas, (68, 65), "叶瞬光桌面宠物", 68, bold=True)
    text(canvas, (72, 174), "让桌面多一点陪伴", 34, MUTED)
    paste(canvas, capture / "mini-00.png", (112, 290), 856)
    paste(canvas, sprite(capture, "ye-shunguang", "Waving", 0.4), (100, 690), 258)
    text(canvas, (470, 720), "离线运行", 36, bold=True)
    text(canvas, (470, 785), "迷你专注计时", 36, bold=True)
    text(canvas, (470, 850), "可换角色皮肤", 36, bold=True)
    text(canvas, (70, 1002), "Windows 版 · v2.4.0 · 非官方同人项目", 22, MUTED)
    canvas.save(output / "post-cover-square.png", optimize=True)
    video_frame(capture, 15).save(output / "overview.png", optimize=True)
    for name in ("mini-00.png", "focus-ready.png", "skin-robin.png"):
        shutil.copyfile(capture / name, output / name)


def find_ffmpeg(explicit):
    if explicit:
        return explicit
    if shutil.which("ffmpeg"):
        return shutil.which("ffmpeg")
    sys.path.insert(0, str(ROOT / "artifacts" / "promo-tools" / "python"))
    import imageio_ffmpeg
    return imageio_ffmpeg.get_ffmpeg_exe()


def make_video(capture, output, executable):
    args = [executable, "-y", "-loglevel", "error", "-f", "rawvideo", "-pix_fmt", "rgb24", "-s", "1280x720", "-r", "24", "-i", "-",
            "-an", "-c:v", "libx264", "-preset", "fast", "-crf", "20", "-pix_fmt", "yuv420p", "-movflags", "+faststart", str(output / "demo-30s.mp4")]
    with subprocess.Popen(args, stdin=subprocess.PIPE, stderr=subprocess.PIPE) as process:
        try:
            for index in range(30 * 24):
                process.stdin.write(video_frame(capture, index / 24).tobytes())
            process.stdin.close()
            error = process.stderr.read().decode("utf-8", "replace")
            if process.wait(timeout=180):
                raise RuntimeError(error)
        except BaseException:
            process.kill()
            process.wait()
            raise


def make_gif(capture, output):
    frames = []
    for index in range(12 * 10):
        local = index / 10
        seconds = local if local < 4 else local + 9 if local < 8 else local + 14
        image = video_frame(capture, seconds).resize((864, 486), Image.Resampling.LANCZOS)
        frames.append(image.quantize(colors=192, method=Image.Quantize.FASTOCTREE, dither=Image.Dither.NONE))
    frames[0].save(output / "demo.gif", save_all=True, append_images=frames[1:], duration=100, loop=0, optimize=True, disposal=2)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--capture", type=Path, default=ROOT / "artifacts" / "promotion-capture")
    parser.add_argument("--output", type=Path, default=ROOT / "docs" / "media")
    parser.add_argument("--ffmpeg")
    args = parser.parse_args()
    metadata = json.loads((args.capture / "capture.json").read_text(encoding="utf-8"))
    if metadata["Version"] != "2.4.0" or metadata["PrivateUserDataLoaded"]:
        raise ValueError("Expected isolated v2.4.0 captures")
    args.output.mkdir(parents=True, exist_ok=True)
    covers(args.capture, args.output)
    make_gif(args.capture, args.output)
    make_video(args.capture, args.output, find_ffmpeg(args.ffmpeg))
    if (args.output / "social-preview.png").stat().st_size >= 1_000_000:
        raise ValueError("Social preview exceeds GitHub's image budget")
    manifest = {"version": metadata["Version"], "source": metadata["Source"], "skins": metadata["Skins"], "assets": []}
    for path in sorted(args.output.iterdir()):
        if path.suffix not in (".png", ".gif", ".mp4"):
            continue
        data = path.read_bytes()
        item = {"name": path.name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()}
        manifest["assets"].append(item)
        print(f"{path.name}: {len(data):,} bytes")
    (args.output / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
