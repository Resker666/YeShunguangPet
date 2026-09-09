"""Validate publication drafts and decode their actual media outputs."""
from __future__ import annotations

import hashlib
from html import escape
from html.parser import HTMLParser
import json
from pathlib import Path
import sys
import unicodedata
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "artifacts" / "promo-tools" / "python"))
import imageio_ffmpeg
from markdown_it import MarkdownIt
from PIL import Image, ImageDraw
import yaml

MD = MarkdownIt("commonmark", {"html": True}).enable("table")


def slug(text):
    return "".join(c for c in text.lower() if c in "-_ " or not unicodedata.category(c).startswith(("P", "S"))).replace(" ", "-")


def anchors(path):
    tokens = MD.parse(path.read_text(encoding="utf-8"))
    return {slug(tokens[i + 1].content) for i, token in enumerate(tokens) if token.type == "heading_open"}


class HtmlLinks(HTMLParser):
    def __init__(self):
        super().__init__()
        self.links = []

    def handle_starttag(self, tag, attrs):
        self.links.extend(value for name, value in attrs if name in ("src", "href") and value)


def verify_documents():
    files = [ROOT / "README.md", ROOT / "ASSET_NOTICE.md", *sorted((ROOT / "docs").rglob("*.md"))]
    count = 0
    for path in files:
        tokens = MD.parse(path.read_text(encoding="utf-8"))
        links = []
        for token in tokens:
            for child in token.children or []:
                if child.type in ("link_open", "image"):
                    links.append(child.attrGet("href") or child.attrGet("src"))
            if token.type == "html_block":
                parser = HtmlLinks(); parser.feed(token.content); links.extend(parser.links)
        for value in links:
            parsed = urlsplit(value)
            if parsed.scheme or parsed.netloc:
                continue
            target = (path.parent / unquote(parsed.path)).resolve() if parsed.path else path
            if not target.is_relative_to(ROOT) or not target.exists():
                raise ValueError(f"Missing or external local target in {path.name}: {value}")
            if parsed.fragment and target.suffix == ".md" and unquote(parsed.fragment) not in anchors(target):
                raise ValueError(f"Missing heading in {path.name}: {value}")
            count += 1
    for path in (ROOT / ".github" / "ISSUE_TEMPLATE").glob("*.yml"):
        form = yaml.safe_load(path.read_text(encoding="utf-8"))
        assert form.get("name") and form.get("description") and form.get("body")
        ids = [field["id"] for field in form["body"] if "id" in field]
        assert len(ids) == len(set(ids)), path
    return {"markdown_files": len(files), "local_links": count, "issue_forms": 2}


def verify_media(output):
    media = ROOT / "docs" / "media"
    manifest = json.loads((media / "manifest.json").read_text(encoding="utf-8"))
    for asset in manifest["assets"]:
        data = (media / asset["name"]).read_bytes()
        assert len(data) == asset["bytes"] and hashlib.sha256(data).hexdigest() == asset["sha256"], asset["name"]
    for skin in manifest["skins"]:
        data = (ROOT / "Pets" / skin["Folder"] / "spritesheet.png").read_bytes()
        assert hashlib.sha256(data).hexdigest().upper() == skin["Sha256"]
    with Image.open(media / "social-preview.png") as image:
        assert image.size == (1280, 640) and (media / "social-preview.png").stat().st_size < 1_000_000
    with Image.open(media / "demo.gif") as image:
        duration = 0
        hashes = set()
        for index in range(image.n_frames):
            image.seek(index)
            duration += image.info.get("duration", 0)
            hashes.add(hashlib.sha256(image.convert("RGB").tobytes()).digest())
        assert duration == 12000 and len(hashes) > 20
        gif_count = image.n_frames
    frames = imageio_ffmpeg.read_frames(str(media / "demo-30s.mp4"), pix_fmt="rgb24")
    metadata = next(frames)
    assert metadata["size"] == (1280, 720) and abs(metadata["duration"] - 30) < 0.05
    samples = {}
    total = 0
    for index, data in enumerate(frames):
        if index in {0, 72, 192, 312, 408, 552, 600, 696}:
            image = Image.frombytes("RGB", metadata["size"], data)
            assert any(high - low > 80 for low, high in image.getextrema())
            samples[index] = image.resize((480, 270), Image.Resampling.LANCZOS)
        total += 1
    assert total == 720 and len(samples) == 8
    sheet = Image.new("RGB", (960, 4 * 298), "#FFFFFF")
    draw = ImageDraw.Draw(sheet)
    for slot, (index, image) in enumerate(samples.items()):
        x, y = (slot % 2) * 480, (slot // 2) * 298
        draw.text((x + 8, y + 6), f"{index / 24:.1f}s", fill="#202427")
        sheet.paste(image, (x, y + 28))
    sheet.save(output / "video-contact-sheet.png")
    return {"video_seconds": metadata["duration"], "video_frames": total, "gif_milliseconds": duration, "gif_frames": gif_count}


def render_readme(output):
    content = MD.render((ROOT / "README.md").read_text(encoding="utf-8"))
    styles = """
    *{box-sizing:border-box}body{margin:0;background:#fff;color:#24292f;font:16px/1.6 'Segoe UI','Microsoft YaHei',sans-serif;letter-spacing:0}
    article{max-width:940px;margin:auto;padding:32px}h1{font-size:32px;line-height:1.3}h2{font-size:24px;border-bottom:1px solid #d8dee4;padding-bottom:8px;margin-top:28px}
    a{color:#0969da;text-decoration:none}img{display:block;max-width:100%;height:auto}table{width:100%;border-collapse:collapse;font-size:14px}td,th{border:1px solid #d0d7de;padding:8px;text-align:left;overflow-wrap:anywhere}
    blockquote{margin:16px 0;padding:0 16px;border-left:4px solid #d0d7de;color:#57606a}code{font-size:14px;background:#eff1f3;border-radius:4px;padding:2px 4px}li{margin:7px 0}p{overflow-wrap:anywhere}
    @media(max-width:480px){article{padding:18px}h1{font-size:28px}table{font-size:13px}td,th{padding:6px}}
    """
    document = f'<!doctype html><html lang="zh-CN"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><base href="{escape(ROOT.as_uri())}/"><title>README preview</title><style>{styles}</style></head><body><article>{content}</article></body></html>'
    (output / "readme-preview.html").write_text(document, encoding="utf-8")


def main():
    output = ROOT / "artifacts" / "promotion-qa"
    output.mkdir(parents=True, exist_ok=True)
    result = {"documents": verify_documents(), "media": verify_media(output)}
    render_readme(output)
    (output / "verification.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
