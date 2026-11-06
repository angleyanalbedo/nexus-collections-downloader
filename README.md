# ASH · Nexus Collections Downloader

[English](README.en.md) | 简体中文

基于 WPF、WebView2 和 Vue 3 的 Windows Nexus Mods 下载客户端。使用自己的 Nexus 登录会话浏览游戏、选择 Mod 文件和合集指定版本，并交给后台下载队列管理。

## 功能

- 浏览和搜索游戏、Mod 与合集。
- 查看 Mod 介绍、文件列表和合集清单。
- 按合集指定文件 ID、版本和修订版选择文件，可排除整个 Mod 或单个文件。
- 从已登录的 WebView2 会话获取短时签名链接，由 C# 后台传输。
- 下载队列、进度、取消、重试及历史记录管理。
- 记住上次浏览页面和下载目录。
- 通过可选的本机 CDP 接口调试网页和前端。

## 下载与运行

从 [GitHub Releases](https://github.com/angleyanalbedo/nexus-collections-downloader/releases) 下载 Windows 压缩包，完整解压后运行 `ASH.exe`。

需要：

- Windows 10/11。
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)。
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。

发布包依赖系统运行时，不是单文件独立安装包。

## 使用

1. 启动 ASH，在“登录 / Nexus 账户”中登录自己的账户。
2. 选择游戏，浏览或搜索 Mod；也可直接粘贴 Nexus 页面链接。
3. 在 Mod 文件页选择文件，加入队列或立即下载。
4. 下载合集时进入“Mod 清单”，核对修订版和文件版本，取消不需要的条目，再下载所选文件。
5. 在下载管理中查看任务、选择保存目录和处理失败任务。

Nexus 的登录、账户权限、限速和网站验证仍然适用。遇到需要人工完成的验证，请打开内嵌网页处理。归档版本不等于禁止下载；实际可用性由 Nexus 返回结果决定。

## 开发

```powershell
cd frontend
npm ci
npm test
npm run build
cd ..
dotnet build desktop/Ash.Nexus.csproj -c Release -p:SkipFrontendBuild=true
```

技术栈：.NET 8 WPF、Microsoft WebView2、Vue 3、TypeScript、Vite、SQLite，以及 Downloader 下载库。

前端源代码位于 `frontend/`；`desktop/Assets/` 是构建产物。窗口代码按浏览器、消息桥接和下载职责分文件，仍使用 WPF partial class。

## CDP 调试

先退出已运行的 ASH，再运行 `start-debug.cmd`，或：

```powershell
.\desktop\bin\Release\net8.0-windows\ASH.exe --cdp-port 9222
```

- Nexus WebView2：`http://127.0.0.1:9222/json/list`
- ASH 前端：`http://127.0.0.1:9223/json/list`

外部 CDP 客户端可连接对应目标读取 DOM、执行脚本和监听网络。普通启动不启用调试端口。调试会话可以访问已登录页面，请只在本机使用，避免分享 Cookie、签名链接或未经脱敏的日志。

## 版本与已知限制

`v0.3.0` 保存了基本下载功能可用的基线。当前源码可能包含尚未进入 Release 的修复；请以 Release 说明为准。

- v0.3.0 已知存在取消获链后仍启动下载、删除记录被后台写回等竞态；源码正在修复，仍需回归验证。
- 当前源码增加了可保存的传输并发设置（默认 4，范围 1–16）。
- 暂停、恢复、重启后的断点续传和异常场景尚未完成全面验证。
- 文件名解析正在改进；旧版本下载的文件可能缺少正确扩展名。
- Nexus 页面及内部接口可能变化，导致解析或获取链接失败。
- 本工具管理下载文件，不负责自动安装 Mod、解决冲突或保证合集可运行。

## 数据

本地数据位于 `%LOCALAPPDATA%\ASH\Nexus`，包含任务数据库和 WebView2 浏览器 Profile。默认下载目录为 `%USERPROFILE%\Downloads\ASH`。不要将浏览器 Profile 上传到仓库或分享给其他人。

## 许可证

见 [LICENSE](LICENSE)。Mod 文件和 Nexus Mods 内容的权利归各自权利人所有。
