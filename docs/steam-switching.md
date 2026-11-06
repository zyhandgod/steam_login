# Steam 账号切换

> 最后更新：2026-11-06 | 版本：3.0.3

本工具使用 Windows 原生 WinForms 和 Steam 已保存在本机的登录状态切号；不提取或导出 Steam token。账号资料、DPAPI 密码存储和查询功能沿用原实现。本文说明切号行为，界面与绿色包约束见 [原生迁移说明](native-migration.md)。

## 上游依据

参考 WattToolkit（原 Steam++）官方仓库 `BeyondDimension/SteamTools` 的 `develop` 提交 `11f35fcf36cda1d1e924dab2eccb5bf19a0006bf`，以及该提交固定的 `BeyondDimension/SteamClient` 子模块 `b2f0cd31e390253922c453865128fd283d376332`：

- [账号切换入口](https://github.com/BeyondDimension/SteamTools/blob/11f35fcf36cda1d1e924dab2eccb5bf19a0006bf/src/BD.WTTS.Client.Plugins.GameAccount/Services.Implementation/SteamPlatformSwitcher.cs#L14)：停止 Steam、设置目标用户、更新选择字段、重启。
- [本机记录读取](https://github.com/BeyondDimension/SteamClient/blob/b2f0cd31e390253922c453865128fd283d376332/src/BD.SteamClient/Services.Implementation/SteamServiceImpl.cs#L299)及[字段更新](https://github.com/BeyondDimension/SteamClient/blob/b2f0cd31e390253922c453865128fd283d376332/src/BD.SteamClient/Services.Implementation/SteamServiceImpl.cs#L545)。
- [Windows 注册表设置](https://github.com/BeyondDimension/SteamClient/blob/b2f0cd31e390253922c453865128fd283d376332/src/BD.SteamClient/Services.Implementation/SteamServiceImpl.Abstract.cs#L222)：两个用户选择键和全局记住密码开关。
- [账号选择器设置](https://github.com/BeyondDimension/SteamClient/blob/b2f0cd31e390253922c453865128fd283d376332/src/BD.SteamClient/Services.Implementation/SteamServiceImpl.cs#L602)。

`liushiyangFrsan/GitHub_WattToolkit` 是旧源码副本，其 `main` 提交 `0286ed1e611b33be560286346abc5b0de54efd6e` 日期为 2022-02-08；[旧切号入口](https://github.com/liushiyangFrsan/GitHub_WattToolkit/blob/0286ed1e611b33be560286346abc5b0de54efd6e/src/ST.Client/UI/ViewModels/Pages/SteamAccountPageViewModel.cs#L218)没有当前官方的完整新字段处理。本版以以上固定官方提交为依据。

## 读取与账号资格

从所选 `steam.exe` 同目录的 `config/loginusers.vdf` 读取 `users` 下的有效账号记录，按账号名匹配。新旧字段分别保留语义：

| 字段 | 读取规则 | 切换时写入 |
| --- | --- | --- |
| `AutoLogin` | 优先使用；缺失时回退 `AllowAutoLogin` | 目标为 `1`，其他有效账号为 `0` |
| `AllowAutoLogin` | 独立读取；缺失时回退所读的自动登录选择值 | 所有有效账号为 `1`，兼容旧版 |
| `MostRecent` | 支持旧字段及大小写；缺失时回退所读的自动登录选择值 | 目标为 `1`，其他有效账号为 `0` |
| `RememberPassword` | 为 `1` 时允许尝试本机已记住登录 | 保持每个账号原值 |

`AutoLogin=0` 或 `AllowAutoLogin=0` 不表示密码或凭据已经失效，不再作为账号资格的额外门槛。`RememberPassword=1` 也只表示可尝试，不能证明当前已登录或凭据仍有效。

主界面优先沿用已确认的目标会话；否则在符合资格时使用本机记录。没有符合条件的本机记录时沿用原密码后备路线，缺少密码则提示补充。等待验证超时后不会新增自动密码重试，Steam Guard 等验证仍在 Steam 窗口中完成。

## 切换流程与配置写入

1. 检查所选 Steam 路径和目标记录；目标会话已就绪时直接复用。
2. 沿用现有进程停止流程：`CloseMainWindow → 等待 900ms → 必要时 Kill → 等待退出`，之后再准备登录配置。它不是本版新增的 `steam.exe -shutdown` 流程。
3. 重新读取登录记录，选择目标账号，按上表更新选择字段。每账号 `RememberPassword`、`WantsOfflineMode`、`SkipOfflineModeWarning` 及其他值保持原样。
4. 若 `config/config.vdf` 已存在，检查以下已有路径并将 `AlwaysShowUserChooser` 设为 `0`：常见包装结构 `InstallConfigStore/WebStorage/Auth`，以及旧无包装结构 `WebStorage/Auth`。文件或父块缺失时跳过，不创建整棵配置树；已有父块内可以补充该键。格式错误时拒绝修改。
5. 写入 `HKEY_CURRENT_USER\Software\Valve\Steam`：字符串 `AutoLoginUser`、字符串 `AutoLoginUser_steamchina` 均为目标账号，DWORD `RememberPassword=1`。后者是全局开关，不改变上表中每账号的记住密码值，也不会恢复失效凭据。中国模式兼容键随官方实现写入，但本版不添加 `-steamchina` 参数或模式界面。
6. 无密码启动参数启动 `steam.exe`，随后等待目标真实活动会话。原密码登录路线保留，并修正 Windows 参数中引号和反斜杠的转义。

两个 VDF 先完成解码、解析和目标更新计算，再逐个写入。每个实际变化的文件使用临时文件原子替换、首次 `.steam-login-lite.bak` 备份、UTF-8 BOM 保留和原内容变化检查；保留注释、格式与未知值。这是逐文件保护，不是跨两个文件的事务：第二个文件提交失败时，第一个文件可能已更新，不能把“先验证”描述为两个文件同时提交。

## 保留的状态与界面行为

- PID、进程路径和 `ActiveUser` 共同确认目标实际会话；最近选择的账号、启动成功或 `MostRecent=1` 都不能代替确认。
- 底部使用原生 Marquee 进度条，显示 `stopping`、`preparing`、`starting`、`waiting` 等阶段文字。确认、失败、取消或超时后结束对应进度；`LastLoginAt` 仅由目标会话确认事件更新。
- 后台当前账号同步保持每 3 秒高亮，不激活窗口；游戏库每次登录最多发送一次打开请求。推荐、新闻和好友列表的后续偏好处理保持。
- 保留正在运行目标会话的复用、原进程退出检查、密码后备和 DPAPI。未引入 Steam SDK、其他界面框架、离线模式界面或 token 迁移。

与官方入口的主要差异是保留账号资格检查、真实会话确认和文件保护；不把重启请求本身当作登录成功。配置字段兼容不代表保证所有 Steam 版本均能免验证登录。

## 验证命令与边界

安装 .NET SDK 8。以下纯逻辑项目以 `net8.0` 运行，可在 macOS 或 Windows 检查字段兼容、精确路径、文件保护和查询状态逻辑：

```sh
dotnet run --project tests/SteamLocalAccounts/SteamLocalAccounts.csproj -c Release
dotnet run --project tests/SteamPreferences/SteamPreferences.csproj -c Release
dotnet run --project tests/QueryBatch/QueryBatch.csproj -c Release
```

Windows 上运行会话和原生界面回归，再构建绿色包：

```powershell
dotnet run --project tests/SteamSession/SteamSession.csproj -c Release
dotnet run --project tests/NativeSmoke/NativeSmoke.csproj -c Release -- --ui release/native-preview.png
dotnet run --project tests/ParserSmoke/ParserSmoke.csproj -c Release
dotnet build SteamLoginLite.csproj -c Release
./scripts/build.ps1
```

切号回归关注以下边界：

- 新旧字段缺失、值冲突、未选中但已记住的账号，以及重复或无效账号记录；只改变目标选择，不伪造每账号记住密码标志。
- 选择器两种根结构、缺文件或父块、其他位置同名键和坏格式；保留未知键、注释、BOM，拒绝覆盖已变化文件。
- 实际会话与旧 PID、错误路径、其他账号或最近记录的区别；未退出进程不能继续假报成功。
- 原生进度及确认、失败、超时；过期回调不能覆盖新任务，最近登录时间须等目标会话确认，后续偏好完成不能重复记录登录。
- Windows 引号和反斜杠参数转义、注册表字段，以及现有不抢焦点和游戏库一次打开的约束。

新增 Windows 回归位于 `tests/SteamSession` 和 `tests/NativeSmoke`，使用隔离夹具或状态注入验证边界。这些命令是验证入口，不构成 CI 或真实账号登录结果。真实本机登录、过期凭据、Steam Guard 及远程查询验证码仍需在 Windows 实机验收。

## 3.0.3 验证记录

2026-11-06，Windows [CI 42153762394](https://github.com/zyhandgod/steam_login/actions/runs/42153762394) 在源码 `a79f2f9e4ec4016e3200f55297c5ac20f8a2eabe` 上全部通过：账号与配置兼容、隔离注册表、Windows 参数解析、取消处理、原生进度/时间/迟到回调/重命名/焦点回归、查询夹具、安装器签名及绿色包检查。

构建产物 `SteamLoginLite-3.0.3-win-x64.zip` 共 6 个文件，ZIP 486,945 字节，解压 1,122,403 字节。下载后已复核 ZIP 完整性、文件清单及版本说明；SHA-256 为 `0fea3c7d22411905b476fa28fab42640c72c7d9265ec378c39b4f3a74fcc17bc`。原生界面截图已检查，账号资料不包含真实用户数据。

CI 没有进行真实 Steam 账号登录或 Steam Guard 验证；这些仍需 Windows 本机验收。
