# 琪舟阁 QizhouPavilion

给 [琪舟阁导航站](https://nav.520jacky.dpdns.org)（Cloudflare Worker 单文件版）做的四平台原生外壳。
启动 → 输入域名 → 进站，域名只输一次。

**包名** `QizhouPavilion`　**显示名** 琪舟阁

## 下载

打开 [Releases](https://github.com/bayueqi/ZQ-QizhouPrivilion/releases/latest)，按平台拿：

| 平台 | 文件 | 说明 |
| --- | --- | --- |
| Android | `QizhouPavilion-android.apk` | 直接安装，minSdk 24 |
| Windows | `QizhouPavilion-windows-x64.zip` | 解压运行 `QizhouPavilion.exe`，需要 .NET 8 桌面运行时 |
| Linux | `QizhouPavilion-linux-x86_64.tar.gz` | 解压运行 `./qizhoupavilion`，需要 GTK3 + WebKitGTK 4.1 |
| macOS | `QizhouPavilion-macos.zip` | 解压得到 `.app`，**未公证**，首次要右键 →「打开」 |

首次运行都会让你填一次域名（例如 `nav.example.com`，不用打 `https://`）。
之后 Android 用返回键菜单，桌面端用 `Ctrl+L`（macOS 是 `Cmd+L`）可以随时换。

## 为什么小、为什么快

四个平台都是**系统自带 WebView 的外壳**，不捆绑 Chromium：

| 平台 | 用的内核 | 内核来自哪 |
| --- | --- | --- |
| Android | Android WebView | 系统组件 |
| Windows | WebView2 | 系统 Edge |
| Linux | WebKitGTK | 系统库 |
| macOS | WKWebView | 系统 Safari 内核 |

程序本体只有几十 KB 到几百 KB，内存大头由系统的内核进程承担，且和系统里其它程序共用同一份内核。

启动速度上还做了这些：

| 做法 | 效果 |
| --- | --- |
| `Application` 里预热 WebView 内核 | 省掉首次 `new WebView` 的 200ms 左右 |
| 启动时后台解析域名（DNS 预热） | WebView 发请求时直接命中系统 DNS 缓存 |
| 启动页与网页背景同色 `#FDF8F3` | 没有白屏闪烁 |
| `onPageCommitVisible` 才撤启动页 | 首帧出来就可见，不等图标全加载完 |
| 首屏 HTML 本地快照 | 第二次启动直接读本地，毫秒级；后台静默拉最新 |
| 页面不重建（`configChanges`） | 旋转屏幕不重新加载 |
| 切后台暂停页面定时器 | 不占 CPU、不白耗电 |

安装包体积上做了这些（v1.0.1）：

- 图标全部转 **WebP 无损**，比 PNG 小一半
- 开 **R8 混淆 + 资源压缩**，无三方库所以没有反射风险
- 不引任何 AndroidX / 三方依赖，dex 只有一个类包

## 浏览方式

点导航站里的书签，目标站直接在软件里打开，不跳系统浏览器。
Android 上按返回键逐页后退；退到导航站首页再按返回，弹出菜单（刷新 / 更换域名 / 退出）。

为了让各类站点都能在壳里正常用，放行了：

- `usesCleartextTraffic=true` + 混合内容 `ALWAYS_ALLOW` —— http 老站、https 页里的 http 资源都能加载
- 去掉 UA 里的 `; wv` 标记 —— 站点不会因为认出 WebView 就给降级页面
- 下载类链接交回系统 —— WebView 本身不下载文件，不接管会点了没反应
- `tel:` / `mailto:` / `intent:` 等协议仍交给系统

## 登录状态

会记住。Cookie 存在 App 私有目录，不卸载、不清数据就一直有效；切后台时会强制 `flush()` 落盘，
避免进程被系统回收时丢掉最后的登录态。

注意这是「登录态保持」，不是浏览器那种「记住密码自动填充」：
下次进来网站直接是已登录状态；但银行、支付类站点自己设的短时效或二次验证，壳子绕不过去，Chrome 里也一样。

## 想固定域名（可选）

Android 想做到「装完就是固定站点、连输入页都不出现」，把默认地址写进去：

```gradle
buildConfigField "String", "DEFAULT_HOME", "\"https://nav.example.com\""
```

桌面三平台首次运行填一次即可，配置分别存在：

- Windows `%APPDATA%\QizhouPavilion\home.txt`
- Linux `~/.config/QizhouPavilion/home.txt`
- macOS `~/Library/Application Support/QizhouPavilion/home.txt`

## 目录结构

```
app/                     Android 外壳（纯 Java，零三方依赖）
desktop/windows/         Windows 外壳（C# / WinForms / WebView2）
desktop/linux/           Linux 外壳（C / GTK3 / WebKitGTK）
desktop/macos/           macOS 外壳（Swift / AppKit / WKWebView）
tools/make_icons.py      从站点 favicon 生成四平台图标
.github/workflows/       四平台构建 + 自动发布 Release
```

图标取自站点自身的 favicon（`_worker.js` 里的 `406956.png`）。换图标后重新生成全部资源：

```bash
# 替换 tools/site-icon.png，然后
python tools/make_icons.py
```

## 签名说明

Android 的 `release` 复用 debug 签名，CI 用 cache 固定 `~/.android/debug.keystore`，
好处是每次构建签名一致、可以直接覆盖安装；代价是这个 key 不是私有的 —— 自用导航站，够。
macOS 是 ad-hoc 签名（`codesign --sign -`），没有开发者证书，所以首次打开要右键 →「打开」。
