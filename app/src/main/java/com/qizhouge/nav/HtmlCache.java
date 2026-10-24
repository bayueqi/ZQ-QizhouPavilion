package com.qizhouge.nav;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * 首屏 HTML 的本地快照：启动时先渲染上一次的页面（毫秒级），后台再静默拉取最新版本。
 */
final class HtmlCache {

    private static final long MAX_AGE = 5L * 24 * 60 * 60 * 1000;
    private static final long MIN_GAP = 60L * 1000;
    private static final int MAX_BYTES = 4 * 1024 * 1024;

    private final File file;
    private final ExecutorService pool = Executors.newSingleThreadExecutor();
    private volatile long lastFetch;

    HtmlCache(File cacheDir) {
        this.file = new File(cacheDir, "home_snapshot.html");
    }

    byte[] read() {
        try {
            if (!file.exists()) return null;
            if (System.currentTimeMillis() - file.lastModified() > MAX_AGE) return null;
            FileInputStream in = new FileInputStream(file);
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buf = new byte[8192];
            int n;
            while ((n = in.read(buf)) != -1) out.write(buf, 0, n);
            in.close();
            byte[] data = out.toByteArray();
            if (data.length < 256 || data.length > MAX_BYTES) return null;
            String head = new String(data, 0, 200, "UTF-8").toLowerCase();
            if (!head.contains("<html") && !head.contains("<!doctype")) return null;
            return data;
        } catch (Exception e) {
            return null;
        }
    }

    void refreshAsync(final String url) {
        long now = System.currentTimeMillis();
        if (now - lastFetch < MIN_GAP) return;
        lastFetch = now;
        pool.execute(new Runnable() {
            @Override
            public void run() {
                fetch(url);
            }
        });
    }

    private void fetch(String url) {
        HttpURLConnection conn = null;
        try {
            conn = (HttpURLConnection) new URL(url).openConnection();
            conn.setConnectTimeout(8000);
            conn.setReadTimeout(15000);
            conn.setInstanceFollowRedirects(true);
            conn.setRequestProperty("User-Agent",
                    "Mozilla/5.0 (Linux; Android 14) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Mobile Safari/537.36");
            if (conn.getResponseCode() != 200) return;

            InputStream in = conn.getInputStream();
            ByteArrayOutputStream out = new ByteArrayOutputStream();
            byte[] buf = new byte[8192];
            int n;
            while ((n = in.read(buf)) != -1) out.write(buf, 0, n);
            in.close();

            byte[] data = out.toByteArray();
            if (data.length < 256 || data.length > MAX_BYTES) return;

            File tmp = new File(file.getAbsolutePath() + ".tmp");
            FileOutputStream fos = new FileOutputStream(tmp);
            fos.write(data);
            fos.getFD().sync();
            fos.close();
            if (file.exists() && !file.delete()) return;
            tmp.renameTo(file);
        } catch (Exception ignored) {
        } finally {
            if (conn != null) conn.disconnect();
        }
    }
}
