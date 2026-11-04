# Steam Login Lite 2.1.8 · 中文本地版

- 恢复本机 Steam 账号自动同步，启动或刷新时识别，已有备注、昵称、密码和查询数据保持不变。
- 头像增加“本机”标记，实际在线账号显示绿色“当前”标记；不再用最近登录记录代替在线状态。
- 支持优先使用 Steam 已记住的本机登录状态，状态失效仍需在 Steam 中验证；没有可用状态时可补充密码。
- 保留备注独立列、紧凑列表和纯本地数据存储，不包含在线更新，不提取 Steam 登录凭证。
- 本机记录解析、选择、备份、合并及界面经过自动验证；真实 Steam 登录效果仍需在 Windows 使用时验证。

## 使用方式

下载 `SteamLoginLite-2.1.8-win-x64.zip`，完整解压后双击 `SteamLoginLite.exe`。请保留整个解压文件夹，不要单独移动 EXE。

需要 Windows 10/11 x64 和 .NET Framework 4.8（通常系统已安装），无需安装 Node.js 或 WebView2。

升级前关闭旧工具，建议备份 `%LOCALAPPDATA%\SteamLoginLite`。在同一电脑和 Windows 用户下，新版会自动读取原数据；删除程序目录不会删除账号。换电脑或 Windows 用户后，加密密码需要重新填写。

Steam 登录与 PUBG.PLUS 查询仍需要网络，并可能需要手动完成登录验证或验证码。Windows 原生 Steam 窗口行为仍需实际使用验收。
