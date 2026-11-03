const { BrowserWindow, WebContentsView, session } = require("electron");
const path = require("node:path");
const { classify, parse, same } = require("./query-parser.cjs");
const { autofillScript } = require("./query-autofill.cjs");
const PAGE = "https://pubg.plus/zh-CN/player";
function allowed(url) {
  try {
    const u = new URL(url);
    return u.protocol === "https:" && u.hostname === "pubg.plus";
  } catch {
    return false;
  }
}
class QueryJob {
  constructor(accounts, save, notify, onClose, interval = 1200) {
    this.accounts = accounts;
    this.save = save;
    this.notify = notify;
    this.onClose = onClose;
    this.interval = interval;
    this.index = 0;
    this.generation = 0;
    this.timers = new Set();
    this.completed = 0;
    this.skipped = 0;
    this.win = new BrowserWindow({
      width: 1180,
      height: 850,
      minWidth: 920,
      minHeight: 650,
      title: "PUBG.PLUS · 查询与同步",
      backgroundColor: "#101827",
      webPreferences: {
        preload: path.join(__dirname, "query-preload.cjs"),
        contextIsolation: true,
        nodeIntegration: false,
        sandbox: true,
      },
    });
    this.win.setMenuBarVisibility(false);
    this.win.webContents.setWindowOpenHandler(() => ({ action: "deny" }));
    this.win.webContents.on("will-navigate", (e) => e.preventDefault());
    this.win.loadFile(path.join(__dirname, "query.html"));
    this.win.on("resize", () => this.resize());
    this.win.on("closed", () => {
      this.disposePage();
      const summary = {
        completed: this.completed,
        skipped: this.skipped,
        total: accounts.length,
      };
      this.emit(`已保存 ${this.completed} 个账号，跳过 ${this.skipped} 个。`, {
        ...summary,
        finished: true,
      });
      this.onClose(summary);
    });
    this.next();
  }
  get account() {
    return this.accounts[this.index];
  }
  get contents() {
    return this.view?.webContents;
  }
  emit(message, extra = {}) {
    this.state = {
      index: this.index,
      total: this.accounts.length,
      gameId: this.account?.GameId || this.account?.Username,
      completed: this.completed,
      skipped: this.skipped,
      message,
      saving: this.saving,
      ...extra,
    };
    if (!this.win.isDestroyed()) {
      this.win.setTitle(`${this.state.gameId || "已完成"} · PUBG.PLUS`);
      this.win.webContents.send("query:state", this.state);
    }
    this.notify(this.state);
  }
  later(callback, ms) {
    const id = setTimeout(() => {
      this.timers.delete(id);
      callback();
    }, ms);
    this.timers.add(id);
  }
  resize() {
    if (this.view && !this.win.isDestroyed()) {
      const [width, height] = this.win.getContentSize();
      this.view.setBounds({
        x: 0,
        y: 108,
        width,
        height: Math.max(0, height - 108),
      });
    }
  }
  disposePage() {
    this.generation++;
    for (const timer of this.timers) clearTimeout(timer);
    this.timers.clear();
    if (this.view) {
      if (!this.win.isDestroyed())
        this.win.contentView.removeChildView(this.view);
      if (!this.view.webContents.isDestroyed()) this.view.webContents.close();
      this.view = null;
    }
  }
  async next() {
    this.disposePage();
    this.saving = false;
    this.basic = null;
    this.mastery = new Map();
    if (this.index >= this.accounts.length) {
      this.win.close();
      return;
    }
    const generation = this.generation;
    const valid = () =>
      generation === this.generation && !this.win.isDestroyed();
    const expected = this.account.GameId || this.account.Username;
    const ses = session.fromPartition("persist:pubg-query");
    ses.setPermissionRequestHandler((_wc, _permission, callback) =>
      callback(false),
    );
    if (!ses.__steamDownloadsBlocked) {
      ses.on("will-download", (event) => event.preventDefault());
      ses.__steamDownloadsBlocked = true;
    }
    this.view = new WebContentsView({
      webPreferences: {
        session: ses,
        contextIsolation: true,
        nodeIntegration: false,
        sandbox: true,
      },
    });
    this.win.contentView.addChildView(this.view);
    this.resize();
    const wc = this.contents;
    wc.setWindowOpenHandler(() => ({ action: "deny" }));
    wc.on("will-navigate", (event, url) => {
      if (!allowed(url)) event.preventDefault();
    });
    wc.on("will-redirect", (event, url) => {
      if (!allowed(url)) event.preventDefault();
    });
    wc.on("render-process-gone", () => {
      if (valid()) this.emit("查询页面进程退出，请点击重试。");
    });
    wc.on("did-fail-load", (_e, code, _description, _url, main) => {
      if (valid() && main && code !== -3)
        this.emit("查询网站加载失败，请检查网络后重试，或跳过当前账号。");
    });
    let automationStarted = false;
    wc.on("did-finish-load", async () => {
      if (!valid() || !allowed(wc.getURL()) || automationStarted) return;
      automationStarted = true;
      try {
        this.emit("正在自动填写并提交查询；如出现验证码，请手动完成。", { autoSubmitting: true });
        const outcome = await wc.executeJavaScript(autofillScript(expected));
        if (valid() && !this.saving && !this.basic)
          this.emit(
            outcome === "submitted"
              ? "已自动点击查询，正在等待结果。如出现验证码，请手动完成；结果将自动保存并继续下一个。"
              : outcome === "filled"
              ? "查询标识已填写，但未能自动提交，请在网页点击查询；如有验证码，请手动完成。"
              : "未找到可填写的输入框，请在网页手动输入上方查询标识 后点击查询，或重试。",
          );
      } catch {
        if (valid())
          this.emit("未能自动填写，请在网页输入当前查询标识 后点击查询。");
      }
    });
    this.emit("正在加载查询网站…");
    try {
      // Initialize a renderer before issuing CDP commands; a fresh view has none yet.
      await wc.loadURL("about:blank");
      if (!valid()) return;
      wc.debugger.attach("1.3");
      await wc.debugger.sendCommand("Network.enable");
      if (!valid()) return;
      const requests = new Map();
      wc.debugger.on("message", async (_event, method, params) => {
        if (!valid() || this.saving) return;
        if (method === "Network.responseReceived") {
          const match = classify(params.response.url, expected);
          if (match && params.response.status === 200)
            requests.set(params.requestId, match);
        }
        if (method === "Network.loadingFailed")
          requests.delete(params.requestId);
        if (
          method !== "Network.loadingFinished" ||
          !requests.has(params.requestId)
        )
          return;
        const match = requests.get(params.requestId);
        requests.delete(params.requestId);
        try {
          const response = await wc.debugger.sendCommand(
            "Network.getResponseBody",
            { requestId: params.requestId },
          );
          if (!valid() || this.saving) return;
          const result = parse(
            response.base64Encoded
              ? Buffer.from(response.body, "base64").toString("utf8")
              : response.body,
            match,
            expected,
          );
          if (!result) {
            this.emit("网页返回了无法匹配的结果，请确认查询标识 或重试。");
            return;
          }
          if (match.kind === "legacy") return this.accept(result, generation);
          if (match.kind === "basic") {
            this.basic = result;
            this.emit("已读取封禁状态，正在等待等级信息…");
            this.later(() => {
              if (valid() && !this.saving && this.basic === result)
                this.accept(result, generation);
            }, 8000);
          } else this.mastery.set(result.AccountId.toLowerCase(), result);
          if (
            this.basic &&
            this.mastery.has(this.basic.AccountId.toLowerCase())
          ) {
            const mastery = this.mastery.get(
              this.basic.AccountId.toLowerCase(),
            );
            if (same(mastery.AccountId, this.basic.AccountId))
              this.accept({ ...this.basic, ...mastery }, generation);
          }
        } catch {
          if (valid()) this.emit("读取查询结果失败，请重试当前账号。");
        }
      });
      // Set the platform before the first document loads, including SPA initialization.
      await wc.debugger.sendCommand("Page.enable");
      await wc.debugger.sendCommand("Page.addScriptToEvaluateOnNewDocument", {
        source:
          "try{localStorage.setItem('pubgSelectedPlatform','steam')}catch{}",
      });
      if (valid()) wc.loadURL(PAGE).catch(() => {});
    } catch {
      if (valid()) this.emit("内置浏览器监听初始化失败，请重试。");
    }
    this.later(() => {
      if (valid() && !this.saving)
        this.emit("暂未获取结果。如有验证码，请手动完成；也可手动点击查询、重试或跳过。");
    }, 90000);
  }
  async accept(result, generation) {
    if (generation !== this.generation || this.saving) return;
    this.saving = true;
    this.emit("正在保存查询结果…");
    try {
      await this.save(this.account.Id, result);
      if (generation !== this.generation || this.win.isDestroyed()) return;
      this.completed++;
      this.emit(
        result.HasLevel
          ? "查询结果已保存。"
          : "已保存封禁状态，等级未返回，保留原等级。",
      );
      this.later(
        () => {
          this.index++;
          this.next();
        },
        Math.max(500, this.interval),
      );
    } catch (error) {
      if (generation === this.generation && !this.win.isDestroyed()) {
        this.saving = false;
        this.emit(`保存失败：${error.message}。请重试。`);
      }
    }
  }
  action(action) {
    if (action === "cancel") {
      this.win.close();
      return;
    }
    if (this.saving) return;
    if (action === "skip") {
      this.skipped++;
      this.index++;
      this.next();
    }
    if (action === "retry") this.next();
  }
}
module.exports = { QueryJob, allowed };
