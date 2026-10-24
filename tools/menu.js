/*
 * App 自绘右键菜单（Linux / WebKitGTK 用）。Shadow DOM 隔离样式，避免被宿主页面污染。
 * 注入后暴露 window.__zqMenu.show(x,y) / showCenter()。
 * 点击后按平台回传「动作名」：
 *   WebView2 -> chrome.webview.postMessage
 *   WKWebView / WebKitGTK -> webkit.messageHandlers.app
 *   Android -> location.href = "zqnavgo:<urlencode>"
 */
(function () {
  if (window.__zqMenu) return;

  var tries = 0;

  function install() {
    if (window.__zqMenu) return;
    if (window.top !== window) return;
    // 脚本可能在 document 还没根元素时就被执行（WebView2 的 document-created 阶段），
    // 这时 appendChild 会抛错，必须等根元素出现。
    if (!document.documentElement) {
      if (++tries > 120) return;
      setTimeout(install, 16);
      return;
    }
    try {
      build();
    } catch (e) {
      // 页面结构异常时放弃自绘菜单，不要影响页面本身
    }
  }

  function build() {
    var host = document.createElement('div');
    host.style.cssText =
      'all:initial;position:fixed;left:0;top:0;width:0;height:0;z-index:2147483647;';
    var root = host.attachShadow({ mode: 'open' });

    // 与手机端长按菜单、Windows 右键菜单保持完全一致
    var rows = [
      ['reload', '刷新'],
      ['fullscreen', '全屏'],
      ['change', '更换域名'],
      ['back', '后退上一页'],
      ['forward', '前进下一页'],
      ['home', '返回主页面']
    ];

    var html = [
      '<style>',
      '.mask{position:fixed;left:0;top:0;right:0;bottom:0;display:none}',
      '.mask.on{display:block}',
      '.menu{position:fixed;min-width:184px;padding:6px;border-radius:14px;',
      'background:#fff;box-shadow:0 14px 38px rgba(16,24,40,.22),0 0 0 1px rgba(16,24,40,.07);',
      'font:14px/1.45 "PingFang SC","Microsoft YaHei UI","Segoe UI",system-ui,sans-serif;color:#1F2937;',
      '-webkit-user-select:none;user-select:none;animation:zqpop .12s ease-out}',
      '@keyframes zqpop{from{opacity:0;transform:scale(.97)}to{opacity:1;transform:none}}',
      '.it{padding:11px 13px;border-radius:9px;cursor:default}',
      '.it:hover,.it:active{background:rgba(48,85,128,.09);color:#254267}',
      '</style>',
      '<div class="mask" id="mask"><div class="menu" id="menu">'
    ];

    var i;
    for (i = 0; i < rows.length; i++) {
      html.push('<div class="it" data-a="' + rows[i][0] + '">' + rows[i][1] + '</div>');
    }
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
      var msg = action;
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

    // 移动端 click 有时会被 WebView 吞掉，这里同时吃 touchend，并用标志去重
    function bindItem(el) {
      var fired = false;

      function handler(e) {
        if (fired) return;
        fired = true;
        setTimeout(function () { fired = false; }, 500);
        if (e) {
          if (e.cancelable) e.preventDefault();
          e.stopPropagation();
        }
        fire(el.getAttribute('data-a'));
      }

      el.addEventListener('click', handler);
      el.addEventListener('touchend', handler);
    }

    for (i = 0; i < items.length; i++) {
      bindItem(items[i]);
    }

    // 注意：判断点的是不是菜单本体。否则 touchstart/mousedown 会在 click 之前
    // 先把菜单隐藏，导致"菜单出来了但点不动"。
    function onMaskDown(e) {
      if (menu && menu.contains && menu.contains(e.target)) return;
      hide();
    }

    mask.addEventListener('mousedown', onMaskDown);
    mask.addEventListener('touchstart', onMaskDown);
    document.addEventListener('keydown', function (e) {
      if (e.key === 'Escape') hide();
    });
    window.addEventListener('resize', hide);

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
  }

  if (document.documentElement) {
    install();
  } else if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', install, { once: true });
    setTimeout(install, 32);
    setTimeout(install, 200);
  } else {
    install();
  }
})();
