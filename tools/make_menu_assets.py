#!/usr/bin/env python3
"""把 tools/menu.js 分发到三个语言侧的载体。

Windows / Android 直接拷文件；Linux 是 C，需要转成字符串常量头文件。
"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
SRC = os.path.join(HERE, "menu.js")

COPIES = [
]

C_HEADER = os.path.join(ROOT, "desktop", "linux", "menu_js.h")


def main():
    with open(SRC, encoding="utf-8") as f:
        js = f.read()

    for dst in COPIES:
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, "w", encoding="utf-8", newline="\n") as f:
            f.write(js)
        print("copied ->", os.path.relpath(dst, ROOT))

    # C 字符串：逐行转义，保留可读性
    lines = []
    for line in js.split("\n"):
        esc = line.replace("\\", "\\\\").replace('"', '\\"')
        lines.append('    "%s\\n"' % esc)
    with open(C_HEADER, "w", encoding="utf-8", newline="\n") as f:
        f.write("/* 由 tools/make_menu_assets.py 生成，勿手改 */\n")
        f.write("static const char *MENU_JS =\n")
        f.write("\n".join(lines))
        f.write(";\n")
    print("wrote", os.path.relpath(C_HEADER, ROOT), "%.0f KB" % (os.path.getsize(C_HEADER) / 1024))


if __name__ == "__main__":
    main()
