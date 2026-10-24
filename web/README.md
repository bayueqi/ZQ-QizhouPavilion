# 琪舟阁官网页面

单文件静态页，无任何外部依赖，直接把整个 `web/` 目录丢给 Cloudflare Pages 就能用。

## 文件

| 文件 | 说明 |
| --- | --- |
| `index.html` | 页面本体，样式内联，约 8.5 KB |
| `icon.webp` | 软件图标，256×256 |
| `favicon.png` | 浏览器标签页图标，64×64 |
| `_headers` | Cloudflare Pages 的响应头配置 |

## 部署到 Cloudflare Pages

### 方式一：直接上传（不用连 GitHub）

1. Cloudflare 控制台 → **Workers & Pages** → **Create** → **Pages** → **Upload assets**
2. 项目名填 `qizhoupavilion`（决定访问域名 `qizhoupavilion.pages.dev`）
3. 把 `web` 文件夹里的**四个文件**一起拖进去 → **Deploy**
4. 之后改了页面，点 **Create new deployment** 重新上传即可

### 方式二：连 GitHub 自动构建

1. **Workers & Pages** → **Create** → **Pages** → **Connect to Git** → 选 `ZQ-QizhouPavilion`
2. 构建配置：
   - Framework preset：`None`
   - Build command：**留空**
   - Build output directory：`web`
3. **Save and Deploy**

之后每次推送到 `main`，页面会自动重新部署。

### 绑定自己的域名（可选）

Pages 项目 → **Custom domains** → 添加域名 → 按提示在 DNS 里加 CNAME 记录。

## 改内容

### 换版本号

发新版时改这几处：

- `index.html` 里搜 `v1.0.1`：顶部 hero 的版本行
- 下载表格上方的「当前版本 1.0.1」
- 「更新日志」区块里新增一条，旧的往下留
- 下载表格里的三个体积数字（KB）

### 下载链接

用的是 GitHub `latest` 直链，发新 Release 后自动指向最新版，**文件名不变就不用动**：

```
https://github.com/bayueqi/ZQ-QizhouPavilion/releases/latest/download/QizhouPavilion-windows-x64.zip
```

### 改配色

样式在 `<style>` 顶部的 `:root` 里：

```css
--ink: #17181a;      /* 标题文字 */
--body: #4a4d52;     /* 正文 */
--line: #e6e6e3;     /* 分隔线 */
--brand: #305580;    /* 主色，按钮和链接 */
```

### 改文案

| 内容 | 位置 |
| --- | --- |
| 大标题、副标题 | `.hero h1`、`.hero .lede` |
| 按钮下方的小字 | `.hero .facts` |
| 界面预览窗 | `.preview` 里的 `.win`（纯 CSS 画的，改侧边栏文字找 `.win-side`，改卡片找 `.win-card`） |
| 组成部分 / 功能 | `<div class="cols">` 里成对的 `<div class="item">` |
| 下载表格 | `table.plat` 的 `<tbody>`，每行一个平台 |
| 系统要求 | `<dl class="req">` 里成对的 `<dt>` + `<dd>` |
| 更新日志 | `<div class="log">` |
| 页脚三栏 | `<div class="foot-grid">` |

### 加一个平台

在 `table.plat` 的 `<tbody>` 里复制一个 `<tr>`，改平台名、最低要求、文件名、大小、下载链接即可。

## 本地预览

直接双击 `index.html` 就能看，不需要起服务器。
