#!/usr/bin/env python3
"""把 tools/error.html 分发到三个桌面端：Windows/macOS 拷文件，Linux 转成 C 字符串。"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
SRC = os.path.join(HERE, "error.html")

COPIES = [
    os.path.join(ROOT, "desktop", "windows", "error.html"),
    os.path.join(ROOT, "desktop", "macos", "error.html"),
]

C_HEADER = os.path.join(ROOT, "desktop", "linux", "error_html.h")


def main():
    with open(SRC, encoding="utf-8") as f:
        html = f.read()

    for dst in COPIES:
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, "w", encoding="utf-8", newline="\n") as f:
            f.write(html)
        print("copied ->", os.path.relpath(dst, ROOT))

    lines = []
    for line in html.split("\n"):
        esc = line.replace("\\", "\\\\").replace('"', '\\"')
        lines.append('    "%s\\n"' % esc)
    with open(C_HEADER, "w", encoding="utf-8", newline="\n") as f:
        f.write("/* 由 tools/make_error_assets.py 生成，勿手改 */\n")
        f.write("static const char *ERROR_HTML =\n")
        f.write("\n".join(lines))
        f.write(";\n")
    print("wrote", os.path.relpath(C_HEADER, ROOT), "%.0f KB" % (os.path.getsize(C_HEADER) / 1024))


if __name__ == "__main__":
    main()
