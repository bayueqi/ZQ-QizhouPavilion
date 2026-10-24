# 琪舟阁 App

给 [琪舟阁导航站](https://nav.520jacky.dpdns.org)（Cloudflare Worker 单文件版）套的原生 Android 外壳。
启动 → 输入域名 → 进站，域名只输一次。

## 装哪个

1. 打开仓库 **Actions** → 最新一次 `Build APK`
2. 底部 **Artifacts** → 下载 `QizhougeNav-apk`
3. 解压得到 `QizhougeNav.apk`，装到手机

> 首次运行是"输入域名"页，填你部署好的地址，例如 `nav.example.com`（不用打 `https://`），回车即进。
> 想换域名：在首页按返回键 → 菜单里选「更换域名」。

## 为什么快

| 做法 | 效果 |
| --- | --- |
| 启动页与网页背景同色（`#FDF8F3`） | 没有白屏闪烁 |
| `onPageCommitVisible` 而非 `onPageFinished` 才撤启动页 | 首帧一出来就可见，不等图标加载完 |
| 首屏 HTML 本地快照（`HtmlCache`） | 第二次启动直接读本地，秒开；后台静默拉最新 |
| WebView 默认磁盘缓存 + DOM Storage | Tailwind CDN 等走缓存，不重复下载 |
| 页面不重建（`configChanges`） | 旋转屏幕不重新加载 |
| 所有网页留在 App 内浏览 | 点书签直接在软件里打开目标站，返回键逐页后退 |

首屏 HTML 快照有 5 天有效期，且每次打开都会后台刷新；
想立刻拿到后台最新内容，用返回键菜单里的「刷新页面」。

## 浏览方式

点导航站里的书签，目标站直接在 App 内打开，不跳系统浏览器。
按返回键逐页后退，退到导航站首页再按返回，才弹出菜单（刷新 / 更换域名 / 退出）。

为让各类站点都能在壳里正常用，做了这些放行：

- `usesCleartextTraffic=true` + 混合内容 `ALWAYS_ALLOW` —— http 老站、https 页面里的 http 资源都能加载
- 去掉 UA 里的 `; wv` 标记 —— 站点不会因为识别到 WebView 就给我们降级页面
- 下载类链接交回系统 —— WebView 本身不下载文件，不接管会点了没反应
- `tel:` / `mailto:` / `intent:` 等协议仍交给系统

## 改默认域名（可选）

想让 App 装完就是固定站点、连输入页都不出现：

`app/build.gradle` 里

```gradle
buildConfigField "String", "DEFAULT_HOME", "\"https://nav.example.com\""
```

留空 `""` 则保持"首次输入"的行为。

## 图标

图标取自站点自身的 favicon（`_worker.js` 里的 `406956.png`）。
换了站点图标后重新生成各密度资源：

```bash
# 替换 tools/site-icon.png，然后
python tools/make_icons.py
```

## 签名说明

`release` 直接复用 debug 签名，CI 用 cache 固定 `~/.android/debug.keystore`。
好处是每次构建的签名一致、可以直接覆盖安装；
代价是这个 key 不是私有的 —— 自己用的导航站，够。要正式签名就自己生成 keystore 后改 `signingConfig`。

## 开发

纯 Java + 系统 framework，无任何三方依赖（连 AndroidX 都没引），Gradle 只需要 AGP 本身。

- `MainActivity` — WebView 外壳、域名设置页、返回键菜单、外链路由
- `HtmlCache` — 首屏 HTML 快照（stale-while-revalidate）

本地构建：`gradle :app:assembleRelease`（需 JDK 17 + Android SDK 34）。
