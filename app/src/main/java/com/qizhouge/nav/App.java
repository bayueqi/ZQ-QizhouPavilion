package com.qizhouge.nav;

import android.app.Application;
import android.webkit.WebView;

/**
 * 冷启动预热 WebView 内核：首次 new WebView 要加载 libwebviewchromium.so，
 * 提前在 Application 里付掉这笔开销，首屏能省掉约 200ms 的空白期。
 */
public class App extends Application {

    @Override
    public void onCreate() {
        super.onCreate();
        try {
            WebView warm = new WebView(this);
            warm.destroy();
        } catch (Throwable ignored) {
            // 个别 ROM 上提前创建会失败，预热失败不影响正常启动
        }
    }
}
