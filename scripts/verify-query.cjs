// Real Chromium integration against isolated HTTPS fixtures; no live player queries.
const { app, session } = require("electron");
app.setPath(
  "userData",
  require("node:fs").mkdtempSync(
    require("node:path").join(require("node:os").tmpdir(), "steam-verify-"),
  ),
);
app.on("window-all-closed", () => {});
const assert = require("node:assert/strict");
const { QueryJob } = require("../desktop/query.cjs");
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
async function wait(check) {
  for (let i = 0; i < 150; i++) {
    if (await check()) return;
    await sleep(100);
  }
  throw new Error("Integration timeout");
}
app.whenReady().then(async () => {
  let job;
  const deadline = setTimeout(() => {
    console.error("Hard timeout", job?.state, job?.contents?.getURL());
    app.exit(1);
  }, 45000);
  try {
    const ses = session.fromPartition("persist:pubg-query");
    await ses.clearStorageData();
    ses.protocol.handle("https", (request) => {
      const url = new URL(request.url);
      console.log("Fixture request", url.hostname, url.pathname);
      if (url.hostname === "apiv1.pubg.plus") {
        const body = url.pathname.endsWith("/basic")
          ? {
              id: "account.fixture",
              attributes: {
                name: url.searchParams.get("player_id"),
                banType: "Innocent",
              },
            }
          : { id: "account.fixture", attributes: { level: 155, tier: 3 } };
        return new Response(JSON.stringify(body), {
          headers: {
            "content-type": "application/json",
            "access-control-allow-origin": "*",
          },
        });
      }
      return new Response(
        `<!doctype html><div class="search-input"><input placeholder="玩家昵称"></div><button id="query">查询</button><script>document.getElementById('query').onclick=async()=>{const id=document.querySelector('input').value;await fetch('https://apiv1.pubg.plus/steam/player/basic?player_id='+encodeURIComponent(id));await fetch('https://apiv1.pubg.plus/steam/player/survival_mastery?acc_id=account.fixture')}</script>`,
        { headers: { "content-type": "text/html" } },
      );
    });
    const results = [],
      states = [],
      summary = [];
    job = new QueryJob(
      [
        { Id: "one", Username: "first", GameId: "PlayerOne" },
        { Id: "two", Username: "second", GameId: "PlayerTwo" },
      ],
      async (id, result) => {
        results.push({ id, ...result });
      },
      (state) => {
        states.push(state);
        console.log("State", state.message);
      },
      (value) => summary.push(value),
      500,
    );
    await wait(
      async () =>
        job.contents &&
        !job.contents.isDestroyed() &&
        !job.contents.isLoadingMainFrame() &&
        (await job.contents
          .executeJavaScript(
            "document.querySelector('input')?.value === 'PlayerOne'",
          )
          .catch(() => false)),
    );
    assert.equal(
      await job.contents.executeJavaScript("typeof window.steam"),
      "undefined",
    );
    assert.equal(
      await job.contents.executeJavaScript("typeof require"),
      "undefined",
    );
    await job.contents.executeJavaScript(
      "document.getElementById('query').click()",
    );
    await wait(() => results.length === 1);
    assert.equal(results[0].id, "one");
    assert.equal(results[0].Level, 155);
    assert.equal(results[0].Status, "正常");
    await wait(
      async () =>
        job.account?.Id === "two" &&
        job.contents &&
        !job.contents.isLoadingMainFrame() &&
        (await job.contents
          .executeJavaScript(
            "document.querySelector('input')?.value === 'PlayerTwo'",
          )
          .catch(() => false)),
    );
    job.action("retry");
    await wait(
      async () =>
        job.contents &&
        !job.contents.isLoadingMainFrame() &&
        (await job.contents
          .executeJavaScript(
            "document.querySelector('input')?.value === 'PlayerTwo'",
          )
          .catch(() => false)),
    );
    job.action("skip");
    await wait(() => summary.length === 1);
    assert.equal(summary[0].completed, 1);
    assert.equal(summary[0].skipped, 1);
    assert.equal(results.length, 1);
    console.log(
      "PASS: real isolated Chromium autofill, basic/mastery capture and merge, correct account writeback, retry, skip, cleanup.",
    );
    app.exit(0);
  } catch (error) {
    console.error(error);
    if (job && !job.win.isDestroyed()) job.win.close();
    app.exit(1);
  }
});
