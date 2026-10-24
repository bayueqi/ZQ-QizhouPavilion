#!/usr/bin/env python3
"""把 tools/inject.js 分发到各语言侧的载体（Windows/macOS/Android 拷文件，Linux 转 C 字符串）。"""
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
SRC = os.path.join(HERE, "inject.js")

COPIES = [
    os.path.join(ROOT, "desktop", "windows", "inject.js"),
    os.path.join(ROOT, "desktop", "macos", "inject.js"),
    os.path.join(ROOT, "app", "src", "main", "assets", "inject.js"),
]

C_HEADER = os.path.join(ROOT, "desktop", "linux", "inject_js.h")


def main():
    with open(SRC, encoding="utf-8") as f:
        js = f.read()

    for dst in COPIES:
        os.makedirs(os.path.dirname(dst), exist_ok=True)
        with open(dst, "w", encoding="utf-8", newline="\n") as f:
            f.write(js)
        print("copied ->", os.path.relpath(dst, ROOT))

    lines = []
    for line in js.split("\n"):
        esc = line.replace("\\", "\\\\").replace('"', '\\"')
        lines.append('    "%s\\n"' % esc)
    with open(C_HEADER, "w", encoding="utf-8", newline="\n") as f:
        f.write("/* 由 tools/make_inject_assets.py 生成，勿手改 */\n")
        f.write("static const char *INJECT_JS =\n")
        f.write("\n".join(lines))
        f.write(";\n")
    print("wrote", os.path.relpath(C_HEADER, ROOT), "%.0f KB" % (os.path.getsize(C_HEADER) / 1024))


if __name__ == "__main__":
    main()
