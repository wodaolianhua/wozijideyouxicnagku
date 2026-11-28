package com.toolbox.app;

import android.annotation.SuppressLint;
import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.util.Log;
import android.view.View;
import android.view.ViewGroup;
import android.webkit.CookieManager;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.FrameLayout;
import android.widget.Toast;

import androidx.webkit.WebViewAssetLoader;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.zip.ZipEntry;
import java.util.zip.ZipInputStream;
/**
 * 原生 WebView 壳 —— 离线优先。
 *
 * 为什么用 WebViewAssetLoader 而不是 file://
 *   file:// 是不透明源，Android WebView 会禁掉 IndexedDB / localStorage，
 *   记账、打卡这些数据就存不下来。WebViewAssetLoader 提供一个固定的
 *   https://appassets.androidplatform.net 虚拟域名，是安全源，数据能存，
 *   而且更新后域名不变，**老数据不会丢**。
 *
 * 离线保证
 *   网页代码打包在 APK 的 assets/www 里。安装后不需要连任何服务器，
 *   服务器全挂了也不影响打开和使用。数据本来就只在本机。
 *
 * 更新机制
 *   设置页点「检查新版本」→ 从服务器拉 version.json → 有新版就下载 zip →
 *   解压到私有目录 → 重启 App 生效。更新失败继续用旧版，不影响使用。
 */
public class MainActivity extends Activity {

    private static final String TAG = "Toolbox";

    /** 网页源（assets/www），同时也是本地可写副本的来源 */
    private static final String ASSET_WWW = "www";
    private static final String LOCAL_WWW = "www";

    /**
     * 版本检查地址。
     * 从 assets/update-config.txt 读（每行一个 base url，第一个是主源，其余是备用），
     * 这样换托管平台只要改那个文本文件，不用重新编译 APK。
     * 读不到就用下面的默认值。
     *
     * 多镜像：主源不通自动试下一个，避免被单一平台卡住。
     */
    private static final String DEFAULT_UPDATE_BASE = "https://my-toolbox-41560.app.workbuddy.host";
    private static final String UPDATE_CONFIG = "update-config.txt";

    private WebView web;
    private final ExecutorService pool = Executors.newSingleThreadExecutor();
    private String updateBase = DEFAULT_UPDATE_BASE;
    private final List<String> mirrors = new ArrayList<>();
    private StringBuilder cookie = null;
    private String shareCode = null;

    /**
     * 读 assets/update-config.txt：
     *   第 1 行起：更新源地址（每行一个，第一个主源，后面备用）
     *   最后一行以 # 开头的是分享码，形如 #sharecode=XXXX
     */
    private void readUpdateBases() {
        mirrors.clear();
        shareCode = null;
        try (InputStream is = getAssets().open(UPDATE_CONFIG)) {
            java.io.ByteArrayOutputStream bo = new java.io.ByteArrayOutputStream();
            byte[] buf = new byte[2048];
            int n;
            while ((n = is.read(buf)) > 0) bo.write(buf, 0, n);
            String[] lines = bo.toString("UTF-8").split("\\r?\\n");
            for (String line : lines) {
                String s = line.trim();
                if (s.startsWith("#sharecode=")) {
                    shareCode = s.substring("#sharecode=".length()).trim();
                    continue;
                }
                if (s.startsWith("https://") && s.length() > 10 && !mirrors.contains(s)) {
                    mirrors.add(s);
                }
            }
        } catch (Exception ignored) {
        }
        if (mirrors.isEmpty()) mirrors.add(DEFAULT_UPDATE_BASE);
        updateBase = mirrors.get(0);
    }

    /**
     * 依次尝试所有镜像，返回第一个能拿到的 version.json 内容。
     * 全都不通就抛异常（调用方会告诉用户"检查失败，不影响使用"）。
     */
    private String fetchVersionJson() throws IOException {
        IOException last = null;
        for (String base : mirrors) {
            try {
                /* 有些平台第一次访问要带分享码才能建立会话，
                   这里先握手一次把 Cookie 拿下来，再正式取文件 */
                handshake(base);
                return httpGet(base + "/version.json", 8000);
            } catch (IOException e) {
                last = e;
                Log.w(TAG, "镜像不可用: " + base + " (" + e.getMessage() + ")，试下一个");
            }
        }
        throw last != null ? last : new IOException("没有可用的更新源");
    }

