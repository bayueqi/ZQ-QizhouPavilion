/*
 * 注入到每个页面的 App 侧脚本：
 *   1. 在侧边栏「后台管理」下面插入「更改域名」入口（复用它的 class，样式一致）
 *   2. 手机端长按屏幕 → 弹出「返回主页面」
 * 回传统一是 "动作名"：WebView2 走 postMessage、WKWebView 走 messageHandlers、Android 走 zqnavgo: scheme。
 */
(function () {
  if (window.__zqInjected) return;
  window.__zqInjected = true;

  var ICON = '<svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5 mr-2" fill="none" ' +
    'viewBox="0 0 24 24" stroke="currentColor">' +
    '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" ' +
    'd="M8 7h12m0 0l-4-4m4 4l-4 4m0 6H4m0 0l4 4m-4-4l4-4" /></svg>';

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

  /* ---------- 侧边栏「更改域名」 ---------- */
  /*
   * 锚点必须限制在侧边栏里。
   * 之前是 document.querySelector('a[href*="qizhoupavilion" i]') 全文档找，
   * 后台表格里那条书签的 url 也带 qizhoupavilion（_worker.js 把 url 渲染成 <a>），
   * 结果按钮被插进了表格单元格，变成一条没样式的裸链接——就是「更改域名跑到别处」的 bug。
   */

  /** 首页侧边栏：<aside> 里一定有指向 /admin 的入口；后台页的 aside 是分类树，不含这个链接。 */
  function sidebarOf() {
    var asides = document.querySelectorAll('aside');
    for (var i = 0; i < asides.length; i++) {
      if (asides[i].querySelector('a[href="/admin"], a[href$="/admin"]')) return asides[i];
    }
    return null;
  }

  /** 按文案精确找链接（去掉空白再比，防止换行缩进影响）。 */
  function linkByText(scope, text) {
    var links = scope.getElementsByTagName('a');
    for (var i = 0; i < links.length; i++) {
      var t = (links[i].textContent || '').replace(/\s+/g, '');
      if (t === text) return links[i];
    }
    return null;
  }

  function insertChangeEntry() {
    if (!document.body) return;
    if (document.getElementById('zq-change-domain')) return;

    var side = sidebarOf();
    if (!side) return; // 不是首页（后台 / 外站）就不插

    // 优先挂在「配套软件」下面，找不到就退回侧边栏里的「后台管理」下面
    var anchor = linkByText(side, '配套软件') ||
                 side.querySelector('a[href="/admin"], a[href$="/admin"]');
    if (!anchor || !anchor.parentNode) return;

    var link = document.createElement('a');
    link.id = 'zq-change-domain';
    link.href = 'javascript:void(0)';
    link.className = anchor.className;
    link.innerHTML = ICON + '更改域名';
    link.addEventListener('click', function (e) {
      if (e && e.preventDefault) e.preventDefault();
      send('change');
    }, false);

    anchor.parentNode.insertBefore(link, anchor.nextSibling);
  }

  /* ---------- 手机端长按 → 返回主页面 ---------- */
  var sheetOpen = false;

  function showHomeSheet() {
    // 已经开着就别再叠一层（之前点一次不消失就是这个原因）
    if (sheetOpen) return;
    sheetOpen = true;

    var host = document.createElement('div');
    host.style.cssText = 'all:initial;position:fixed;left:0;top:0;width:0;height:0;z-index:2147483647;';
    var shadow = host.attachShadow({ mode: 'open' });
    shadow.innerHTML =
      '<style>' +
      '.mask{position:fixed;left:0;top:0;right:0;bottom:0;background:rgba(16,24,40,.28);' +
      'display:flex;align-items:center;justify-content:center;animation:zqfade .14s ease-out}' +
      '@keyframes zqfade{from{opacity:0}to{opacity:1}}' +
      '.card{background:#fff;border-radius:18px;padding:8px;box-shadow:0 20px 48px rgba(16,24,40,.3);' +
      'font:16px/1.4 "PingFang SC","Microsoft YaHei UI",system-ui,sans-serif;animation:zqpop .16s ease-out}' +
      '@keyframes zqpop{from{transform:scale(.94);opacity:0}to{transform:none;opacity:1}}' +
      '.it{padding:15px 34px;border-radius:12px;color:#1F2937;white-space:nowrap;text-align:center}' +
      '.it:active{background:rgba(48,85,128,.1);color:#254267}' +
      '</style>' +
      '<div class="mask" id="mask"><div class="card">' +
      '<div class="it" id="reload">刷新页面</div>' +
      '<div class="it" id="home">返回主页面</div>' +
      '</div></div>';

    document.documentElement.appendChild(host);

    function close() {
      sheetOpen = false;
      if (host.parentNode) host.parentNode.removeChild(host);
    }

    shadow.getElementById('mask').addEventListener('click', function (e) {
      if (e.target && e.target.id === 'mask') close();
    });

    // click 和 touchend 都接，防止移动端 click 被吞
    function act(node, action) {
      var done = false;
      function go(e) {
        if (done) return;
        done = true;
        if (e && e.cancelable) e.preventDefault();
        if (e && e.stopPropagation) e.stopPropagation();
        close();
        send(action);
      }
      node.addEventListener('click', go);
      node.addEventListener('touchend', go);
    }
    act(shadow.getElementById('reload'), 'reload');
    act(shadow.getElementById('home'), 'home');
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
    insertChangeEntry();
    bindLongPress();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot, false);
  }
  boot();
  setTimeout(boot, 200);
  setTimeout(insertChangeEntry, 900);
  setTimeout(insertChangeEntry, 2000);
})();
