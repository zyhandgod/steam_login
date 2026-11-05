# Steam Login Lite 3.0.0 · 轻量原生版

改用 Windows 原生 WinForms，移除 Electron、React 和 Ant Design。绿色包只有 6 个文件，不携带 Chromium 或 Node 依赖。

- 保留现有账号数据、本机账号同步/标记、已记住状态登录、Steam 启动偏好。
- 保留备注独立列、搜索/筛选/排序、分页、跨页全选、批量导入/查询/删除。
- 查询使用系统 WebView2；缺少时首次自动下载并验证 Microsoft 官方安装器，安装一次后继续。
- 保留自动填入/提交、结果回填、批量继续并自动关闭、重试/跳过/结束；验证码仍需手动完成。
- 同昵称不同账号独立处理，旧请求/重试回调隔离，缺少等级时保留原等级。
- 程序、账号及查询缓存分开存储，关闭即退出，无在线更新。

Windows 10/11 x64，需 .NET Framework 4.8。完整解压后运行 SteamLoginLite.exe，保留全部 6 个文件。WebView2 为单独安装的共享组件，不随 ZIP 携带。原数据在 %LOCALAPPDATA%\SteamLoginLite 中，换版前建议备份。

自动测试覆盖原生界面和 WebView2 夹具；真实 Steam 登录及远程验证码仍需本机使用验证。
