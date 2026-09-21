# Steam Login Lite

一个轻量、绿色免安装的 Windows Steam 账号管理与切换工具。

[下载最新版 Windows 绿色包](https://github.com/zyhandgod/steam_login/releases/latest)

## 功能

- WinUI 3 极简原生界面，账号表格右侧提供独立的登录、查询、编辑、删除按钮
- 账号密码方式切换 Steam，避免修改 `loginusers.vdf` 带来的长时间 Loading
- 批量导入仅账号密码的两段格式，以及包含邮箱信息的四段、五段格式；支持中文标签、`--`、`---`、`----` 分隔
- 查询 ID 为空时自动使用 Steam 账号名
- 在内置 PUBG.PLUS 弹窗中手动点击查询，自动保存等级和封禁状态
- 保存最后登录、最后查询时间及查询结果
- 首次打开自动识别本机 Steam 最近登录账号，并可在表格中复制账号和查询 ID
- 登录后自动关闭推荐/新闻窗口、关闭好友列表、打开游戏库
- 数据存储在当前 Windows 用户的固定目录，升级或更换解压目录后仍会保留；密码使用 Windows DPAPI 当前用户加密

## 运行要求

- Windows 10/11 x64
- Microsoft Edge WebView2 Runtime（Windows 10/11 通常随 Edge 安装）

下载 ZIP 后完整解压，双击 `SteamLoginLite.exe` 即可运行。程序已自带 .NET 8 与 Windows App SDK 运行组件，不需要安装本工具或另外安装 .NET。

如果未安装 WebView2 Runtime，程序会使用系统默认浏览器打开 PUBG.PLUS，并自动复制当前查询 ID。默认浏览器模式无法自动回填查询结果。

## 构建

在装有 Visual Studio 2022 或 .NET SDK 的 Windows 上执行：

```powershell
dotnet restore -r win-x64
dotnet publish SteamLoginLite.csproj -c Release -r win-x64 --self-contained true -o publish/win-x64
```

输出位于 `publish/win-x64`。复制整个目录到一个可写文件夹即可使用，无需安装。

## 数据说明

运行后会在 `%LOCALAPPDATA%\SteamLoginLite\accounts.json` 保存账号、状态和设置。首次运行新版时，如果固定目录还没有数据，程序会在旧版程序目录、桌面和下载目录中查找旧的 `data/accounts.json` 并自动迁移最近的一份。

删除程序文件夹不会删除账号数据；如需彻底清除，请另行删除 `%LOCALAPPDATA%\SteamLoginLite`。数据移动到另一台电脑后，受 DPAPI 保护的密码不能解密，需要重新导入。
