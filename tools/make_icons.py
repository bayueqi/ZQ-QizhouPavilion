#!/usr/bin/env python3
"""从站点 favicon 生成 Android 各密度图标（传统 + adaptive foreground）。"""
import os
from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "site-icon.png")
RES = os.path.abspath(os.path.join(HERE, "..", "app", "src", "main", "res"))

DENSITIES = {"mdpi": 1.0, "hdpi": 1.5, "xhdpi": 2.0, "xxhdpi": 3.0, "xxxhdpi": 4.0}


def load_logo():
    im = Image.open(SRC).convert("RGBA")
    box = im.split()[3].getbbox()
    return im.crop(box) if box else im


def fit(im, side, ratio):
    """把 im 等比缩放到 side*ratio 的框内并居中，返回方形透明画布。"""
    box = int(side * ratio)
    w, h = im.size
    s = min(box / w, box / h)
    small = im.resize((max(1, int(w * s)), max(1, int(h * s))), Image.LANCZOS)
    canvas = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    canvas.paste(small, ((side - small.width) // 2, (side - small.height) // 2), small)
    return canvas


def rounded_bg(size, color=(255, 255, 255, 255), radius_ratio=0.22):
    mask = Image.new("L", (size, size), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [0, 0, size - 1, size - 1], radius=int(size * radius_ratio), fill=255
    )
    bg = Image.new("RGBA", (size, size), color)
    bg.putalpha(mask)
    return bg


def write(path, img):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path, "PNG", optimize=True)
    print("wrote", os.path.relpath(path, RES), img.size)


def main():
    logo = load_logo()

    for name, scale in DENSITIES.items():
        side = int(48 * scale)  # 48dp 传统图标
        base = rounded_bg(side)
        base.alpha_composite(fit(logo, side, 0.76))
        write(os.path.join(RES, "mipmap-" + name, "ic_launcher.png"), base)

        # 圆形版本（部分启动器 roundIcon 用）
        r = rounded_bg(side, radius_ratio=0.5)
        r.alpha_composite(fit(logo, side, 0.70))
        write(os.path.join(RES, "mipmap-" + name, "ic_launcher_round.png"), r)

        # adaptive icon 前景层：108dp 画布，内容控制在安全区（约 66%）内
        fg_side = int(108 * scale)
        fg = fit(logo, fg_side, 0.56)
        write(os.path.join(RES, "mipmap-" + name, "ic_launcher_foreground.png"), fg)

    # 启动页 / 设置页用的大图（不随密度缩放，保持清晰）
    big = rounded_bg(512)
    big.alpha_composite(fit(logo, 512, 0.78))
    write(os.path.join(RES, "drawable-nodpi", "logo.png"), big)


if __name__ == "__main__":
    main()
