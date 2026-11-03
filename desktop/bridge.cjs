const { spawn } = require("node:child_process");
const path = require("node:path");
const { app } = require("electron");
const children = new Set();
const loginChildren = new Set();
const events = new (require("node:events").EventEmitter)();
let pending = Promise.resolve();
let loginGeneration = 0;
function invoke(request) {
  if (process.platform !== "win32")
    return Promise.reject(
      new Error(
        "当前为 Mac 界面预览。账号加密、迁移和 Steam 切换需要在 Windows 上使用。",
      ),
    );
  const run = () =>
    new Promise((resolve, reject) => {
      const exe = path.join(
        app.isPackaged
          ? path.join(process.resourcesPath, "bridge")
          : path.join(__dirname, "../helper/bin/Release/net48"),
        "SteamLoginBridge.exe",
      );
      if (request.Command === "login") {
        loginGeneration++;
        for (const previous of loginChildren) previous.kill();
        loginChildren.clear();
      }
      const generation = loginGeneration;
      let postDone = false;
      const child = spawn(exe, [], {
        windowsHide: true,
        stdio: ["pipe", "pipe", "pipe"],
        env: {
          ...process.env,
          STEAM_LOGIN_ROOT: app.isPackaged
            ? path.dirname(app.getPath("exe"))
            : path.join(__dirname, ".."),
        },
      });
      children.add(child);
      if (request.Command === "login") loginChildren.add(child);
      let buffer = "",
        settled = false;
      const timeout = setTimeout(() => {
        child.kill();
        finish(new Error("操作超时，请检查 Steam 是否已退出后重试。"));
      }, 30000);
      function finish(error, value) {
        if (settled) return;
        settled = true;
        clearTimeout(timeout);
        error ? reject(error) : resolve(value);
      }
      child.stdout.setEncoding("utf8");
      child.stdout.on("data", (chunk) => {
        buffer += chunk;
        if (buffer.length > 16 * 1024 * 1024) {
          child.kill();
          return finish(new Error("账号数据过大。"));
        }
        let line;
        while ((line = buffer.indexOf("\n")) >= 0) {
          const raw = buffer.slice(0, line).replace(/^\uFEFF/, "");
          buffer = buffer.slice(line + 1);
          try {
            const r = JSON.parse(raw);
            if (r.Event === "steam-status") {
              postDone = [
                "completed",
                "failed",
                "timeout",
                "canceled",
              ].includes(r.Status);
              if (generation === loginGeneration)
                events.emit("status", { status: r.Status, message: r.Message });
              continue;
            }
            r.ok ? finish(null, r) : finish(new Error(r.error));
          } catch {
            finish(new Error("辅助程序返回了无效数据。"));
          }
        }
      });
      child.on("error", () =>
        finish(
          new Error(
            "无法启动 Windows 辅助程序，请确认绿色包已完整解压且未被安全软件移除。",
          ),
        ),
      );
      child.stdin.on("error", () => {});
      child.stderr.resume();
      child.on("exit", () => {
        children.delete(child);
        loginChildren.delete(child);
        if (
          request.Command === "login" &&
          !postDone &&
          generation === loginGeneration
        )
          events.emit("status", {
            status: "failed",
            message: "登录后处理进程已结束，部分窗口操作可能未完成。",
          });
        finish(new Error("辅助程序意外退出。"));
      });
      child.stdin.end(JSON.stringify(request) + "\n");
    });
  const next = pending.then(run, run);
  pending = next.catch(() => {});
  return next;
}
function stop() {
  for (const child of children) child.kill();
}
module.exports = { invoke, stop, events, whenIdle: () => pending };
