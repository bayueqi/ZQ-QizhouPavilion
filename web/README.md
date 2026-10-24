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

`index.html` 里搜 `v1.0.1`，两处：`<title>` 后面的角标、页脚的版本链接。发新版时一起改。

### 下载链接

用的是 GitHub 的 `latest` 直链，发新 Release 后自动指向最新版，**正常情况下不用改**：

```
https://github.com/bayueqi/ZQ-QizhouPavilion/releases/latest/download/QizhouPavilion-windows-x64.zip
```

前提是 Release 里的文件名保持一致。如果改了打包文件名，这里要同步改。

### 改配色

样式集中在 `<style>` 顶部的 `:root` 里：

```css
--bg: #fdf8f3;        /* 页面底色，和导航站同一个米色 */
--brand: #305580;     /* 主色，深蓝 */
--line: #e9e2d9;      /* 分隔线 */
```

### 改文案

- 主标题：`.hero h1`
- 副标题：`.hero p`
- 那句补充说明：`.hero .note`
- 特性六条：`<div class="feat">` 里成对出现的 `<div>`
- 使用步骤：`<ol class="steps">`

## 本地预览

直接双击 `index.html` 就能看，不需要起服务器。
