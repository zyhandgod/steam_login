// Run with: electron scripts/verify-ui.cjs (isolated preview on every OS).
process.env.STEAM_PREVIEW = "1";
const { app, BrowserWindow } = require("electron");
app.commandLine.appendSwitch("force-prefers-reduced-motion", "reduce");
app.setPath(
  "userData",
  require("node:fs").mkdtempSync(
    require("node:path").join(require("node:os").tmpdir(), "steam-verify-"),
  ),
);
const assert = require("node:assert/strict");
const path = require("node:path");
const fs = require("node:fs");
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
    const execute = wc.executeJavaScript.bind(wc);
    wc.executeJavaScript = async (expression) => {
      try { return await execute(expression); }
      catch (error) { throw new Error(`Renderer check failed: ${expression}`, { cause: error }); }
    };
    const errors = [];
    wc.on("console-message", (event) => {
      if (event.level === "error") errors.push(event.message);
    });
    await until(wc, "document.body.innerText.includes('重置示例数据')");
    assert.equal(await wc.executeJavaScript("matchMedia('(prefers-reduced-motion: reduce)').matches"), true);
    assert.equal(await wc.executeJavaScript("document.documentElement.dataset.motion"), "full");
    assert.notEqual(await wc.executeJavaScript("getComputedStyle(document.querySelector('.table-card')).animationName"), "none");
    assert.equal(
      await wc.executeJavaScript(
        "document.querySelector('.brand-mark img')?.alt",
      ),
      "Steam Login Lite",
    );
    await until(
      wc,
      "document.querySelectorAll('tr.ant-table-row').length===20",
    );
    assert.equal(await wc.executeJavaScript("document.querySelectorAll('tr.current-account-row').length"), 1);
    assert.equal(await wc.executeJavaScript("document.querySelector('.current-account-badge').textContent.trim()"), "当前账号");
    assert.notEqual(await wc.executeJavaScript("getComputedStyle(document.querySelector('tr.current-account-row > td')).backgroundColor"), "rgb(255, 255, 255)");
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
      8,
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
      "document.querySelectorAll('tr.ant-table-row').length===20",
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
    async function choosePageSize(size) {
      await wc.executeJavaScript("document.querySelector('.ant-pagination-options-size-changer .ant-select-selector').dispatchEvent(new MouseEvent('mousedown', {bubbles:true}))");
      await until(wc, "!!document.querySelector('.ant-select-dropdown:not(.ant-select-dropdown-hidden) .ant-select-item-option')");
      await wc.executeJavaScript(`Array.from(document.querySelectorAll('.ant-select-dropdown:not(.ant-select-dropdown-hidden) .ant-select-item-option')).find(e=>e.textContent.trim()=== '${size} 条/页').click()`);
      await until(wc, `document.querySelectorAll('tr.ant-table-row').length === ${size}`);
    }
    await choosePageSize(10);
    assert.equal(await wc.executeJavaScript("document.querySelector('.ant-pagination-item-1').classList.contains('ant-pagination-item-active')"), true);
    await choosePageSize(20);
    for (const [width, height] of [[1060, 700], [1360, 880]]) {
      win.setSize(width, height);
      await sleep(250);
      const layout = await wc.executeJavaScript(`(() => {
        const body = document.querySelector('.ant-table-body');
        const pagination = document.querySelector('.ant-pagination').getBoundingClientRect();
        return { pageFits: document.documentElement.scrollHeight <= innerHeight,
          paginationVisible: pagination.bottom <= innerHeight && pagination.top >= 0,
          internalScroll: body.scrollHeight > body.clientHeight && body.clientHeight > 0 };
      })()`);
      assert.deepEqual(layout, { pageFits:true, paginationVisible:true, internalScroll:true });
    }
    const density = await wc.executeJavaScript(`(() => {
      const rows = [...document.querySelectorAll('tr.ant-table-row')];
      const body = document.querySelector('.ant-table-body').getBoundingClientRect();
      return { maxHeight: Math.max(...rows.map(row => row.getBoundingClientRect().height)),
        visibleRows: rows.filter(row => row.getBoundingClientRect().bottom <= body.bottom).length };
    })()`);
    assert.ok(density.maxHeight <= 45, JSON.stringify(density));
    assert.ok(density.visibleRows >= 9, JSON.stringify(density));
    const longName = await wc.executeJavaScript(`(() => {
      const name = document.querySelector('.account-name');
      const original = name.textContent;
      name.textContent = 'very_long_steam_account_'.repeat(10);
      const button = name.parentElement.querySelector('button').getBoundingClientRect();
      const cell = name.closest('td').getBoundingClientRect();
      const fits = button.left >= cell.left && button.right <= cell.right;
      name.textContent = original;
      return fits;
    })()`);
    assert.equal(longName, true);
    console.log('Compact table:', density);
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
    async function selectMotion(label, expected) {
      await wc.executeJavaScript(`Array.from(document.querySelectorAll('.motion-settings label')).find(e => e.textContent === ${JSON.stringify(label)}).click()`);
      await until(wc, `document.documentElement.dataset.motion === ${JSON.stringify(expected)}`);
      await until(wc, "!!document.querySelector('.ant-drawer-open .ant-switch') && !!document.querySelector('.motion-settings label')");
    }
    await selectMotion("减少动效", "reduced");
    assert.equal(await wc.executeJavaScript("getComputedStyle(document.querySelector('.stat')).transitionDuration"), "0s");
    assert.equal(await wc.executeJavaScript("getComputedStyle(document.querySelector('.ant-drawer .ant-switch')).transitionDuration"), "0s");
    await selectMotion("跟随系统", "reduced");
    await selectMotion("流畅动效", "full");
    await until(wc, "getComputedStyle(document.querySelector('.ant-drawer-open .ant-switch')).transitionDuration !== '0s'");
    await selectMotion("减少动效", "reduced");
    wc.reload();
    await until(wc, "document.querySelectorAll('tr.ant-table-row').length===20 && document.documentElement.dataset.motion==='reduced'");
    await wc.executeJavaScript("[...document.querySelectorAll('.nav-item')].find(b=>b.textContent.includes('偏好设置')).click()");
    await until(wc, "!!document.querySelector('.motion-settings label')");
    await selectMotion("流畅动效", "full");
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
      "PASS: preview, selection, pagination, drawers, settings, full motion with reduced OS setting, reduced/system modes, Ant Design transitions, preference persistence, screenshots.",
    );
    app.exit(0);
  } catch (error) {
    console.error(error);
    app.exit(1);
  }
});
