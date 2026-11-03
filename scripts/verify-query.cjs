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
    let holdSecond = true;
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
        `<!doctype html><div class="search-input"><input placeholder="玩家昵称"></div><button id="query">查询</button><script>document.querySelector('input').addEventListener('input', e => { document.getElementById('query').disabled = ${holdSecond} && e.target.value === 'PlayerTwo'; }); document.getElementById('query').onclick=async()=>{const id=document.querySelector('input').value;await fetch('https://apiv1.pubg.plus/steam/player/basic?player_id='+encodeURIComponent(id));await fetch('https://apiv1.pubg.plus/steam/player/survival_mastery?acc_id=account.fixture')}</script>`,
        { headers: { "content-type": "text/html; charset=utf-8" } },
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
    assert.equal(states.filter(s => s.finished).length, 1);
    // With no challenge or disabled button, the whole batch completes unaided.
    holdSecond = false;
    const automatic = [], automaticSummary = [], automaticStates = [];
    job = new QueryJob(
      [{ Id: "auto-one", Username: "PlayerOne" }, { Id: "auto-two", Username: "PlayerTwo" }],
      async (id, result) => automatic.push({ id, ...result }),
      value => automaticStates.push(value), value => automaticSummary.push(value), 500,
    );
    await wait(() => automaticSummary.length === 1);
    assert.deepEqual(automatic.map(r => r.id), ["auto-one", "auto-two"]);
    assert.equal(automaticSummary[0].completed, 2);
    assert.equal(automaticSummary[0].skipped, 0);
    assert.equal(automaticStates.filter(s => s.finished).length, 1);
    const singleStates = [], singleSummary = [];
    job = new QueryJob([{ Id: "single", Username: "PlayerOne" }],
      async () => {}, value => singleStates.push(value), value => singleSummary.push(value), 500);
    await wait(() => singleSummary.length === 1);
    assert.equal(singleStates.filter(s => s.finished).length, 1);
    assert.equal(singleStates.find(s => s.finished).completed, 1);
    holdSecond = true;
    const canceledStates = [], canceledSummary = [];
    job = new QueryJob([{ Id: "cancel", Username: "PlayerTwo" }],
      async () => { throw Error("Disabled query must not save"); },
      value => canceledStates.push(value), value => canceledSummary.push(value), 500);
    job.action("cancel");
    await wait(() => canceledSummary.length === 1);
    assert.equal(canceledStates.filter(s => s.finished).length, 1);
    assert.equal(canceledSummary[0].completed, 0);
    console.log(
      "PASS: automatic submit and two-account batch, disabled button waits, correct result binding, basic/mastery merge, retry, skip, cleanup.",
    );
    app.exit(0);
  } catch (error) {
    console.error(error);
    if (job?.contents && !job.contents.isDestroyed()) console.error(await job.contents.executeJavaScript("JSON.stringify({visibility:document.visibilityState,input:document.querySelector('input')?.value,buttons:[...document.querySelectorAll('button')].map(b=>({text:b.textContent,disabled:b.disabled,rects:b.getClientRects().length})),html:document.body.innerHTML})").catch(e=>e.message));
    if (job && !job.win.isDestroyed()) job.win.close();
    app.exit(1);
  }
});
