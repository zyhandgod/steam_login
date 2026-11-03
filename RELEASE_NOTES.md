# Steam Login Lite 2.1.3 · 中文本地版

本次将账号管理工具重构为 Electron + React + Ant Design 界面，提供完整解压即可运行的 Windows x64 ZIP 绿色包。

- 使用中文界面，保留软件名和账号、昵称等原始数据。
- 自带 Chromium，查询不再依赖 WebView2；自动填写查询标识，用户点击查询后回填封禁状态和生存等级。
- 增加跨分页全选筛选结果、取消选择、批量查询和删除，以及查询进度、重试和跳过。
- 修正 Steam 登录后偏好配置和处理时机，支持游戏库、好友列表及推荐/新闻窗口设置，显示实际处理状态。
- 沿用 Windows 用户目录保存账号、备注、查询结果和设置；密码继续使用 Windows DPAPI 加密，兼容旧版数据。
- 关闭窗口即退出，不驻留后台，不包含在线更新。后续手动解压新版 ZIP 使用即可。

## 使用方式

下载 `SteamLoginLite-2.1.3-win-x64.zip`，完整解压后双击 `SteamLoginLite.exe`。请保留整个解压文件夹，不要单独移动 EXE。

需要 Windows 10/11 x64 和 .NET Framework 4.8（通常系统已安装），无需安装 Node.js 或 WebView2。

升级前关闭旧工具，建议备份 `%LOCALAPPDATA%\SteamLoginLite`。在同一电脑和 Windows 用户下，新版会自动读取原数据；删除程序目录不会删除账号。换电脑或 Windows 用户后，加密密码需要重新填写。

Steam 登录与 PUBG.PLUS 查询仍需要网络，并可能需要手动完成登录验证或验证码。Windows 原生 Steam 窗口行为仍需实际使用验收。
