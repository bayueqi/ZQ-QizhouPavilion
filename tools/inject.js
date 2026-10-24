/*
 * 注入到每个页面的 App 侧脚本：手机端长按屏幕弹出功能菜单。
 * 回传统一是「动作名」：WebView2 走 postMessage、WKWebView 走 messageHandlers、Android 走 zqnavgo: scheme。
 * 动作：reload 刷新 / fullscreen 全屏 / change 更换域名 / back 后退上一页 / forward 前进下一页 / home 返回主页面
 */
(function () {
  if (window.__zqInjected) return;
  window.__zqInjected = true;

  var ITEMS = [
    ['reload', '刷新'],
    ['fullscreen', '全屏'],
    ['change', '更换域名'],
    ['back', '后退上一页'],
    ['forward', '前进下一页'],
    ['home', '返回主页面']
  ];

  function send(action) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(action);
      return;
    }
    if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.app) {
      window.webkit.messageHandlers.app.postMessage(action);
      return;
    }
    location.href = 'zqnavgo:' + encodeURIComponent(action);
  }

  /* ---------- 长按屏幕 → 功能菜单 ---------- */
  var sheetOpen = false;

  function showHomeSheet() {
    // 已经开着就别再叠一层
    if (sheetOpen) return;
    sheetOpen = true;

    var host = document.createElement('div');
    host.style.cssText = 'all:initial;position:fixed;left:0;top:0;width:0;height:0;z-index:2147483647;';
    var shadow = host.attachShadow({ mode: 'open' });

    var html = [
      '<style>',
      '.mask{position:fixed;left:0;top:0;right:0;bottom:0;background:rgba(16,24,40,.28);',
      'display:flex;align-items:center;justify-content:center;animation:zqfade .14s ease-out}',
      '@keyframes zqfade{from{opacity:0}to{opacity:1}}',
      '.card{background:#fff;border-radius:18px;padding:8px;box-shadow:0 20px 48px rgba(16,24,40,.3);',
      'font:16px/1.4 "PingFang SC","Microsoft YaHei UI",system-ui,sans-serif;animation:zqpop .16s ease-out}',
      '@keyframes zqpop{from{transform:scale(.94);opacity:0}to{transform:none;opacity:1}}',
      '.it{padding:15px 34px;border-radius:12px;color:#1F2937;white-space:nowrap;text-align:center}',
      '.it:active{background:rgba(48,85,128,.1);color:#254267}',
      '</style>',
      '<div class="mask" id="mask"><div class="card" id="card">'
    ];
    for (var i = 0; i < ITEMS.length; i++) {
      html.push('<div class="it" data-a="' + ITEMS[i][0] + '">' + ITEMS[i][1] + '</div>');
    }
    html.push('</div></div>');

    shadow.innerHTML = html.join('');
    document.documentElement.appendChild(host);

    function close() {
      sheetOpen = false;
      if (host.parentNode) host.parentNode.removeChild(host);
    }

    shadow.getElementById('mask').addEventListener('click', function (e) {
      if (e.target && e.target.id === 'mask') close();
    });

    // click 和 touchend 都接，防止移动端 click 被吞
    var nodes = shadow.querySelectorAll('.it');
    for (var j = 0; j < nodes.length; j++) {
      (function (node) {
        var done = false;
        function go(e) {
          if (done) return;
          done = true;
          if (e && e.cancelable) e.preventDefault();
          if (e && e.stopPropagation) e.stopPropagation();
          close();
          send(node.getAttribute('data-a'));
        }
        node.addEventListener('click', go);
        node.addEventListener('touchend', go);
      })(nodes[j]);
    }
  }

  function bindLongPress() {
    var timer = null;
    var moved = false;
    var x0 = 0;
    var y0 = 0;

    document.addEventListener('touchstart', function (e) {
      if (sheetOpen) return;
      if (e.touches.length !== 1) return;
      var t = e.target;
      if (t && t.closest && (t.closest('a') || t.closest('input') ||
          t.closest('textarea') || t.closest('select') || t.closest('button'))) return;
      x0 = e.touches[0].clientX;
      y0 = e.touches[0].clientY;
      moved = false;
      timer = setTimeout(function () {
        timer = null;
        if (moved || sheetOpen) return;
        try { if (navigator.vibrate) navigator.vibrate(12); } catch (e2) { }
        showHomeSheet();
      }, 520);
    }, { passive: true });

    document.addEventListener('touchmove', function (e) {
      if (!timer) return;
      var t = e.touches[0];
      if (Math.abs(t.clientX - x0) > 12 || Math.abs(t.clientY - y0) > 12) {
        moved = true;
        clearTimeout(timer);
        timer = null;
      }
    }, { passive: true });

    function cancel() {
      if (timer) {
        clearTimeout(timer);
        timer = null;
      }
    }
    document.addEventListener('touchend', cancel, { passive: true });
    document.addEventListener('touchcancel', cancel, { passive: true });
  }

  function boot() {
    bindLongPress();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, false);
  }
  boot();
  setTimeout(boot, 200);
})();
