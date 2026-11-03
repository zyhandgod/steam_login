// Optional network smoke: load the real site and check autofill only; never submit a query.
const { app } = require("electron");
const fs = require("node:fs"),
  path = require("node:path"),
  os = require("node:os");
app.setPath("userData", fs.mkdtempSync(path.join(os.tmpdir(), "steam-live-")));
app.on("window-all-closed", () => {});
const { QueryJob } = require("../desktop/query.cjs");
app.whenReady().then(async () => {
  let job;
  const timeout = setTimeout(async () => {
    console.error("Live site autofill could not be verified", job?.state);
    console.error(
      await job.contents.executeJavaScript(
        'JSON.stringify({url:location.href,body:document.body.innerText.slice(0,2000),inputs:[...document.querySelectorAll("input")].map(i=>({placeholder:i.placeholder,value:i.value,cls:i.className}))})',
      ),
    );
    fs.writeFileSync(
      path.join(__dirname, "../release/live-site.png"),
      (await job.win.capturePage()).toPNG(),
    );
    app.exit(1);
  }, 60000);
  job = new QueryJob(
    [{ Id: "check", Username: "AutofillPreview", GameId: "AutofillPreview" }],
    async () => {
      throw new Error("This smoke must not save/query");
    },
    (state) => console.log(state.message),
    () => {},
  );
  const check = setInterval(async () => {
    const wc = job.contents;
    if (!wc || wc.isDestroyed() || wc.isLoadingMainFrame()) return;
    try {
      const result = await wc.executeJavaScript(
        "[...document.querySelectorAll('input')].some(i=>i.value==='AutofillPreview')",
      );
      if (result) {
        clearInterval(check);
        clearTimeout(timeout);
        console.log(
          "PASS: live PUBG.PLUS page renders and nickname autofills; no query submitted.",
        );
        job.win.close();
        app.exit(0);
      }
    } catch {}
  }, 500);
});
