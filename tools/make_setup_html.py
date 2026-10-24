#!/usr/bin/env python3
"""生成欢迎页 setup.html（图标内嵌 base64，页面自包含）。

一份 HTML 三个平台共用，靠 send() 自动适配各平台的回传通道：
  Windows/WebView2 -> chrome.webview.postMessage
  Linux/macOS      -> window.webkit.messageHandlers.app
  Android          -> 自定义 scheme: zqnavgo:<urlencode>
"""
import base64
import io
import os

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
LOGO = os.path.join(ROOT, "desktop", "linux", "qizhoupavilion.png")

OUTS = [
    os.path.join(ROOT, "desktop", "windows", "setup.html"),
    os.path.join(ROOT, "app", "src", "main", "assets", "setup.html"),
]

TPL = """<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<meta name="color-scheme" content="light">
<title>琪舟阁</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; user-select:none; -webkit-tap-highlight-color:transparent; }
  html, body { height:100%; }
  body {
    background:#FDF8F3;
    font-family:"PingFang SC","Microsoft YaHei UI","Microsoft YaHei","Noto Sans CJK SC",system-ui,sans-serif;
    display:flex; align-items:center; justify-content:center;
    overflow:hidden; position:relative;
    padding:20px;
  }
  body::before, body::after {
    content:""; position:absolute; border-radius:50%;
    filter:blur(70px); opacity:.62; pointer-events:none;
  }
  body::before { width:460px; height:460px; background:#EAD6BA; top:-150px; left:-120px; }
  body::after  { width:420px; height:420px; background:#C3D0E3; bottom:-170px; right:-130px; }

  .card {
    position:relative; width:100%; max-width:376px; padding:36px 32px 30px;
    background:rgba(255,255,255,.74);
    border:1px solid rgba(255,255,255,.92);
    border-radius:24px;
    box-shadow:0 20px 52px rgba(48,85,128,.14), 0 2px 10px rgba(48,85,128,.05);
    backdrop-filter:blur(18px); -webkit-backdrop-filter:blur(18px);
    text-align:center;
    animation:rise .45s cubic-bezier(.22,.9,.3,1) both;
  }
  @keyframes rise { from { opacity:0; transform:translateY(16px) scale(.98); } to { opacity:1; transform:none; } }

  .close {
    display:none; position:absolute; top:14px; right:14px;
    width:30px; height:30px; line-height:30px; border-radius:50%;
    color:#A79E90; font-size:15px; cursor:pointer;
    transition:background .18s, color .18s;
  }
  .close:hover { background:rgba(48,85,128,.08); color:#305580; }

  .logo { width:92px; height:92px; border-radius:23px; box-shadow:0 10px 24px rgba(48,85,128,.18); }
  h1 { margin-top:18px; font-size:25px; letter-spacing:4px; color:#1F2937; font-weight:600; }
  .sub { margin-top:9px; font-size:12.5px; color:#9A9184; letter-spacing:.6px; }

  input {
    margin-top:26px; width:100%; height:47px; padding:0 15px;
    border-radius:14px; border:1.5px solid #E7DAC8; background:#fff;
    font-size:16px; color:#1F2937; outline:none; font-family:inherit;
    user-select:text; -webkit-user-select:text;
    transition:border-color .18s, box-shadow .18s;
  }
  input::placeholder { color:#BCB3A5; font-size:14.5px; }
  input:focus { border-color:#305580; box-shadow:0 0 0 3px rgba(48,85,128,.12); }

  button {
    margin-top:14px; width:100%; height:48px;
    border:none; border-radius:14px; cursor:pointer;
    background:linear-gradient(180deg,#3B6391,#2A4A70);
    color:#fff; font-size:15px; letter-spacing:5px; font-family:inherit; font-weight:500;
    box-shadow:0 10px 24px rgba(48,85,128,.26);
    transition:box-shadow .2s, transform .1s;
  }
  button:hover { box-shadow:0 13px 28px rgba(48,85,128,.34); }
  button:active { transform:translateY(1px) scale(.996); }

  .tip { margin-top:15px; font-size:11.5px; color:#AFA697; line-height:1.75; }
  .tip.err { color:#C2410C; }
</style>
</head>
<body>
  <div class="card">
    <div class="close" id="back">✕</div>
    <img class="logo" src="data:image/webp;base64,__LOGO__" alt="">
    <h1>琪舟阁</h1>
    <div class="sub">精品网址导航</div>
    <input id="url" type="text" spellcheck="false" autocomplete="off" autocapitalize="off"
           autocorrect="off" placeholder="nav.example.com">
    <button id="go">进 入</button>
    <div class="tip" id="tip">填你部署好的导航站域名，只需一次</div>
  </div>
<script>
  var CURRENT = "__CURRENT__";
  var el = document.getElementById('url');
  var tip = document.getElementById('tip');
  var back = document.getElementById('back');

  if (CURRENT) {
    el.value = CURRENT.replace(/^https?:\\/\\//, '').replace(/\\/+$/, '');
    back.style.display = 'block';
    tip.textContent = '换一个导航站域名';
  }
  el.focus();

  function send(text) {
    if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage(text); return; }
    if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.app) {
      window.webkit.messageHandlers.app.postMessage(text); return;
    }
    location.href = 'zqnavgo:' + encodeURIComponent(text);
  }

  function bad(msg) { tip.textContent = msg; tip.className = 'tip err'; el.focus(); }

  function go() {
    var v = el.value.trim();
    if (!v) { bad('请输入域名'); return; }
    v = v.replace(/^https?:\\/\\//i, '').replace(/\\/+$/, '');
    if (v.indexOf('.') < 0) { bad('域名格式不对，例如 nav.example.com'); return; }
    tip.className = 'tip';
    tip.textContent = '正在进入…';
    send('https://' + v);
  }

  document.getElementById('go').addEventListener('click', go);
  el.addEventListener('keydown', function (e) { if (e.key === 'Enter') go(); });
  back.addEventListener('click', function () { send('cancel'); });
</script>
</body>
</html>
"""


def main():
    # 用无损 WebP 内嵌，比 PNG 小一半以上，页面体积直接影响 APK 大小
    buf = io.BytesIO()
    Image.open(LOGO).convert("RGBA").save(buf, "WEBP", lossless=True, quality=100, method=6)
    b64 = base64.b64encode(buf.getvalue()).decode("ascii")
    html = TPL.replace("__LOGO__", b64)
    for out in OUTS:
        os.makedirs(os.path.dirname(out), exist_ok=True)
        with open(out, "w", encoding="utf-8", newline="\n") as f:
            f.write(html)
        print("wrote", os.path.relpath(out, ROOT), "%.0f KB" % (os.path.getsize(out) / 1024))


if __name__ == "__main__":
    main()