    /** 访问一次站点根路径，把会话 Cookie 拿到手 */
    private void handshake(String base) {
        try {
            HttpURLConnection c = open(base + "/", 6000);
            c.setReadTimeout(6000);
            int code = c.getResponseCode();
            if (code >= 400) {
                /* 根路径不通就带分享码再试 */
                String sc = shareCode;
                if (sc != null && !sc.isEmpty()) {
                    c.disconnect();
                    c = open(base + "/?sharecode=" + sc, 6000);
                    c.setReadTimeout(6000);
                    c.getResponseCode();
                }
            }
            c.disconnect();
        } catch (Exception ignored) {
        }
    }

    @SuppressLint("SetJavaScriptEnabled")
    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        FrameLayout root = new FrameLayout(this);
        root.setLayoutParams(new ViewGroup.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        root.setBackgroundColor(Color.parseColor("#111318"));

        web = new WebView(this);
        web.setBackgroundColor(Color.parseColor("#111318"));
        web.setOverScrollMode(View.OVER_SCROLL_NEVER);

        WebSettings s = web.getSettings();
        s.setJavaScriptEnabled(true);
        s.setDomStorageEnabled(true);
        s.setDatabaseEnabled(true);
        s.setAllowFileAccess(false);
        s.setAllowContentAccess(false);
        s.setUseWideViewPort(false);
        s.setLoadWithOverviewMode(false);
        s.setSupportZoom(false);
        s.setBuiltInZoomControls(false);
        s.setTextZoom(100);
        s.setMediaPlaybackRequiresUserGesture(false);
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            s.setSafeBrowsingEnabled(false);
        }
        CookieManager.getInstance().setAcceptCookie(true);

        WebViewAssetLoader loader = new WebViewAssetLoader.Builder()
                .setDomain("appassets.androidplatform.net")
                .addPathHandler("/app/", new LocalFirstPathHandler(this))
                .build();

        web.setWebViewClient(new WebViewClient() {
            @Override
            public WebResourceResponse shouldInterceptRequest(WebView view, WebResourceRequest req) {
                return loader.shouldInterceptRequest(req.getUrl());
            }

            @Override
            public boolean shouldOverrideUrlLoading(WebView view, WebResourceRequest req) {
                return handleUrl(req.getUrl().toString());
            }

            @Override
            public boolean shouldOverrideUrlLoading(WebView view, String url) {
                return handleUrl(url);
            }
        });

        // 网页 → 原生 的桥：更新检查、版本号
        web.addJavascriptInterface(new NativeBridge(), "ToolboxNative");

        root.addView(web, new FrameLayout.LayoutParams(
                ViewGroup.LayoutParams.MATCH_PARENT, ViewGroup.LayoutParams.MATCH_PARENT));
        setContentView(root);

        // 首次启动：把内置网页复制到私有目录，之后以私有目录为准（更新就写这里）
        pool.execute(() -> {
            try {
                readUpdateBases();
                ensureLocalCopy();
            } catch (Exception e) {
                Log.w(TAG, "复制内置网页失败，改用 assets 直读", e);
            }
        });

