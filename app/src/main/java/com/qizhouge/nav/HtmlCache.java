package com.qizhouge.nav;

import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.security.MessageDigest;
import java.util.Locale;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

/**
 * 首屏 HTML 的本地快照：启动时先渲染上一次的页面（毫秒级），后台再静默拉取最新版本。
 * 快照按域名分别存放，换过域名之后不会再把上一个站点的页面当成新站点渲染出来。
 */
final class HtmlCache {

    private static final long MAX_AGE = 5L * 24 * 60 * 60 * 1000;
    private static final long MIN_GAP = 60L * 1000;
    private static final int MAX_BYTES = 4 * 1024 * 1024;
    private static final String PREFIX = "home_";

    private final File dir;
    private final ExecutorService pool = Executors.newSingleThreadExecutor();
    private volatile String lastUrl;
    private volatile long lastFetch;

    HtmlCache(File cacheDir) {
        this.dir = cacheDir;
    }

    byte[] read(String url) {
        File file = fileFor(url);
        if (file == null) return null;
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
        if (url == null) return;
        long now = System.currentTimeMillis();
        // 换了地址必须立刻去拉，不能等 MIN_GAP 的间隔
        boolean sameUrl = url.equalsIgnoreCase(lastUrl);
        if (sameUrl && now - lastFetch < MIN_GAP) return;
        lastUrl = url;
        lastFetch = now;
        pool.execute(new Runnable() {
            @Override
            public void run() {
                fetch(url);
            }
        });
    }

    /** 换域名时把旧站点的快照全部清掉。 */
    void clear() {
        lastUrl = null;
        lastFetch = 0;
        try {
            File[] files = dir.listFiles();
            if (files == null) return;
            for (File f : files) {
                String name = f.getName();
                if (name.startsWith(PREFIX) && (name.endsWith(".html") || name.endsWith(".tmp"))) {
                    f.delete();
                }
            }
        } catch (Exception ignored) {
        }
    }

    private void fetch(String url) {
        HttpURLConnection conn = null;
        try {
            File file = fileFor(url);
            if (file == null) return;
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

    /** 每个地址一份文件，文件名取地址的摘要，互相不会串。 */
    private File fileFor(String url) {
        if (url == null || url.length() == 0) return null;
        return new File(dir, PREFIX + digest(url) + ".html");
    }

    private static String digest(String s) {
        try {
            MessageDigest md = MessageDigest.getInstance("SHA-1");
            byte[] b = md.digest(s.toLowerCase(Locale.US).getBytes("UTF-8"));
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < 8; i++) {
                String hex = Integer.toHexString(b[i] & 0xff);
                if (hex.length() == 1) sb.append('0');
                sb.append(hex);
            }
            return sb.toString();
        } catch (Exception e) {
            return Integer.toHexString(s.toLowerCase(Locale.US).hashCode());
        }
    }
}
