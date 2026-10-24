/*
 * 右键菜单：用 Shadow DOM 画一个 App 自己的菜单，替代 WebView2 那个原生英文菜单。
 * 注入到每个页面的 document 创建时执行，点击后通过 postMessage 回传给宿主。
 */
(function () {
  if (window.top !== window) return;
  if (window.__zqMenu) return;

  var host = document.createElement('div');
  host.style.cssText =
    'all:initial;position:fixed;left:0;top:0;width:0;height:0;z-index:2147483647;';
  var root = host.attachShadow({ mode: 'open' });

  root.innerHTML = [
    '<style>',
    '.mask{position:fixed;left:0;top:0;right:0;bottom:0;display:none}',
    '.mask.on{display:block}',
    '.menu{position:fixed;min-width:198px;padding:6px;border-radius:13px;',
    'background:#fff;box-shadow:0 14px 38px rgba(16,24,40,.20),0 0 0 1px rgba(16,24,40,.07);',
    'font:13.5px/1.45 "Microsoft YaHei UI","Segoe UI",system-ui,sans-serif;color:#1F2937;',
    '-webkit-user-select:none;user-select:none;animation:zqpop .12s ease-out}',
    '@keyframes zqpop{from{opacity:0;transform:scale(.97)}to{opacity:1;transform:none}}',
    '.it{display:flex;align-items:center;justify-content:space-between;gap:30px;',
    'padding:8px 11px;border-radius:9px;cursor:default}',
    '.it:hover{background:rgba(48,85,128,.09);color:#254267}',
    '.kd{color:#ADA496;font-size:11.5px;letter-spacing:.2px}',
    '.sp{height:1px;margin:5px 9px;background:rgba(16,24,40,.08)}',
    '</style>',
    '<div class="mask" id="mask"><div class="menu" id="menu">',
    '<div class="it" data-a="domain">更换导航站域名<span class="kd">Ctrl+L</span></div>',
    '<div class="it" data-a="back">后退<span class="kd">Alt+&#8592;</span></div>',
    '<div class="it" data-a="reload">刷新<span class="kd">F5</span></div>',
    '<div class="sp"></div>',
    '<div class="it" data-a="quit">退出琪舟阁</div>',
    '</div></div>'
  ].join('');

  document.documentElement.appendChild(host);

  var mask = root.getElementById('mask');
  var menu = root.getElementById('menu');
  var items = root.querySelectorAll('.it');
  var i;

  function hide() {
    mask.className = 'mask';
  }

  function fire(action) {
    hide();
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage('menu:' + action);
    }
  }

  for (i = 0; i < items.length; i++) {
    (function (el) {
      el.addEventListener('click', function () {
        fire(el.getAttribute('data-a'));
      });
    })(items[i]);
  }

  mask.addEventListener('mousedown', hide);
  document.addEventListener('keydown', function (e) {
    if (e.key === 'Escape') hide();
  });
  window.addEventListener('resize', hide);
  window.addEventListener('blur', hide);

  window.__zqMenu = {
    show: function (x, y) {
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
  };
})();
