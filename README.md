# Steam Login Lite 3.0.4 · 轻量原生版

> 最后更新：2026-11-07 | 版本：3.0.4

使用 Windows 原生 WinForms（.NET Framework 4.8），查询使用系统 WebView2。没有 Electron、React、Ant Design、Node.js、内置 Chromium、在线更新或托盘常驻。

## 使用

下载 `SteamLoginLite-3.0.4-win-x64.zip`，完整解压后双击 `SteamLoginLite.exe`。绿色包仅 6 个文件，需放在一起，无需安装本工具或 Node.js。

Windows 10/11 x64，需 .NET Framework 4.8（最新 Windows 通常已有）。首次查询如果没有 WebView2 Evergreen Runtime，工具会从 Microsoft 官方地址下载、验证安装器签名并安装，再继续当前查询；已有运行时时不重复安装。安装失败可点击查询窗口“重试”，或在偏好设置中检测/安装。WebView2 是单独安装的系统共享浏览器组件，占用系统磁盘，不放在绿色包中。安装可能需要网络及 Windows 授权。

## 保留的功能

- 账号添加、编辑、复制、备注、删除，以及批量导入预览；支持“账号-密码”及“账号-密码-游戏昵称”，分隔符为一个或多个连续的 `-`，可混用不同数量；兼容原有邮箱四段、五段格式。同名本机账号只补全缺失资料。
- 搜索账号、昵称、备注或邮箱，状态筛选，账号/等级/最近查询排序，20/50/100 条分页。
- 勾选单条、选择当前页、跨页全选筛选结果、取消选择、批量查询和删除。
- 备注独立显示在账号状态右侧，未设置备注则留空；生存等级同时显示阶位。
- 启动或刷新时同步本机 Steam 登录记录，按账号名合并，不覆盖已有密码、备注、游戏昵称和查询数据。“本机”表示有本机记录，“当前”只标识实际活动会话，每 3 秒同步并整行高亮。
- 优先尝试 Steam 已记住的登录状态，已是目标账号在线时沿用会话；没有符合条件的本机记录时使用工具保存的密码，缺少密码会提示补充。兼容新版 Steam 的自动登录选择，并通过已有设置关闭启动账号选择器。登录状态过期或 Steam Guard 仍需手动验证，不提取或导出 Steam 登录凭证。
- 登录时底部显示退出 Steam、准备账号、启动和等待验证的文字及进度动画；只有确认目标账号实际登录后才更新最近登录时间。
- Steam 登录后关闭推荐/新闻、关闭好友列表、打开游戏库，保留启动等待和查询间隔设置；游戏库每次登录只打开一次，后台状态同步不激活窗口。
- 查询自动填写/提交，验证码手动完成；可信 PUBG.PLUS API 返回后自动保存，批量继续、结束后关闭窗口。支持重试、跳过和结束。基本状态返回但 8 秒无等级时保留原等级。
- 查询任务按不可变账号 ID 和查询标识绑定；同昵称不同账号不去重，每次重试/下一条都隔离旧回调。
- 关闭主窗口即退出，停止未完成查询及 Steam 后续窗口操作，Steam 继续运行。

删除会真正移除工具中的账号、密码、备注和查询结果，并仅记下账号名以阻止本机同步重新加入。Steam 自己的记录不受影响。手动添加或批量导入同名账号会解除该名字的同步屏蔽；也可在偏好设置勾选“保存时恢复已删除的本机账号同步”。

## 数据与换版

账号数据始终在 `%LOCALAPPDATA%\SteamLoginLite\accounts.json`。3.0.4 直接读取 2.x 及原生旧版的原有数据；删除程序目录、重新解压新 ZIP 不删除账号。同一 Windows 用户下密码继续可用，换电脑或用户需要重新填写。密码仍由 Windows DPAPI 保护。数据仍通过临时文件原子保存并保留 `.bak`；首次运行的旧目录迁移仍保留。读取失败时禁止覆盖原数据。

换版前关闭旧程序，建议备份 `%LOCALAPPDATA%\SteamLoginLite`，再运行新目录内 EXE。WebView2 查询缓存位于该用户目录下的 `WebView2` 子目录，偏好设置可单独清理缓存，不删除账号。首次安装器临时下载在 `installer` 子目录，安装后清理。

## 开发、构建和验证

源码不再使用 npm。Windows 安装 .NET SDK 8 后：

```powershell
dotnet build SteamLoginLite.csproj -c Release
./scripts/build.ps1
```

正式包严格只包含 EXE、配置、三个 WebView2 接口/加载 DLL 和使用说明。构建脚本核对 6 个文件且解压大小不超过 5 MB；不打包浏览器、源码或缓存。

```powershell
dotnet run --project tests/ImportParser/ImportParser.csproj -c Release
dotnet run --project tests/QueryBatch/QueryBatch.csproj -c Release
dotnet run --project tests/SteamPreferences/SteamPreferences.csproj -c Release
dotnet run --project tests/SteamLocalAccounts/SteamLocalAccounts.csproj -c Release
dotnet run --project tests/SteamSession/SteamSession.csproj -c Release
dotnet run --project tests/ParserSmoke/ParserSmoke.csproj -c Release
dotnet run --project tests/NativeSmoke/NativeSmoke.csproj -c Release -- --ui release/native-preview.png
dotnet run --project tests/NativeSmoke/NativeSmoke.csproj -c Release -- --query
```

Windows CI 运行原生界面及真实 WebView2 夹具，不提交真实玩家查询、不读取真实账号。`--preview` 启动隔离示例界面；WinForms 正式运行及预览只支持 Windows，macOS 可编译检查和运行纯逻辑测试。旧 Electron 实现在历史标签 v2.1.8 中保留。

自动测试不能代替真实 Steam 登录、远程网站及验证码验收。迁移功能清单见 [原生迁移说明](docs/native-migration.md)，账号切换的配置兼容、上游依据和验证边界见 [Steam 切号实现](docs/steam-switching.md)。

### 3.0.4 批量导入修复

每行支持 `账号-密码` 或 `账号----密码----游戏昵称`，一个或多个连续的 `-` 均可分隔，同一行可以混用不同数量。第三段直接作为游戏昵称；省略或留空时使用账号名作为查询标识。原有 `账号-密码-邮箱-邮箱密码[-游戏昵称]` 及中文标签格式继续兼容。预览通过后确认导入，错误行会阻止确认。

在分隔格式中，连续的 `-` 都视为分隔符；字段本身含 `-` 时可使用中文标签格式，例如 `账号player密码pass-word游戏昵称nick-name`。

### 3.0.3 Steam 切号与进度提示

参考 WattToolkit 当前实现，补齐新版自动登录字段、记住密码开关和账号选择器处理，同时保留原有轻量界面、实际会话校验和密码登录后备。未被选中的已记住账号仍可切换；底部显示登录阶段与进度动画，最近登录时间仅在确认成功后记录。Steam 配置改动保留备份与原文件编码，并检查文件是否被其他进程改动。

### 3.0.2 交互修复

当前活动账号每 3 秒同步并整行高亮，选中后仍保留高亮。重复添加可确认后搜索定位已有记录，无需重复输入密码。登录后打开游戏库只发送一次；状态同步不激活或置顶窗口。
