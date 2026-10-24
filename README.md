# 琪舟阁 QizhouPavilion

[ZQ-NAV](https://github.com/bayueqi/ZQ-NAV) 导航站的客户端外壳，四个平台：Android、Windows、Linux、macOS。

启动后填一次域名就进站，点书签在当前窗口里打开，不跳系统浏览器。四端都调用系统自带的 WebView，不带浏览器内核。

包名 `QizhouPavilion`，显示名「琪舟阁」。

## 下载

到 [Releases](https://github.com/bayueqi/ZQ-QizhouPavilion/releases/latest) 按平台取：

- **Android**：`QizhouPavilion-android.apk`，直接装，Android 7.0 起
- **Windows**：`QizhouPavilion-windows-x64.zip`，解压运行 `QizhouPavilion.exe`，Windows 10 1903 起，要 WebView2 运行时（Win11 自带）
- **Linux**：`QizhouPavilion-linux-x86_64.tar.gz`，解压运行 `./qizhoupavilion`，需要 gtk3 和 webkit2gtk-4.1
- **macOS**：`QizhouPavilion-macos.zip`，解压得到 `.app`。没有公证，第一次打开要右键 →「打开」

## 用法

客户端不携带站点数据，站点内容由使用者自行部署的 [ZQ-NAV](https://github.com/bayueqi/ZQ-NAV) 实例提供。使用前需先完成 ZQ-NAV 的部署，取得该实例的访问域名。

首次启动时填入这个域名（例如 `nav.example.com`，不需要自行补充 `https://`），之后启动直接进入站点。

菜单（四端相同）：刷新 / 全屏 / 更换域名 / 后退上一页 / 前进下一页 / 返回主页面。

- Windows：在网页里右键
- macOS：屏幕顶部的菜单栏
- Linux：右键，或标题栏右边的 ☰
- Android：长按屏幕

## 配置文件

- Windows：`QizhouPavilion.exe` 同目录的 `data\home.txt`
- Linux：`~/.config/QizhouPavilion/home.txt`
- macOS：`~/Library/Application Support/QizhouPavilion/home.txt`
- Android：应用私有目录（SharedPreferences）

Android 想固定域名、连输入页都不要，编译时改 `app/build.gradle`：

```gradle
buildConfigField "String", "DEFAULT_HOME", "\"https://nav.example.com\""
```

## 目录

```
app/                  Android 外壳（Java）
desktop/windows/      Windows 外壳（C# / WinForms / WebView2）
desktop/macos/        macOS 外壳（Swift / AppKit / WKWebView）
desktop/linux/        Linux 外壳（C / GTK3 / WebKitGTK）
tools/                图标、菜单、注入脚本的生成脚本
web/                  产品页，可以直接放 Cloudflare Pages
.github/workflows/    四平台构建 + 发 Release
```

`tools/inject.js`（手机长按菜单）和 `tools/menu.js`（Linux 右键菜单）要分发到各平台，改完重新生成：

```bash
python tools/make_inject_assets.py
python tools/make_menu_assets.py
```

## 签名

Android 的 release 复用 debug 签名，每次构建一致，可以直接覆盖安装。这个 key 不是私有的。

macOS 是 ad-hoc 签名，没有开发者证书，所以首次打开需要右键 →「打开」。
