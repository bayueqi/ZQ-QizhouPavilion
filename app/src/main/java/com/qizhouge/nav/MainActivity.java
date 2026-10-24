package com.qizhouge.nav;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.Context;
import android.content.DialogInterface;
import android.content.Intent;
import android.content.SharedPreferences;
import android.graphics.drawable.ColorDrawable;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.text.TextUtils;
import android.view.KeyEvent;
import android.view.View;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputMethodManager;
import android.webkit.CookieManager;
import android.webkit.DownloadListener;
import android.webkit.WebChromeClient;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.EditText;
import android.widget.TextView;

import java.io.ByteArrayInputStream;

public class MainActivity extends Activity {

    private static final String PREFS = "zqnav";
    private static final String KEY_HOME = "home_url";
    private static final int BG_COLOR = 0xFFFDF8F3;

    private WebView web;
    private View splash;
    private View loadingBox;
    private View errorBox;
    private View setup;
    private EditText urlInput;
    private TextView setupMsg;

    private HtmlCache cache;
    private String homeUrl;
    private boolean pageShown;
    private volatile boolean bypassCache;

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
        setup = findViewById(R.id.setup);
        urlInput = (EditText) findViewById(R.id.urlInput);
        setupMsg = (TextView) findViewById(R.id.setupMsg);

        initWebView();
        bindButtons();

        SharedPreferences sp = getSharedPreferences(PREFS, MODE_PRIVATE);
        String saved = sp.getString(KEY_HOME, null);
        if (TextUtils.isEmpty(saved)) saved = BuildConfig.DEFAULT_HOME;
        homeUrl = TextUtils.isEmpty(saved) ? null : saved;

        if (homeUrl == null) {
            showSetupPage();
        } else {
            loadHome(false);
        }
    }

    private void bindButtons() {
        findViewById(R.id.retryBtn).setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                loadHome(true);
            }
        });
        findViewById(R.id.changeBtn).setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                showSetupPage();
            }
        });
        findViewById(R.id.goBtn).setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                onGoClick();
            }
        });
        urlInput.setOnEditorActionListener(new TextView.OnEditorActionListener() {
            @Override
            public boolean onEditorAction(TextView v, int actionId, KeyEvent event) {
                if (actionId == EditorInfo.IME_ACTION_GO || actionId == EditorInfo.IME_ACTION_DONE) {
                    onGoClick();
                    return true;
                }
                return false;
            }
        });
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
        pageShown = false;
        bypassCache = fresh;
        setup.setVisibility(View.GONE);
        errorBox.setVisibility(View.GONE);
        loadingBox.setVisibility(View.VISIBLE);
        splash.setVisibility(View.VISIBLE);
        web.setVisibility(View.INVISIBLE);
        web.loadUrl(homeUrl);
        if (fresh) cache.refreshAsync(homeUrl);
    }

    private void revealPage() {
        if (web == null || pageShown) return;
        pageShown = true;
        web.setVisibility(View.VISIBLE);
        splash.setVisibility(View.GONE);
    }

    private void showLoadError() {
        if (pageShown) return;
        loadingBox.setVisibility(View.GONE);
        errorBox.setVisibility(View.VISIBLE);
        splash.setVisibility(View.VISIBLE);
        web.setVisibility(View.INVISIBLE);
    }

    private void showSetupPage() {
        setup.setVisibility(View.VISIBLE);
        splash.setVisibility(View.GONE);
        web.setVisibility(View.INVISIBLE);
        errorBox.setVisibility(View.GONE);
        loadingBox.setVisibility(View.VISIBLE);
        setupMsg.setVisibility(View.GONE);
        urlInput.setText(homeUrl == null ? "" : homeUrl);
        if (homeUrl != null) {
            urlInput.setSelection(urlInput.getText().length());
        }
        urlInput.requestFocus();
    }

    private void onGoClick() {
        String normalized = normalize(urlInput.getText().toString());
        if (normalized == null) {
            setupMsg.setText(R.string.setup_invalid);
            setupMsg.setVisibility(View.VISIBLE);
            return;
        }
        setupMsg.setVisibility(View.GONE);
        homeUrl = normalized;
        getSharedPreferences(PREFS, MODE_PRIVATE).edit().putString(KEY_HOME, normalized).apply();
        hideKeyboard();
        loadHome(false);
    }

    private void hideKeyboard() {
        try {
            InputMethodManager imm =
                    (InputMethodManager) getSystemService(Context.INPUT_METHOD_SERVICE);
            if (imm != null) {
                imm.hideSoftInputFromWindow(urlInput.getWindowToken(), 0);
            }
        } catch (Exception ignored) {
        }
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
            if (homeUrl == null) return null;
            Uri u = request.getUrl();
            if (u == null || !sameHomeUrl(u.toString())) return null;
            if (bypassCache) {
                bypassCache = false;
                return null;
            }
            byte[] data = cache.read();
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
            if (homeUrl != null) cache.refreshAsync(homeUrl);
        }

        @Override
        public void onReceivedError(WebView view, WebResourceRequest request,
                                    WebResourceError error) {
            if (request != null && request.isForMainFrame()) {
                showLoadError();
            }
        }
    }

    private class AppChrome extends WebChromeClient {
        @Override
        public void onProgressChanged(WebView view, int newProgress) {
            if (newProgress >= 100) revealPage();
        }
    }

    // ---------- 返回键 ----------

    @Override
    public void onBackPressed() {
        if (setup.getVisibility() == View.VISIBLE) {
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
        new AlertDialog.Builder(this)
                .setTitle(R.string.app_name)
                .setItems(new CharSequence[]{
                        getString(R.string.menu_refresh),
                        getString(R.string.menu_change),
                        getString(R.string.menu_exit)
                }, new DialogInterface.OnClickListener() {
                    @Override
                    public void onClick(DialogInterface dialog, int which) {
                        if (which == 0) {
                            loadHome(true);
                        } else if (which == 1) {
                            showSetupPage();
                        } else {
                            finish();
                        }
                    }
                })
                .setNegativeButton(R.string.cancel, null)
                .show();
    }

    // ---------- 工具 ----------

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
}
