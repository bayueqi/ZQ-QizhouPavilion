#!/usr/bin/env python3
"""生成 Windows 端的欢迎页 setup.html（图标内嵌为 base64，页面自包含）。"""
import base64
import os

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
LOGO = os.path.join(ROOT, "desktop", "linux", "qizhoupavilion.png")
OUT = os.path.join(ROOT, "desktop", "windows", "setup.html")

TPL = """<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="color-scheme" content="light">
<title>琪舟阁</title>
<style>
  * { margin:0; padding:0; box-sizing:border-box; user-select:none; }
  html, body { height:100%; }
  body {
    background:#FDF8F3;
    font-family:"Microsoft YaHei UI","Microsoft YaHei","Segoe UI",system-ui,sans-serif;
    display:flex; align-items:center; justify-content:center;
    overflow:hidden; position:relative;
  }
  body::before, body::after {
    content:""; position:absolute; border-radius:50%;
    filter:blur(70px); opacity:.6; pointer-events:none;
  }
  body::before { width:460px; height:460px; background:#EAD6BA; top:-150px; left:-120px; }
  body::after  { width:420px; height:420px; background:#C3D0E3; bottom:-170px; right:-130px; }

  .card {
    position:relative; width:376px; padding:36px 36px 30px;
    background:rgba(255,255,255,.74);
    border:1px solid rgba(255,255,255,.92);
    border-radius:24px;
    box-shadow:0 20px 52px rgba(48,85,128,.14), 0 2px 10px rgba(48,85,128,.05);
    backdrop-filter:blur(18px);
    text-align:center;
    animation:rise .42s cubic-bezier(.22,.9,.3,1) both;
  }
  @keyframes rise { from { opacity:0; transform:translateY(14px) scale(.98); } to { opacity:1; transform:none; } }

  .logo { width:94px; height:94px; border-radius:23px; box-shadow:0 10px 24px rgba(48,85,128,.18); }
  h1 { margin-top:18px; font-size:25px; letter-spacing:4px; color:#1F2937; font-weight:600; }
  .sub { margin-top:9px; font-size:12.5px; color:#9A9184; letter-spacing:.6px; }

  input {
    margin-top:26px; width:100%; height:47px; padding:0 15px;
    border-radius:14px; border:1.5px solid #E7DAC8; background:#fff;
    font-size:14.5px; color:#1F2937; outline:none; font-family:inherit;
    user-select:text;
    transition:border-color .18s, box-shadow .18s;
  }
  input::placeholder { color:#BCB3A5; }
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
    <img class="logo" src="data:image/png;base64,__LOGO__" alt="">
    <h1>琪舟阁</h1>
    <div class="sub">精品网址导航</div>
    <input id="url" type="text" spellcheck="false" autocomplete="off" placeholder="nav.example.com">
    <button id="go">进 入</button>
    <div class="tip" id="tip">填你部署好的导航站域名，只需一次</div>
  </div>
<script>
  var CURRENT = "__CURRENT__";
  var el = document.getElementById('url');
  var tip = document.getElementById('tip');
  if (CURRENT) el.value = CURRENT.replace(/^https?:\\/\\//, '').replace(/\\/+$/, '');
  el.focus();

  function send(text) {
    if (window.chrome && window.chrome.webview) { window.chrome.webview.postMessage(text); return; }
    if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.app) {
      window.webkit.messageHandlers.app.postMessage(text);
    }
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
</script>
</body>
</html>
"""


def main():
    with open(LOGO, "rb") as f:
        b64 = base64.b64encode(f.read()).decode("ascii")
    html = TPL.replace("__LOGO__", b64)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        f.write(html)
    print("wrote", os.path.relpath(OUT, ROOT), "%.0f KB" % (os.path.getsize(OUT) / 1024))


if __name__ == "__main__":
    main()