        web.postDelayed(() -> loadApp(), 120);
    }

    /** 站内链接留在 App 里，外链给系统浏览器 */
    private boolean handleUrl(String url) {
        if (url == null) return false;
        if (url.startsWith("https://appassets.androidplatform.net/app/")
                || url.startsWith("about:blank")) {
            return false;
        }
        if (url.startsWith("http://") || url.startsWith("https://")) {
            try {
                startActivity(new Intent(Intent.ACTION_VIEW, Uri.parse(url)));
            } catch (Exception ignored) {
            }
            return true;
        }
        return false;
    }

    // ---------------- 本地文件解析 ----------------

    private File localWwwDir() {
        File d = new File(getFilesDir(), LOCAL_WWW);
        if (!d.exists()) d.mkdirs();
        return d;
    }

    /** 优先读私有目录（更新后的版本），没有再读 APK 内置的 */
    private class LocalFirstPathHandler implements WebViewAssetLoader.PathHandler {
        LocalFirstPathHandler(Context ctx) {
        }

        @Override
        public WebResourceResponse handle(String path) {
            String rel = path.startsWith("/") ? path.substring(1) : path;
            File local = new File(localWwwDir(), rel);
            if (local.exists() && local.isFile()) {
                try {
                    InputStream is = new java.io.FileInputStream(local);
                    return new WebResourceResponse(mimeOf(rel), "utf-8", is);
                } catch (Exception ignored) {
                }
            }
            // 回退到 APK 内置
            try {
                InputStream is = getAssets().open(ASSET_WWW + "/" + rel);
                return new WebResourceResponse(mimeOf(rel), "utf-8", is);
            } catch (Exception e) {
                return null;
            }
        }

        private String mimeOf(String p) {
            String l = p.toLowerCase();
            if (l.endsWith(".html") || l.endsWith(".htm")) return "text/html";
            if (l.endsWith(".js")) return "text/javascript";
            if (l.endsWith(".css")) return "text/css";
            if (l.endsWith(".json") || l.endsWith(".webmanifest")) return "application/json";
            if (l.endsWith(".png")) return "image/png";
            if (l.endsWith(".jpg") || l.endsWith(".jpeg")) return "image/jpeg";
            if (l.endsWith(".svg")) return "image/svg+xml";
            if (l.endsWith(".ico")) return "image/x-icon";
            if (l.endsWith(".woff2")) return "font/woff2";
            return "application/octet-stream";
        }
    }

    private void ensureLocalCopy() throws IOException {
        File target = localWwwDir();
        File marker = new File(target, ".installed");
        if (marker.exists()) return;
        copyAssets(ASSET_WWW, target);
        try (FileOutputStream fos = new FileOutputStream(marker)) {
            fos.write(String.valueOf(System.currentTimeMillis()).getBytes());
        }
    }

    private void copyAssets(String assetPath, File outDir) throws IOException {
        String[] children = getAssets().list(assetPath);
        if (children == null) return;
        for (String c : children) {
            String ap = assetPath + "/" + c;
            File out = new File(outDir, c);
            try (InputStream is = getAssets().open(ap)) {
                if (c.indexOf('.') >= 0) {
                    try (OutputStream os = new FileOutputStream(out)) {
                        byte[] buf = new byte[16384];
                        int n;
                        while ((n = is.read(buf)) > 0) os.write(buf, 0, n);
                    }
                } else {
                    out.mkdirs();
                    copyAssets(ap, out);
                }
            }
        }
    }

    private void loadApp() {
        web.loadUrl("https://appassets.androidplatform.net/app/index.html");
    }

    // ---------------- 更新 ----------------

    /** 静默检查：不弹窗、不打扰 */
    private void checkUpdateQuietly() {
        pool.execute(() -> {
            try {
                String body = fetchVersionJson();
                String v = jsonString(body, "version");
                if (v != null && !v.isEmpty() && !v.equals(BuildConfig.VERSION_NAME)) {
                    Log.i(TAG, "有新版: " + v + " 当前: " + BuildConfig.VERSION_NAME);
                }
            } catch (Exception ignored) {
            }
        });
    }

    /** 真正执行更新：下载 zip → 解压 → 重启。失败则保留旧版。 */
    private void doUpdate(final String zipFile, final String version) {
        new Thread(() -> {
            File tmp = null;
            try {
                toast("正在下载 " + version + "…");
                tmp = new File(getCacheDir(), "update-" + version + ".zip");

                /* 依次试所有镜像下载 zip */
                IOException lastErr = null;
                boolean got = false;
                String file = zipFile;
                if (file.startsWith("http://") || file.startsWith("https://")) {
                    /* 绝对地址就直接用 */
                    try {
                        httpDownload(file, tmp);
                        got = true;
                    } catch (IOException e) {
                        lastErr = e;
                    }
                }
                for (String base : mirrors) {
                    if (got) break;
                    try {
                        httpDownload(base + "/" + file.replaceFirst("^/+", ""), tmp);
                        got = true;
                        break;
                    } catch (IOException e) {
                        lastErr = e;
                        Log.w(TAG, "下载失败: " + base + "，试下一个镜像");
                    }
                }
                if (!got) throw lastErr != null ? lastErr : new IOException("所有镜像都不可用");

                toast("正在安装…");

                File stage = new File(getCacheDir(), "stage");
                deleteTree(stage);
                stage.mkdirs();
                unzip(tmp, stage);

                // 校验 index.html 在不在，防止装出一个坏包
                if (!new File(stage, "index.html").exists()) {
                    throw new IOException("更新包不完整");
                }

                File current = localWwwDir();
                File backup = new File(getCacheDir(), "backup-www");
                deleteTree(backup);
                current.renameTo(backup);

                stage.renameTo(current);
                deleteTree(backup);
                deleteTree(tmp);

                runOnUiThread(() -> {
                    toast("更新好了，正在重启…");
                    new Thread(() -> {
                        try {
                            Thread.sleep(600);
                        } catch (InterruptedException ignored) {
                        }
                        Intent i = getPackageManager().getLaunchIntentForPackage(getPackageName());
                        i.addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_NEW_TASK);
                        startActivity(i);
                        finish();
                        Runtime.getRuntime().exit(0);
                    }).start();
                });
            } catch (Exception e) {
                Log.w(TAG, "更新失败", e);
                if (tmp != null) tmp.delete();
                runOnUiThread(() -> toast("更新失败，继续用现在的版本"));
            }
        }).start();
    }

    private void deleteTree(File f) {
        if (f == null || !f.exists()) return;
        File[] kids = f.listFiles();
        if (kids != null) for (File k : kids) deleteTree(k);
        f.delete();
    }

    private void unzip(File zip, File out) throws IOException {
        try (ZipInputStream zis = new ZipInputStream(new java.io.FileInputStream(zip))) {
            ZipEntry e;
            byte[] buf = new byte[16384];
            while ((e = zis.getNextEntry()) != null) {
                File target = new File(out, e.getName());
                // 防目录穿越
                if (!target.getCanonicalPath().startsWith(out.getCanonicalPath())) continue;
                if (e.isDirectory()) {
                    target.mkdirs();
                } else {
                    target.getParentFile().mkdirs();
                    try (OutputStream os = new FileOutputStream(target)) {
                        int n;
                        while ((n = zis.read(buf)) > 0) os.write(buf, 0, n);
                    }
                }
                zis.closeEntry();
            }
        }
    }

    /**
     * 读取一个 URL 的文本。
     * 自动维护 Cookie（有些托管平台第一次访问会下发会话 Cookie，
     * 后面带着它才能访问），并且遇到 403 会自动重试一次。
     */
    private String httpGet(String url, int timeout) throws IOException {
        HttpURLConnection c = open(url, timeout);
        try {
            int code = c.getResponseCode();
            if (code == 403) {
                /* 再来一次，这次带上已拿到的 Cookie */
                c.disconnect();
                c = open(url, timeout);
                code = c.getResponseCode();
            }
            if (code != 200) throw new IOException("HTTP " + code);
            try (InputStream is = c.getInputStream()) {
                java.io.ByteArrayOutputStream bo = new java.io.ByteArrayOutputStream();
                byte[] buf = new byte[8192];
                int n;
                while ((n = is.read(buf)) > 0) bo.write(buf, 0, n);
                return bo.toString("UTF-8");
            }
        } finally {
            c.disconnect();
        }
    }

    /** 打开连接，顺带收集 Cookie */
    private HttpURLConnection open(String url, int timeout) throws IOException {
        HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
        c.setConnectTimeout(timeout);
        c.setReadTimeout(timeout);
        c.setRequestProperty("Cache-Control", "no-cache");
        c.setRequestProperty("User-Agent", "Mozilla/5.0 (Linux; Android 12) Toolbox/1.0");
        if (cookie != null && cookie.length() > 0) {
            c.setRequestProperty("Cookie", cookie.toString());
        }
        c.connect();
        /* 收集 Set-Cookie */
        try {
            Map<String, List<String>> headers = c.getHeaderFields();
            if (headers != null) {
                for (Map.Entry<String, List<String>> e : headers.entrySet()) {
                    if (e.getKey() != null && e.getKey().equalsIgnoreCase("Set-Cookie")) {
                        for (String sc : e.getValue()) {
                            String pair = sc.split(";")[0];
                            if (pair.contains("=")) {
                                if (cookie == null) cookie = new StringBuilder();
                                else cookie.append("; ");
                                cookie.append(pair);
                            }
                        }
                    }
                }
            }
        } catch (Exception ignored) {
        }
        return c;
    }

    private void httpDownload(String url, File out) throws IOException {
        HttpURLConnection c = open(url, 10000);
        c.setReadTimeout(60000);
        c.setInstanceFollowRedirects(true);
        try {
            int code = c.getResponseCode();
            if (code == 403) {
                c.disconnect();
                c = open(url, 10000);
                c.setReadTimeout(60000);
                code = c.getResponseCode();
            }
            if (code != 200) throw new IOException("HTTP " + code);
            try (InputStream is = c.getInputStream(); OutputStream os = new FileOutputStream(out)) {
                byte[] buf = new byte[16384];
                int n;
                while ((n = is.read(buf)) > 0) os.write(buf, 0, n);
            }
        } finally {
            c.disconnect();
        }
    }

    /** 从 JSON 里取字符串字段（够用即可，不引三方库） */
    private String jsonString(String body, String key) {
        if (body == null) return null;
        String k = "\"" + key + "\"";
        int i = body.indexOf(k);
        if (i < 0) return null;
        int colon = body.indexOf(':', i + k.length());
        if (colon < 0) return null;
        int s = body.indexOf('"', colon);
        if (s < 0) return null;
        int e = body.indexOf('"', s + 1);
        if (e < 0) return null;
        return body.substring(s + 1, e);
    }

    private void toast(final String msg) {
        runOnUiThread(() -> Toast.makeText(this, msg, Toast.LENGTH_SHORT).show());
    }

    /** 给网页调用的接口：window.ToolboxNative.checkUpdate() */
    public class NativeBridge {
        @android.webkit.JavascriptInterface
        public void checkUpdate() {
            pool.execute(() -> {
                try {
                    final String body = fetchVersionJson();
                    final String v = jsonString(body, "version");
                    final String url = jsonString(body, "url");
                    if (v == null || url == null) {
                        runOnUiThread(() -> toast("没检查到版本信息"));
                        return;
                    }
                    if (v.equals(BuildConfig.VERSION_NAME)) {
                        runOnUiThread(() -> toast("已经是最新版了"));
                        return;
                    }
                    runOnUiThread(() -> new android.app.AlertDialog.Builder(MainActivity.this)
                            .setTitle("发现新版本 " + v)
                            .setMessage("要现在下载吗？下载完自动重启就生效了。")
                            .setPositiveButton("下载", (d, w) -> doUpdate(url, v))
                            .setNegativeButton("以后再说", null)
                            .show());
                } catch (Exception e) {
                    runOnUiThread(() -> toast("检查失败，不影响使用"));
                }
            });
        }

        @android.webkit.JavascriptInterface
        public String version() {
            return BuildConfig.VERSION_NAME;
        }

        @android.webkit.JavascriptInterface
        public void setUpdateBase(String base) {
            if (base != null && base.startsWith("https://")) updateBase = base.trim();
        }
    }

    @Override
    public void onBackPressed() {
        if (web != null && web.canGoBack()) web.goBack();
        else super.onBackPressed();
    }

    @Override
    protected void onDestroy() {
        if (web != null) {
            web.loadUrl("about:blank");
            web.destroy();
        }
        pool.shutdownNow();
        super.onDestroy();
    }
}
