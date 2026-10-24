/*
 * 往导航站侧边栏插入「更改域名」入口，位置在「后台管理」下面。
 * 直接复用「后台管理」那个 <a> 的 class，所以样式、间距、hover 效果完全一致。
 * 不改网页源码，纯 App 侧注入。
 */
(function () {
  if (window.__zqInjected) return;
  window.__zqInjected = true;

  var ICON = '<svg xmlns="http://www.w3.org/2000/svg" class="h-5 w-5 mr-2" fill="none" ' +
    'viewBox="0 0 24 24" stroke="currentColor">' +
    '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" ' +
    'd="M8 7h12m0 0l-4-4m4 4l-4 4m0 6H4m0 0l4 4m-4-4l4-4" /></svg>';

  function sendChange() {
    var m = 'change';
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(m);
      return;
    }
    if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.app) {
      window.webkit.messageHandlers.app.postMessage(m);
      return;
    }
    location.href = 'zqnavgo:' + encodeURIComponent(m);
  }

  function insert() {
    if (!document.body) return;
    if (document.getElementById('zq-change-domain')) return;

    var admin = document.querySelector('a[href="/admin"]');
    if (!admin || !admin.parentNode) return;

    var link = document.createElement('a');
    link.id = 'zq-change-domain';
    link.href = 'javascript:void(0)';
    link.className = admin.className;
    link.innerHTML = ICON + '更改域名';
    link.addEventListener('click', function (e) {
      if (e && e.preventDefault) e.preventDefault();
      sendChange();
    }, false);

    admin.parentNode.insertBefore(link, admin.nextSibling);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', insert, false);
  }
  insert();
  setTimeout(insert, 200);
  setTimeout(insert, 800);
  setTimeout(insert, 2000);
})();
