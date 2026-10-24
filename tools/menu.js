/*
 * App 自绘菜单（四端共用）。用 Shadow DOM 隔离样式，避免被宿主页面污染。
 * 注入后暴露 window.__zqMenu.show(x,y) / showCenter()。
 * 点击后按平台回传 "menu:<动作>"：
 *   WebView2 -> chrome.webview.postMessage
 *   WKWebView / WebKitGTK -> webkit.messageHandlers.app
 *   Android -> location.href = "zqnavgo:<urlencode>"
 */
(function () {
  if (window.top !== window) return;
  if (window.__zqMenu) return;

  var desktop = !!(window.chrome && window.chrome.webview) ||
    !!(window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.app);

  var host = document.createElement('div');
  host.style.cssText =
    'all:initial;position:fixed;left:0;top:0;width:0;height:0;z-index:2147483647;';
  var root = host.attachShadow({ mode: 'open' });

  var rows = [
    ['domain', '更换导航站域名', 'Ctrl+L'],
    ['back', '后退', 'Alt+\u2190'],
    ['reload', '刷新', 'F5']
  ];

  var html = [
    '<style>',
    '.mask{position:fixed;left:0;top:0;right:0;bottom:0;display:none}',
    '.mask.on{display:block}',
    '.menu{position:fixed;min-width:198px;padding:6px;border-radius:14px;',
    'background:#fff;box-shadow:0 14px 38px rgba(16,24,40,.22),0 0 0 1px rgba(16,24,40,.07);',
    'font:14px/1.45 "PingFang SC","Microsoft YaHei UI","Segoe UI",system-ui,sans-serif;color:#1F2937;',
    '-webkit-user-select:none;user-select:none;animation:zqpop .12s ease-out}',
    '@keyframes zqpop{from{opacity:0;transform:scale(.97)}to{opacity:1;transform:none}}',
    '.it{display:flex;align-items:center;justify-content:space-between;gap:30px;',
    'padding:11px 13px;border-radius:9px;cursor:default}',
    '.it:hover,.it:active{background:rgba(48,85,128,.09);color:#254267}',
    '.kd{color:#ADA496;font-size:11.5px;letter-spacing:.2px}',
    '.sp{height:1px;margin:5px 9px;background:rgba(16,24,40,.08)}',
    '</style>',
    '<div class="mask" id="mask"><div class="menu" id="menu">'
  ];

  var i;
  for (i = 0; i < rows.length; i++) {
    html.push('<div class="it" data-a="' + rows[i][0] + '">' + rows[i][1] +
      '<span class="kd">' + (desktop ? rows[i][2] : '') + '</span></div>');
  }
  html.push('<div class="sp"></div>');
  html.push('<div class="it" data-a="quit">退出琪舟阁</div>');
  html.push('</div></div>');

  root.innerHTML = html.join('');

  document.documentElement.appendChild(host);

  var mask = root.getElementById('mask');
  var menu = root.getElementById('menu');
  var items = root.querySelectorAll('.it');

  function hide() {
    mask.className = 'mask';
  }

  function fire(action) {
    hide();
    var msg = 'menu:' + action;
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(msg);
      return;
    }
    if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.app) {
      window.webkit.messageHandlers.app.postMessage(msg);
      return;
    }
    location.href = 'zqnavgo:' + encodeURIComponent(msg);
  }

  for (i = 0; i < items.length; i++) {
    (function (el) {
      el.addEventListener('click', function () {
        fire(el.getAttribute('data-a'));
      });
    })(items[i]);
  }

  mask.addEventListener('mousedown', hide);
  mask.addEventListener('touchstart', hide);
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') hide();
  });
  window.addEventListener('resize', hide);
  window.addEventListener('blur', hide);

  function place(x, y) {
    mask.className = 'mask on';
    menu.style.left = '0px';
    menu.style.top = '0px';
    var w = menu.offsetWidth;
    var h = menu.offsetHeight;
    var left = Math.min(x, window.innerWidth - w - 8);
    var top = Math.min(y, window.innerHeight - h - 8);
    menu.style.left = Math.max(8, left) + 'px';
    menu.style.top = Math.max(8, top) + 'px';
  }

  window.__zqMenu = {
    show: place,
    showCenter: function () {
      mask.className = 'mask on';
      menu.style.left = '0px';
      menu.style.top = '0px';
      var w = menu.offsetWidth;
      var h = menu.offsetHeight;
      place(Math.max(8, (window.innerWidth - w) / 2), Math.max(8, (window.innerHeight - h) / 2));
    }
  };
})();
