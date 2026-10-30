package com.qizhouge.nav;

import android.annotation.SuppressLint;
import android.annotation.TargetApi;
import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.drawable.ColorDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.text.TextUtils;
import android.view.View;
import android.view.Window;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import android.webkit.CookieManager;
import android.webkit.DownloadListener;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.net.InetAddress;

public class MainActivity extends Activity {

    private static final String PREFS = "zqnav";
    private static final String KEY_HOME = "home_url";
    private static final int BG_COLOR = 0xFFFDF8F3;
    /** 欢迎页回传通道：页面里 location.href = "zqnavgo:<urlencode>" */
    private static final String SETUP_SCHEME = "zqnavgo";
    private static final String SETUP_BASE = "https://qizhouge.app/";

    private WebView web;
    private View splash;
    private View loadingBox;
    private View errorBox;
    private FrameLayout fullscreenBox;

    private View fullscreenView;
    private WebChromeClient.CustomViewCallback fullscreenCallback;

    private HtmlCache cache;
    private String homeUrl;
    private boolean pageShown;
    private boolean inSetup;
    /** 长按菜单切换的全屏（隐藏状态栏/导航栏），与网页视频全屏是两回事 */
    private boolean appFullscreen;
    private volatile boolean bypassCache;
    /** 主页面加载失败，此时挡住内核自带的报错页，显示 App 自己的提示 */
    private boolean loadFailed;
    private String failedUrl;

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        getWindow().setBackgroundDrawable(new ColorDrawable(BG_COLOR));
        setContentView(R.layout.activity_main);

        cache = new HtmlCache(getCacheDir());

        web = (WebView) findViewById(R.id.web);
        splash = findViewById(R.id.splash);
        loadingBox = findViewById(R.id.loadingBox);
        errorBox = findViewById(R.id.errorBox);
        fullscreenBox = (FrameLayout) findViewById(R.id.fullscreen);

        initWebView();

