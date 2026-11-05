# Steam Login Lite 3.0.0 · 轻量原生版

使用 Windows 原生 WinForms（.NET Framework 4.8），查询使用系统 WebView2。没有 Electron、React、Ant Design、Node.js、内置 Chromium、在线更新或托盘常驻。

## 使用

下载 `SteamLoginLite-3.0.0-win-x64.zip`，完整解压后双击 `SteamLoginLite.exe`。绿色包仅 6 个文件，需放在一起，无需安装本工具或 Node.js。

Windows 10/11 x64，需 .NET Framework 4.8（最新 Windows 通常已有）。首次查询如果没有 WebView2 Evergreen Runtime，工具会从 Microsoft 官方地址下载、验证安装器签名并安装，再继续当前查询；已有运行时时不重复安装。安装失败可点击查询窗口“重试”，或在偏好设置中检测/安装。WebView2 是单独安装的系统共享浏览器组件，占用系统磁盘，不放在绿色包中。安装可能需要网络及 Windows 授权。

## 保留的功能

- 账号添加、编辑、复制、备注、删除，以及批量导入预览；同名本机账号只补全缺失资料。
- 搜索账号、昵称、备注或邮箱，状态筛选，账号/等级/最近查询排序，20/50/100 条分页。
- 勾选单条、选择当前页、跨页全选筛选结果、取消选择、批量查询和删除。
- 备注独立显示在账号状态右侧，未设置备注则留空；生存等级同时显示阶位。
- 启动或刷新时同步本机 Steam 登录记录，按账号名合并，不覆盖已有密码、备注、游戏昵称和查询数据。“本机”表示有本机记录，“当前”只标识实际活动会话。外部切换后可刷新识别。
- 优先尝试 Steam 已记住的登录状态，已是目标账号在线时沿用会话；不可用时使用工具保存的密码，缺少密码会提示补充。登录状态过期或 Steam Guard 仍需手动验证，不提取或导出 Steam 登录凭证。
- Steam 登录后关闭推荐/新闻、关闭好友列表、打开游戏库，保留启动等待和查询间隔设置；处理状态显示在底部。
- 查询自动填写/提交，验证码手动完成；可信 PUBG.PLUS API 返回后自动保存，批量继续、结束后关闭窗口。支持重试、跳过和结束。基本状态返回但 8 秒无等级时保留原等级。
- 查询任务按不可变账号 ID 和查询标识绑定；同昵称不同账号不去重，每次重试/下一条都隔离旧回调。
- 关闭主窗口即退出，停止未完成查询及 Steam 后续窗口操作，Steam 继续运行。

删除工具中的本机账号不会删除 Steam 自己保存的记录，再次同步时可能重新出现。

## 数据与换版

账号数据始终在 `%LOCALAPPDATA%\SteamLoginLite\accounts.json`。3.0.0 直接读取 2.x 及原生旧版的原有数据；删除程序目录、重新解压新 ZIP 不删除账号。同一 Windows 用户下密码继续可用，换电脑或用户需要重新填写。数据仍通过临时文件原子保存并保留 `.bak`；首次运行的旧目录迁移仍保留。读取失败时禁止覆盖原数据。

换版前关闭旧程序，建议备份 `%LOCALAPPDATA%\SteamLoginLite`，再运行新目录内 EXE。WebView2 查询缓存位于该用户目录下的 `WebView2` 子目录，偏好设置可单独清理缓存，不删除账号。首次安装器临时下载在 `installer` 子目录，安装后清理。

## 开发、构建和验证

源码不再使用 npm。Windows 安装 .NET SDK 8 后：

```powershell
dotnet build SteamLoginLite.csproj -c Release
./scripts/build.ps1
```

3.0.0 ZIP 约 472 KiB，解压约 1.06 MiB（不包括单独安装的 WebView2 运行时及用户查询缓存）。

正式包严格只包含 EXE、配置、三个 WebView2 接口/加载 DLL 和使用说明。构建脚本核对 6 个文件且解压大小不超过 5 MB；不打包浏览器、源码或缓存。

```powershell
dotnet run --project tests/QueryBatch/QueryBatch.csproj -c Release
dotnet run --project tests/SteamPreferences/SteamPreferences.csproj -c Release
dotnet run --project tests/SteamLocalAccounts/SteamLocalAccounts.csproj -c Release
dotnet run --project tests/SteamSession/SteamSession.csproj -c Release
dotnet run --project tests/ParserSmoke/ParserSmoke.csproj -c Release
dotnet run --project tests/NativeSmoke/NativeSmoke.csproj -c Release -- --ui release/native-preview.png
dotnet run --project tests/NativeSmoke/NativeSmoke.csproj -c Release -- --query
```

Windows CI 运行原生界面及真实 WebView2 夹具，不提交真实玩家查询、不读取真实账号。`--preview` 启动隔离示例界面；WinForms 正式运行及预览只支持 Windows，macOS 可编译检查和运行纯逻辑测试。旧 Electron 实现在历史标签 v2.1.8 中保留。

自动测试不能代替真实 Steam 登录、远程网站及验证码验收。迁移功能清单见 [原生迁移说明](docs/native-migration.md)。
