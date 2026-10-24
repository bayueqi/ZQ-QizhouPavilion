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
   * 插错位置的两次教训：
   *   1. 全文档找 a[href*=qizhoupavilion]，命中后台表格里那条书签链接 →
   *      按钮被塞进表格单元格，变成没样式的裸链接。
   *   2. 改成「aside 里有指向 /admin 的链接就算首页侧边栏」后，后台页的站名
   *      「琪光掠影」和「概览」本身都指向 /admin，于是按钮被插到站名下面，
   *      跑到导航列表外面去了。
   * 结论：光看 href 认不出「导航项」。必须同时满足「当前是首页」+「锚点在一组链接里」。
   */

  var HOME_PATHS = { '/': 1, '/index.html': 1, '/index.htm': 1 };
  var ADMIN_TEXTS = ['后台管理', '管理后台', '配套软件'];

  /** 只在首页插；后台、分类页、外站一律不插。 */
  function isHomePage() {
    var p = (location.pathname || '/').replace(/\/+$/, '');
    return HOME_PATHS[p || '/'] === 1;
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

  /** 侧边栏里的「后台管理 / 配套软件」入口：文案优先，文案被改过才退回 href。 */
  function adminLinkIn(scope) {
    for (var i = 0; i < ADMIN_TEXTS.length; i++) {
      var byText = linkByText(scope, ADMIN_TEXTS[i]);
      if (byText) return byText;
    }
    var links = scope.getElementsByTagName('a');
    for (var j = 0; j < links.length; j++) {
      // 侧边栏里第一个链接是站名，它也可能指向 /admin，绝不拿它当锚点
      if (j === 0) continue;
      var href = (links[j].getAttribute('href') || '').split('?')[0].replace(/\/+$/, '');
      if (!/(^|\/)admin$/i.test(href)) continue;
      // 站名也可能指 /admin，而它通常一个人待在容器里：要求同容器里至少还有一个链接
      var parent = links[j].parentNode;
      if (parent && parent.getElementsByTagName('a').length >= 2) return links[j];
    }
    return null;
  }

  function insertChangeEntry() {
    if (!document.body) return;
    if (document.getElementById('zq-change-domain')) return;
    if (!isHomePage()) return;

    var asides = document.querySelectorAll('aside');
    for (var i = 0; i < asides.length; i++) {
      var anchor = adminLinkIn(asides[i]);
      if (!anchor || !anchor.parentNode) continue;

      var link = document.createElement('a');
      link.id = 'zq-change-domain';
      link.href = 'javascript:void(0)';
      link.className = anchor.className;
      link.innerHTML = ICON + '更改域名';
      link.addEventListener('click', function (e) {
        if (e && e.preventDefault) e.preventDefault();
        send('change');
      }, false);

      // 插在锚点后面，跟它同一组链接、同一套样式
      anchor.parentNode.insertBefore(link, anchor.nextSibling);
      return;
    }
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