        findViewById(R.id.retryBtn).setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                retryFailed();
            }
        });
        findViewById(R.id.changeBtn).setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                showSetupPage();
            }
        });

        SharedPreferences sp = getSharedPreferences(PREFS, MODE_PRIVATE);
        String saved = sp.getString(KEY_HOME, null);
        if (TextUtils.isEmpty(saved)) saved = BuildConfig.DEFAULT_HOME;
        homeUrl = TextUtils.isEmpty(saved) ? null : saved;

        if (homeUrl == null) {
            showSetupPage();
        } else {
            warmUpDns(homeUrl);
            loadHome(false);
        }
    }

    @SuppressLint("SetJavaScriptEnabled")
    private void initWebView() {
        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setDatabaseEnabled(true);
        s.setSupportMultipleWindows(false);
        s.setJavaScriptCanOpenWindowsAutomatically(false);
        s.setUseWideViewPort(true);
        s.setLoadWithOverviewMode(true);
        s.setSupportZoom(false);
        s.setBuiltInZoomControls(false);
        s.setDisplayZoomControls(false);
        s.setTextZoom(100);
        s.setCacheMode(WebSettings.LOAD_DEFAULT);
        // 站内浏览会进到各式各样的站，https 页面里混用 http 资源要放行
        s.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);
        s.setMediaPlaybackRequiresUserGesture(true);
        // 去掉 UA 里的 "; wv" 标记，避免站点把 App 当成功能受限的 WebView 而改走精简页
        String ua = s.getUserAgentString();
        if (ua != null && ua.contains("; wv")) {
            s.setUserAgentString(ua.replace("; wv", ""));
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU) {
            s.setAlgorithmicDarkeningAllowed(false);
        }

        web.setBackgroundColor(BG_COLOR);
        web.setOverScrollMode(View.OVER_SCROLL_NEVER);
        web.setVerticalScrollBarEnabled(false);
        web.setHorizontalScrollBarEnabled(false);
        web.setWebViewClient(new AppClient());
        web.setWebChromeClient(new AppChrome());
        // WebView 本身不下载文件，把下载链接交回系统，避免点了没反应
        web.setDownloadListener(new DownloadListener() {
            @Override
            public void onDownloadStart(String url, String userAgent, String contentDisposition,
                                        String mimeType, long contentLength) {
                try {
                    startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
                } catch (Exception ignored) {
                }
            }
        });

        CookieManager cm = CookieManager.getInstance();
        cm.setAcceptCookie(true);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
            cm.setAcceptThirdPartyCookies(web, true);
        }
    }

    // ---------- 加载 ----------

    private void loadHome(boolean fresh) {
        if (homeUrl == null) {
            showSetupPage();
            return;
        }
        inSetup = false;
        pageShown = false;
        loadFailed = false;
        failedUrl = null;
        bypassCache = fresh;
        errorBox.setVisibility(View.GONE);
        loadingBox.setVisibility(View.VISIBLE);
        splash.setVisibility(View.VISIBLE);
        web.setVisibility(View.INVISIBLE);
        web.loadUrl(homeUrl);
        if (fresh) cache.refreshAsync(homeUrl);
    }

    /** 出错页的「重试」：重试刚才失败的那个地址，不是一律回首页。 */
    private void retryFailed() {
        String url = failedUrl != null ? failedUrl : homeUrl;
        if (url == null) {
            showSetupPage();
            return;
        }
        loadFailed = false;
        failedUrl = null;
        pageShown = false;
        bypassCache = sameHomeUrl(url);
        errorBox.setVisibility(View.GONE);
        loadingBox.setVisibility(View.VISIBLE);
        splash.setVisibility(View.VISIBLE);
        web.setVisibility(View.INVISIBLE);
        web.loadUrl(url);
    }

    /**
     * 欢迎页与桌面三端共用同一份 HTML（assets/setup.html），视觉完全一致。
     */
    private void showSetupPage() {
        String html = SetupPage.build(this, homeUrl);
        if (html == null) {
            if (homeUrl != null) loadHome(false);
            return;
        }
        inSetup = true;
        pageShown = false;
        loadFailed = false;
        failedUrl = null;
        bypassCache = false;
        errorBox.setVisibility(View.GONE);
        loadingBox.setVisibility(View.GONE);
        splash.setVisibility(View.GONE);
        web.setVisibility(View.VISIBLE);
        web.loadDataWithBaseURL(SETUP_BASE, html, "text/html", "utf-8", null);
    }

    private void revealPage() {
        // 加载失败时内核仍会回调 onPageFinished，别让它把自带的报错页翻出来
        if (web == null || pageShown || loadFailed) return;
        pageShown = true;
        web.setVisibility(View.VISIBLE);
        splash.setVisibility(View.GONE);
    }

    private void showLoadError(String url) {
        loadFailed = true;
        failedUrl = url;
        pageShown = false;
        loadingBox.setVisibility(View.GONE);
        errorBox.setVisibility(View.VISIBLE);
        splash.setVisibility(View.VISIBLE);
        web.setVisibility(View.INVISIBLE);
    }

    /**
     * 启动时先把域名解析掉，等 WebView 真发请求时直接命中系统 DNS 缓存，
     * 网络差的机器上一次解析就要一两百毫秒。
     */
    private void warmUpDns(final String url) {
        final String host = hostOf(url);
        if (host == null) return;
        new Thread(new Runnable() {
            @Override
            public void run() {
                try {
                    InetAddress.getByName(host);
                } catch (Throwable ignored) {
                }
            }
        }, "dns-warmup").start();
    }

    // ---------- 路由 ----------

    /**
     * 网页链接一律留在 App 内打开（导航站里点书签 = 在软件里浏览目标站）。
     * 只有非 http(s) 的协议（tel:/mailto:/intent: 等）才交给系统处理。
     */
    private boolean handleUri(Uri uri) {
        if (uri == null) return false;
        String scheme = uri.getScheme();
        if (scheme == null) return false;
        if (SETUP_SCHEME.equalsIgnoreCase(scheme)) {
            return handleSetupResult(uri);
        }
        // 交给 WebView 自己处理
        if ("javascript".equalsIgnoreCase(scheme) || "about".equalsIgnoreCase(scheme)
                || "data".equalsIgnoreCase(scheme) || "blob".equalsIgnoreCase(scheme)) {
            return false;
        }
        if ("http".equalsIgnoreCase(scheme) || "https".equalsIgnoreCase(scheme)) {
            return false;
        }
        try {
            startActivity(new Intent(Intent.ACTION_VIEW, uri));
        } catch (Exception ignored) {
        }
        return true;
    }

    private boolean handleSetupResult(Uri uri) {
        String raw = uri.getSchemeSpecificPart();
        if (raw == null) return true;
        String text;
        try {
            text = Uri.decode(raw);
        } catch (Exception e) {
            return true;
        }
        if ("cancel".equals(text)) {
            if (homeUrl != null) loadHome(false);
            return true;
        }
        // 长按屏幕弹出的功能菜单（与电脑端菜单同六项）
        if ("change".equals(text)) {
            showSetupPage();
            return true;
        }
        if ("reload".equals(text)) {
            // 刷新的是当前这一页，不能回首页
            if (web != null && !inSetup && web.getUrl() != null) {
                web.reload();
            }
            return true;
        }
        if ("fullscreen".equals(text)) {
            toggleAppFullscreen();
            return true;
        }
        if ("back".equals(text)) {
            if (web != null && web.canGoBack()) web.goBack();
            return true;
        }
        if ("forward".equals(text)) {
            if (web != null && web.canGoForward()) web.goForward();
            return true;
        }
        if ("home".equals(text)) {
            loadHome(true);
            return true;
        }
        String url = normalize(text);
        if (url == null) return true;
        homeUrl = url;
        // 同步落盘：随后进程被划掉也不会退回上一个域名
        getSharedPreferences(PREFS, MODE_PRIVATE).edit().putString(KEY_HOME, url).commit();
        // 旧站点的 HTML 快照不能留给新域名用
        cache.clear();
        if (web != null) web.clearHistory();
        warmUpDns(url);
        loadHome(true);
        return true;
    }

    // ---------- 内部类 ----------

    private class AppClient extends WebViewClient {

        @Override
        public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest request) {
            if (request == null || !request.isForMainFrame()) return false;
            return handleUri(request.getUrl());
        }

        @SuppressWarnings("deprecation")
        @Override
        public boolean shouldOverrideUrlLoading(WebView view, String url) {
            return handleUri(Uri.parse(url));
        }

        @Override
        public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest request) {
            if (request == null || !request.isForMainFrame()) return null;
            if (homeUrl == null || inSetup) return null;
            Uri u = request.getUrl();
            if (u == null || !sameHomeUrl(u.toString())) return null;
            if (bypassCache) {
                bypassCache = false;
                return null;
            }
            byte[] data = cache.read(homeUrl);
            if (data == null) return null;
            cache.refreshAsync(homeUrl);
            try {
                return new WebResourceResponse("text/html", "utf-8",
                        new ByteArrayInputStream(data));
            } catch (Exception e) {
                return null;
            }
        }

        @Override
        public void onPageCommitVisible(WebView view, String url) {
            revealPage();
        }

        @Override
        public void onPageFinished(WebView view, String url) {
            revealPage();
            if (homeUrl != null && !inSetup && !loadFailed) {
                cache.refreshAsync(homeUrl);
                injectScripts();
            }
        }

        @Override
        public void doUpdateVisitedHistory(WebView view, String url, boolean isReload) {
            if (!inSetup) syncHistory();
        }

        @Override
        public void onReceivedError(WebView view, WebResourceRequest request,
                                    WebResourceError error) {
            if (request != null && request.isForMainFrame() && !inSetup) {
                Uri u = request.getUrl();
                showLoadError(u == null ? null : u.toString());
            }
        }
    }

    private class AppChrome extends WebChromeClient {
        @Override
        public void onProgressChanged(WebView view, int newProgress) {
            if (newProgress >= 100) revealPage();
        }

        /** 网页视频点全屏时，内核会把播放 Surface 交给我们，塞进全屏容器。 */
        @Override
        public void onShowCustomView(View view, CustomViewCallback callback) {
            if (fullscreenView != null) {
                callback.onCustomViewHidden();
                return;
            }
            fullscreenView = view;
            fullscreenCallback = callback;
            if (fullscreenBox != null) {
                fullscreenBox.addView(view);
                fullscreenBox.setVisibility(View.VISIBLE);
            }
            if (web != null) web.setVisibility(View.GONE);
            enterImmersive();
        }

        @Override
        public void onHideCustomView() {
            exitFullscreen();
        }
    }

    private void exitFullscreen() {
        if (fullscreenView == null) return;
        if (fullscreenBox != null) {
            fullscreenBox.removeAllViews();
            fullscreenBox.setVisibility(View.GONE);
        }
        if (web != null) web.setVisibility(View.VISIBLE);
        if (fullscreenCallback != null) {
            try {
                fullscreenCallback.onCustomViewHidden();
            } catch (Exception ignored) {
            }
            fullscreenCallback = null;
        }
        fullscreenView = null;
        exitImmersive();
    }

    @SuppressWarnings("deprecation")
    private void enterImmersive() {
        try {
            Window win = getWindow();
            // 刘海屏：不声明画进凹口，全屏时状态栏那一条会被系统涂成黑边
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                WindowManager.LayoutParams lp = win.getAttributes();
                lp.layoutInDisplayCutoutMode =
                        WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_SHORT_EDGES;
                win.setAttributes(lp);
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                Bars.hide(win);
            } else {
                win.getDecorView().setSystemUiVisibility(
                        View.SYSTEM_UI_FLAG_LAYOUT_STABLE
                                | View.SYSTEM_UI_FLAG_LAYOUT_HIDE_NAVIGATION
                                | View.SYSTEM_UI_FLAG_LAYOUT_FULLSCREEN
                                | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION
                                | View.SYSTEM_UI_FLAG_FULLSCREEN
                                | View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY);
            }
        } catch (Throwable ignored) {
        }
    }

    @SuppressWarnings("deprecation")
    private void exitImmersive() {
        try {
            Window win = getWindow();
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
                WindowManager.LayoutParams lp = win.getAttributes();
                lp.layoutInDisplayCutoutMode =
                        WindowManager.LayoutParams.LAYOUT_IN_DISPLAY_CUTOUT_MODE_DEFAULT;
                win.setAttributes(lp);
            }
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                Bars.show(win);
            } else {
                win.getDecorView().setSystemUiVisibility(View.SYSTEM_UI_FLAG_VISIBLE);
            }
        } catch (Throwable ignored) {
        }
    }

    /** Android 11 起系统栏交给 WindowInsetsController；单独成类，旧系统不会碰到这些新 API。 */
    private static final class Bars {

        @TargetApi(Build.VERSION_CODES.R)
        static void hide(Window win) {
            win.setDecorFitsSystemWindows(false);
            WindowInsetsController c = win.getInsetsController();
            if (c == null) return;
            c.hide(WindowInsets.Type.systemBars());
            c.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
        }

        @TargetApi(Build.VERSION_CODES.R)
        static void show(Window win) {
            win.setDecorFitsSystemWindows(true);
            WindowInsetsController c = win.getInsetsController();
            if (c == null) return;
            c.show(WindowInsets.Type.systemBars());
            c.setSystemBarsBehavior(WindowInsetsController.BEHAVIOR_DEFAULT);
        }
    }

    /** 长按菜单里的「全屏」：隐藏状态栏和导航栏，再点一次还原。 */
    private void toggleAppFullscreen() {
        appFullscreen = !appFullscreen;
        if (appFullscreen) {
            enterImmersive();
        } else {
            exitImmersive();
        }
        syncFullscreen();
    }

    /** 把全屏状态告诉菜单，那一项据此显示「全屏」或「退出全屏」。 */
    private void syncFullscreen() {
        if (web == null) return;
        try {
            web.evaluateJavascript(fullscreenCall(), null);
        } catch (Exception ignored) {
        }
    }

    private String fullscreenCall() {
        return "window.__zqFullscreen&&window.__zqFullscreen(" + appFullscreen + ")";
    }

    // ---------- 返回键 ----------

    @Override
    public void onBackPressed() {
        // 视频全屏时，返回键先退出全屏
        if (fullscreenView != null) {
            exitFullscreen();
            return;
        }
        // 长按菜单切的全屏，返回键同样能退出，不然只剩菜单一条路
        if (appFullscreen) {
            appFullscreen = false;
            exitImmersive();
            syncFullscreen();
            return;
        }
        // 报错页：先退回上一个能用的页面
        if (loadFailed) {
            loadFailed = false;
            failedUrl = null;
            errorBox.setVisibility(View.GONE);
            loadingBox.setVisibility(View.GONE);
            splash.setVisibility(View.GONE);
            if (web != null && web.canGoBack()) {
                web.setVisibility(View.VISIBLE);
                web.goBack();
            } else {
                loadHome(false);
            }
            return;
        }
        if (inSetup) {
            if (homeUrl != null) {
                loadHome(false);
            } else {
                super.onBackPressed();
            }
            return;
        }
        if (web != null && web.canGoBack()) {
            web.goBack();
            return;
        }
        super.onBackPressed();
    }

    /** 注入侧边栏「更改域名」入口（脚本内部有幂等判断）。 */
    private void injectScripts() {
        if (web == null) return;
        String js = SetupPage.read(this, "inject.js");
        if (js == null) return;
        try {
            // 接着把历史状态和全屏状态塞进去，保证 __zqHistory / __zqFullscreen 已经存在
            web.evaluateJavascript(js + ";" + historyCall() + ";" + fullscreenCall(), null);
        } catch (Exception ignored) {
        }
    }

    /** 把「能否后退/前进」告诉长按菜单，菜单据此把不可用的项置灰。 */
    private void syncHistory() {
        if (web == null) return;
        try {
            web.evaluateJavascript(historyCall(), null);
        } catch (Exception ignored) {
        }
    }

    private String historyCall() {
        if (web == null) return "";
        return "window.__zqHistory&&window.__zqHistory("
                + web.canGoBack() + "," + web.canGoForward() + ")";
    }

    // ---------- 生命周期 ----------

    /**
     * Cookie 默认是内存缓冲、异步落盘的，进程被系统回收时最后写入的登录态可能丢失。
     * 切后台时强制刷盘，保证下次打开仍是登录状态。
     */
    @Override
    protected void onPause() {
        try {
            CookieManager.getInstance().flush();
        } catch (Exception ignored) {
        }
        if (web != null) {
            // 后台不跑页面里的定时器和动画，省电也省内存
            web.onPause();
        }
        super.onPause();
    }

    @Override
    protected void onResume() {
        super.onResume();
        if (web != null) {
            web.onResume();
        }
    }

    @Override
    public void onTrimMemory(int level) {
        super.onTrimMemory(level);
        if (level >= TRIM_MEMORY_RUNNING_LOW && web != null) {
            // 让内核回收 WebView 里可丢弃的位图缓存
            web.freeMemory();
        }
    }

    @Override
    protected void onDestroy() {
        if (web != null) {
            try {
                web.stopLoading();
                web.setWebChromeClient(null);
                web.setWebViewClient(null);
                web.destroy();
            } catch (Exception ignored) {
            }
            web = null;
        }
        super.onDestroy();
    }

    // ---------- 工具 ----------

    private static String hostOf(String url) {
        if (url == null) return null;
        try {
            return Uri.parse(url).getHost();
        } catch (Exception e) {
            return null;
        }
    }

    private static String stripSlash(String s) {
        if (s == null) return "";
        int end = s.length();
        while (end > 0 && s.charAt(end - 1) == '/') end--;
        return s.substring(0, end);
    }

    private boolean sameHomeUrl(String url) {
        return stripSlash(url).equalsIgnoreCase(stripSlash(homeUrl));
    }

    private static String normalize(String raw) {
        if (raw == null) return null;
        String s = raw.trim();
        if (s.length() == 0) return null;
        if (!s.startsWith("http://") && !s.startsWith("https://")) {
            s = "https://" + s;
        }
        int end = s.length();
        while (end > 8 && s.charAt(end - 1) == '/') end--;
        s = s.substring(0, end);
        Uri u = Uri.parse(s);
        String host = u.getHost();
        if (host == null || host.indexOf('.') <= 0) return null;
        return s;
    }

    /** 读取 assets 里的欢迎页，并把当前域名填进去。 */
    private static final class SetupPage {
        private static String template;

        static String build(Context ctx, String current) {
            if (template == null) template = read(ctx, "setup.html");
            if (template == null) return null;
            String safe = current == null
                    ? ""
                    : current.replace("\\", "\\\\").replace("\"", "\\\"");
            return template.replace("__CURRENT__", safe);
        }

        /** 读 assets 里的文本资源。 */
        static String read(Context ctx, String name) {
            InputStream in = null;
            try {
                in = ctx.getAssets().open(name);
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                byte[] buf = new byte[8192];
                int n;
                while ((n = in.read(buf)) != -1) out.write(buf, 0, n);
                return new String(out.toByteArray(), "UTF-8");
            } catch (Exception e) {
                return null;
            } finally {
                if (in != null) {
                    try { in.close(); } catch (Exception ignored) { }
                }
            }
        }
    }
}
