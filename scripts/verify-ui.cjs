// Run with: electron scripts/verify-ui.cjs (macOS preview only).
const { app, BrowserWindow } = require("electron");
app.setPath(
  "userData",
  require("node:fs").mkdtempSync(
    require("node:path").join(require("node:os").tmpdir(), "steam-verify-"),
  ),
);
const assert = require("node:assert/strict");
const path = require("node:path");
const fs = require("node:fs");
if (process.platform === "win32")
  throw new Error(
    "UI smoke uses isolated Mac preview data; do not run against Windows accounts.",
  );
require("../desktop/main.cjs");
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
async function until(wc, expression) {
  for (let i = 0; i < 100; i++) {
    if (await wc.executeJavaScript(expression)) return;
    await sleep(100);
  }
  throw new Error(`Timed out: ${expression}`);
}
app.whenReady().then(async () => {
  try {
    const win = BrowserWindow.getAllWindows()[0],
      wc = win.webContents;
    const errors = [];
    wc.on("console-message", (event) => {
      if (event.level === "error") errors.push(event.message);
    });
    await until(wc, "document.body.innerText.includes('重置示例数据')");
    assert.equal(
      await wc.executeJavaScript(
        "document.querySelector('.brand-mark img')?.alt",
      ),
      "Steam Login Lite",
    );
    await until(
      wc,
      "document.querySelectorAll('tr.ant-table-row').length===10",
    );
    await wc.executeJavaScript(
      "document.querySelector('.selection-bar input').click()",
    );
    await until(
      wc,
      "document.querySelector('.selection-count').textContent.includes('28')",
    );
    await wc.executeJavaScript(
      "document.querySelector('.ant-pagination-item-2').click()",
    );
    await until(
      wc,
      "document.querySelector('.ant-pagination-item-2').classList.contains('ant-pagination-item-active')",
    );
    assert.equal(
      await wc.executeJavaScript(
        "document.querySelectorAll('tr.ant-table-row input:checked').length",
      ),
      10,
    );
    await wc.executeJavaScript(
      `var input=document.querySelector('.search input');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'PUBG_Player_28');input.dispatchEvent(new Event('input',{bubbles:true}));`,
    );
    await until(wc, "document.querySelectorAll('tr.ant-table-row').length===1");
    await wc.executeJavaScript(
      "document.querySelector('.selection-bar input').click()",
    );
    await until(
      wc,
      "document.querySelector('.selection-count').textContent.includes('27')",
    );
    await wc.executeJavaScript(
      `var input=document.querySelector('.search input');Object.getOwnPropertyDescriptor(HTMLInputElement.prototype,'value').set.call(input,'');input.dispatchEvent(new Event('input',{bubbles:true}));`,
    );
    await until(
      wc,
      "document.querySelectorAll('tr.ant-table-row').length===10",
    );
    await wc.executeJavaScript(
      "[...document.querySelectorAll('button')].find(b=>b.textContent.includes('取消选择')).click()",
    );
    await until(
      wc,
      "document.querySelector('.selection-count').textContent.includes('0')",
    );
    await wc.executeJavaScript(
      "document.querySelector('.ant-pagination-item-1').click()",
    );
    win.show();
    win.focus();
    await sleep(1500);
    fs.mkdirSync(path.join(__dirname, "../release"), { recursive: true });
    fs.writeFileSync(
      path.join(__dirname, "../release/preview.png"),
      (await wc.capturePage()).toPNG(),
    );
    await wc.executeJavaScript(
      "[...document.querySelectorAll('button')].find(b=>b.textContent.includes('添加账号')).click()",
    );
    await until(wc, "!!document.querySelector('.ant-drawer-open')");
    assert.equal(
      await wc.executeJavaScript(
        "document.querySelectorAll('.ant-drawer input').length",
      ),
      5,
    );
    await wc.executeJavaScript(
      "document.querySelector('.ant-drawer-close').click()",
    );
    await until(wc, "!document.querySelector('.ant-drawer-open')");
    await wc.executeJavaScript(
      "[...document.querySelectorAll('.nav-item')].find(b=>b.textContent.includes('偏好设置')).click()",
    );
    await until(
      wc,
      "document.querySelector('.ant-drawer-open')?.textContent.includes('Steam 启动')",
    );
    assert.equal(
      await wc.executeJavaScript(
        "document.querySelectorAll('.ant-drawer-open .ant-switch-checked').length",
      ),
      3,
    );
    assert.equal(
      await wc.executeJavaScript(
        "[...document.querySelectorAll('.ant-drawer-open button')].some(b=>b.textContent.includes('检查更新'))",
      ),
      false,
    );
    await sleep(500);
    fs.writeFileSync(
      path.join(__dirname, "../release/settings-preview.png"),
      (await wc.capturePage()).toPNG(),
    );
    assert.deepEqual(
      errors.filter((e) => !e.includes("Electron Security Warning")),
      [],
    );
    console.log(
      "PASS: preview boot, 28 demo records, pagination, cross-page select-all, clear selection, account drawer, no update controls, 3 Steam switches, screenshots.",
    );
    app.exit(0);
  } catch (error) {
    console.error(error);
    app.exit(1);
  }
});
