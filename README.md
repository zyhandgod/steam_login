# Steam Login Lite

一个轻量、绿色免安装的 Windows Steam 账号管理与切换工具。

[下载最新版 Windows 绿色包](https://github.com/zyhandgod/steam_login/releases/latest)

## 功能

- 账号密码方式切换 Steam，避免修改 `loginusers.vdf` 带来的长时间 Loading
- 批量导入中文标签、`--`、`---`、`----` 等格式
- 查询 ID 为空时自动使用 Steam 账号名
- 在内置 PUBG.PLUS 弹窗中手动点击查询，自动保存等级和封禁状态
- 保存最后登录、最后查询时间及查询结果
- 登录后自动关闭推荐/新闻窗口、关闭好友列表、打开游戏库
- 数据存储在 EXE 同目录，密码使用 Windows DPAPI 当前用户加密

## 运行要求

- Windows 10/11 x64
- .NET Framework 4.8（Windows 10/11 通常已安装）
- Microsoft Edge WebView2 Runtime（Windows 10/11 通常随 Edge 安装）

如果未安装 WebView2 Runtime，程序会使用系统默认浏览器打开 PUBG.PLUS，并自动复制当前查询 ID。默认浏览器模式无法自动回填查询结果。

## 构建

在装有 Visual Studio 2022 或 .NET SDK 的 Windows 上执行：

```powershell
dotnet restore
dotnet build -c Release
```

输出位于 `bin/Release/net48`。复制整个目录到一个可写文件夹即可使用，无需安装。

## 数据说明

运行后会在程序同目录创建 `data/accounts.json`。删除整个文件夹即可卸载；移动到另一台电脑后，受 DPAPI 保护的密码不能解密，需要重新导入。
