# Steam Login Lite 2.1.3

使用 Electron + React + Ant Design 的 Windows Steam 账号管理与切换工具。ZIP 绿色版完整解压后双击 `SteamLoginLite.exe`，无需安装工具、Node.js 或 WebView2。请保留整个解压目录。

## 本地运行与手动换版

已移除在线更新、版本检查、发布后台及更新辅助程序。软件启动不请求更新服务器，不上传账号数据。`steam.mooizz.com` 的发布和下载服务已下线。

换版时关闭旧工具，解压新版 ZIP 后运行。程序文件与账号数据分开存储，同一电脑、同一 Windows 用户下，新版会自动读取原数据。删除旧程序文件夹不会删除账号。

账号数据位于：

```text
%LOCALAPPDATA%\SteamLoginLite\accounts.json
```

保存账号、DPAPI 加密密码、备注、查询结果及设置。兼容旧版本数据。首次运行仍会在旧版目录、桌面和下载目录查找旧 `data/accounts.json`，只在固定数据目录不存在时迁移，不覆盖已有数据。更换电脑或 Windows 用户后，密码需要重新填写；建议换版前备份数据目录。保存通过临时文件原子替换，保留 `.bak`；读取损坏数据时保留 `.broken-时间戳` 备份，不以空数据覆盖。

## 功能

- 账号添加、编辑、备注、搜索、状态筛选、等级排序与批量导入。
- 当前页选择、跨页全选筛选结果、一键取消选择、批量查询与删除。
- Steam 账号切换及登录后打开游戏库、关闭好友列表和推荐/新闻窗口。
- 自带 Chromium 查询 PUBG.PLUS，自动填写查询标识；用户点击查询并完成验证码，结果回填并保存。
- 批量查询支持进度、重试、跳过和结束。按账号记录及昵称绑定结果，避免串号。
- 仅封禁状态返回、等级在 8 秒内未返回时，保留原等级。
- 关闭主窗口即退出，无托盘常驻。未完成的查询及登录后窗口操作会停止，Steam 继续运行。

本地存储不代表所有功能可离线使用：Steam 登录和手动 PUBG.PLUS 查询仍需要网络，但不经过自己的更新服务器。

## 运行要求

Windows 10/11 x64、.NET Framework 4.8（通常系统已安装）。Electron 自带浏览器，不需要 WebView2。

macOS 仅提供界面预览，默认展示 28 条明确标记的示例账号，不读取或覆盖真实 Windows 账号。正式账号读写、登录切换与查询在 Windows 上使用。

登录切换会退出 Steam 和正在运行的 PUBG，请先保存游戏进度，按需完成 Steam Guard 验证。登录后处理校验 ActiveUser 会话，再处理初始配置刷新及窗口；主界面显示等待、完成或失败。新闻、好友和默认游戏库配置支持双向写入，并保留初始配置备份。实际窗口行为需在 Windows 验证。

## 开发与构建

进入本 README 所在、包含 `package.json` 的项目目录：

```sh
npm ci
npm run dev
```

Windows 开发需要 .NET SDK 8，并构建登录/加密辅助程序：

```powershell
npm run build:helper
npm run dev
```

构建完整绿色包：

```powershell
npm run pack:win
```

输出：`release/SteamLoginLite-2.1.3-win-x64.zip`。构建包不包含更新模块、发布后台或签名凭证。原 WinForms 源码仍保留供参考；`dotnet build SteamLoginLite.csproj` 构建的是旧界面。

## 验证

```sh
npm test
npm run test:query
npm run test:ui
npm run test:preferences
```

`test:query` 使用隔离的真实 Chromium 和本地 HTTPS 响应夹具验证昵称填入、结果合并及回填，不提交真实玩家查询。`test:ui` 在 Mac 预览模式验证账号表格、全选与取消、设置开关，并检查更新控件不存在。`test:preferences` 验证配置双向写入与备份。

Windows 上还可执行原有服务 smoke：

```powershell
dotnet run --project tests/ParserSmoke/ParserSmoke.csproj
```

本地管理界面与远程查询网页使用独立渲染进程。查询网页无 Node/本机 IPC 权限，启用沙箱和上下文隔离，拒绝下载与权限请求；仅从可信 API 主机解析匹配的查询响应。
