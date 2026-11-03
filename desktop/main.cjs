const { app, BrowserWindow, ipcMain, dialog } = require("electron");
const path = require("node:path");
const bridge = require("./bridge.cjs");
const version = require("../package.json").version;
const { QueryJob } = require("./query.cjs");
let main, job, data, steamStatus;
bridge.events.on("status", (state) => {
  steamStatus = state;
  send("app:steam-status", state);
});
const preview = process.platform !== "win32" || (!app.isPackaged && process.env.STEAM_PREVIEW === "1");
const development = !app.isPackaged && process.env.STEAM_DEV === "1";
const empty = () => ({
  Accounts: [],
  Settings: {
    SteamPath: "",
    StartupDelaySeconds: 8,
    CloseRecommendations: true,
    CloseFriendsList: true,
    OpenLibrary: true,
    QueryIntervalMilliseconds: 1200,
  },
  CurrentAccountId: "",
});
function mockData() {
  const data = empty();
  data.Accounts = Array.from({ length: 28 }, (_, i) => ({
    Id: `demo-${i}`,
    Username: `steam_player_${String(i + 1).padStart(2, "0")}`,
    GameId: `PUBG_Player_${i + 1}`,
    Email: `player${i + 1}@example.com`,
    Status: ["正常", "正常", "未查询", "临时封禁", "永久封禁"][i % 5],
    Level: i % 5 === 2 ? null : 42 + i * 11,
    Tier: 1 + (i % 5),
    LastLoginAt: i ? 0 : Date.now(),
    LastQueryAt: i % 5 === 2 ? 0 : Date.now() - i * 3600000,
    Note: i % 3 === 0 ? "常用账号" : "",
  }));
  data.CurrentAccountId = "demo-0";
  return data;
}
function send(channel, value) {
  if (main && !main.isDestroyed()) main.webContents.send(channel, value);
}
async function request(command, payload = {}) {
  if (typeof command !== "string" || !payload || typeof payload !== "object")
    throw new Error("无效操作。");
  if (command === "environment")
    return { preview, platform: process.platform, version, steamStatus };
  if (command === "choose-steam") {
    if (preview) throw new Error("Steam 登录设置需要在 Windows 上使用。");
    const result = await dialog.showOpenDialog(main, {
      title: "选择 Steam 主程序",
      properties: ["openFile"],
      filters: [{ name: "Steam", extensions: ["exe"] }],
    });
    return result.canceled ? null : result.filePaths[0];
  }
  if (command === "show-query") {
    if (job) {
      job.win.show();
      job.win.focus();
    }
    return null;
  }
  if (command === "query") {
    if (job) {
      job.win.focus();
      throw new Error("已有查询任务，请完成或结束后再开始。");
    }
    if (preview)
      throw new Error(
        "Mac 当前仅提供界面预览。请在 Windows 绿色包中验证查询同步。",
      );
    const ids = Array.isArray(payload.Ids) ? payload.Ids : [];
    const accounts = data?.Accounts.filter((a) => ids.includes(a.Id));
    if (!accounts?.length) throw new Error("请先选择账号。");
    // Keep an immutable task snapshot; later edits cannot retarget a response.
    job = new QueryJob(
      structuredClone(accounts),
      async (id, Result) => {
        const r = await bridge.invoke({ Command: "result", Ids: [id], Result });
        data = r.data;
        send("app:update", data);
      },
      (state) => send("query:state", state),
      () => {
        job = null;
      },
      data.Settings.QueryIntervalMilliseconds,
    );
    return { started: true };
  }
  if (preview) {
    if (command === "load") {
      data ??= mockData();
      return { data };
    }
    if (command === "demo") {
      data = mockData();
      return { data };
    }
    throw new Error("当前为 Mac 界面预览，此操作请在 Windows 绿色包中执行。");
  }
  const commands = new Set([
    "load",
    "save-account",
    "import",
    "delete",
    "settings",
    "login",
  ]);
  if (!commands.has(command)) throw new Error("不支持的操作。");
  if (job && ["save-account", "import", "delete", "login"].includes(command))
    throw new Error("请先结束查询任务，再修改账号或切换 Steam。");
  const result = await bridge.invoke({ ...payload, Command: command });
  data = result.data;
  return result;
}
if (!app.requestSingleInstanceLock()) app.quit();
else {
  app.on("second-instance", () => {
    if (main) {
      if (main.isMinimized()) main.restore();
      main.show();
      main.focus();
    }
  });
  app.whenReady().then(() => {
    if (process.platform === "darwin")
      app.dock.setIcon(path.join(__dirname, "../Assets/SteamSwitcher.png"));
    main = new BrowserWindow({
      width: 1360,
      height: 880,
      minWidth: 1060,
      minHeight: 700,
      backgroundColor: "#f4f6fa",
      title: development ? "Steam Login Lite · 开发版" : "Steam Login Lite",
      icon: path.join(__dirname, "../Assets/SteamSwitcher.png"),
      webPreferences: {
        preload: path.join(__dirname, "preload.cjs"),
        contextIsolation: true,
        nodeIntegration: false,
        sandbox: true,
      },
    });
    main.setMenuBarVisibility(false);
    main.webContents.setWindowOpenHandler(() => ({ action: "deny" }));
    main.webContents.on("will-navigate", (event, url) => {
      // Vite reloads JSX changes through window.location.reload(). Keep that
      // same-page navigation available only for the local development entry.
      if (development && new URL(url).href === "http://127.0.0.1:5173/") return;
      event.preventDefault();
    });
    if (development) main.webContents.on("page-title-updated", (event) => {
      event.preventDefault();
      main.setTitle("Steam Login Lite · 开发版");
    });
    main.on("closed", () => {
      if (job && !job.win.isDestroyed()) job.win.close();
      app.quit();
    });
    ipcMain.handle("app:request", async (event, command, payload) => {
      if (
        event.sender !== main.webContents ||
        event.senderFrame !== main.webContents.mainFrame
      )
        throw new Error("无效来源。");
      try {
        return { ok: true, value: await request(command, payload) };
      } catch (error) {
        return { ok: false, error: error.message };
      }
    });
    ipcMain.handle("query:action", (event, action) => {
      if (
        job &&
        event.sender === job.win.webContents &&
        ["retry", "skip", "cancel"].includes(action)
      )
        job.action(action);
    });
    ipcMain.on("query:ready", (event) => {
      if (job && event.sender === job.win.webContents)
        event.sender.send("query:state", job.state);
    });
    if (development)
      main.loadURL("http://127.0.0.1:5173");
    else main.loadFile(path.join(__dirname, "../dist/index.html"));
  });
  app.on("before-quit", bridge.stop);
  app.on("window-all-closed", () => app.quit());
}
