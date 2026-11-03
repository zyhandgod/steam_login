const { spawn } = require("node:child_process");
const http = require("node:http");
const path = require("node:path");
const vite = spawn(
  process.execPath,
  [path.join(__dirname, "../node_modules/vite/bin/vite.js")],
  { stdio: "inherit" },
);
let electron,
  stopping = false;
function stop() {
  if (stopping) return;
  stopping = true;
  vite.kill();
  electron?.kill();
}
process.on("SIGINT", stop);
process.on("SIGTERM", stop);
vite.on("exit", code => {
  if (!stopping) { stop(); process.exitCode = code || 1; }
});
const deadline = Date.now() + 20000;
function launch() {
  if (stopping) return;
  http
    .get("http://127.0.0.1:5173", (res) => {
      res.resume();
      if (res.statusCode !== 200) return retry();
      electron = spawn(require("electron"), ["."], {
        stdio: "inherit",
        env: { ...process.env, STEAM_DEV: "1" },
      });
      electron.on("exit", code => { process.exitCode = code ?? 1; stop(); });
    })
    .on("error", retry);
}
function retry() {
  if (Date.now() > deadline) {
    stop();
    process.exitCode = 1;
  } else setTimeout(launch, 250);
}
launch();
