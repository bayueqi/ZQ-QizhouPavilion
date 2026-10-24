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
    # WebP 无损：同样的画质比 PNG 小一半左右，直接省安装包体积
    img.save(path, "WEBP", lossless=True, quality=100, method=6)
    print("wrote", os.path.relpath(path, RES), img.size, "%dKB" % (os.path.getsize(path) // 1024))
    stale = path[:-5] + ".png"
    if os.path.exists(stale):
        os.remove(stale)


def main():
    logo = load_logo()

    for name, scale in DENSITIES.items():
        side = int(48 * scale)  # 48dp 传统图标
        base = rounded_bg(side)
        base.alpha_composite(fit(logo, side, 0.76))
        write(os.path.join(RES, "mipmap-" + name, "ic_launcher.webp"), base)

        # 圆形版本（部分启动器 roundIcon 用）
        r = rounded_bg(side, radius_ratio=0.5)
        r.alpha_composite(fit(logo, side, 0.70))
        write(os.path.join(RES, "mipmap-" + name, "ic_launcher_round.webp"), r)

        # adaptive icon 前景层：108dp 画布，内容控制在安全区（约 66%）内
        fg_side = int(108 * scale)
        fg = fit(logo, fg_side, 0.56)
        write(os.path.join(RES, "mipmap-" + name, "ic_launcher_foreground.webp"), fg)

    # 启动页 / 设置页用的大图（不随密度缩放，保持清晰）
    big = rounded_bg(512)
    big.alpha_composite(fit(logo, 512, 0.78))
    write(os.path.join(RES, "drawable-nodpi", "logo.webp"), big)

    make_desktop(logo)


def make_desktop(logo):
    """桌面三平台图标：Windows .ico / macOS .iconset / Linux .png"""
    root = os.path.abspath(os.path.join(HERE, "..", "desktop"))

    # Windows：单文件多尺寸 ico
    ico_dir = os.path.join(root, "windows")
    os.makedirs(ico_dir, exist_ok=True)
    ico_img = rounded_bg(256)
    ico_img.alpha_composite(fit(logo, 256, 0.78))
    ico_img.save(os.path.join(ico_dir, "app.ico"), "ICO",
                 sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("wrote desktop/windows/app.ico")

    # macOS：iconutil 需要的 iconset
    iconset = os.path.join(root, "macos", "icon.iconset")
    os.makedirs(iconset, exist_ok=True)
    for base in (16, 32, 128, 256, 512):
        for scale in (1, 2):
            px = base * scale
            if px > 1024:
                continue
            name = "icon_%dx%d%s.png" % (base, base, "@2x" if scale == 2 else "")
            img = rounded_bg(px)
            img.alpha_composite(fit(logo, px, 0.78))
            img.save(os.path.join(iconset, name), "PNG", optimize=True)
    print("wrote desktop/macos/icon.iconset (%d files)" % len(os.listdir(iconset)))

    # Linux：一张 256 png
    lin_dir = os.path.join(root, "linux")
    os.makedirs(lin_dir, exist_ok=True)
    png = rounded_bg(256)
    png.alpha_composite(fit(logo, 256, 0.78))
    png.save(os.path.join(lin_dir, "qizhoupavilion.png"), "PNG", optimize=True)
    print("wrote desktop/linux/qizhoupavilion.png")



if __name__ == "__main__":
    main()
