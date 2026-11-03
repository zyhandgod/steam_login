const { test } = require("node:test");
const assert = require("node:assert/strict");
const { classify, normalize, parse } = require("./query-parser.cjs");
const basic = {
  id: "account.one",
  attributes: { name: "PlayerOne", banType: "Innocent" },
};
const mastery = { id: "account.one", attributes: { level: 150, tier: 3 } };
test("only trusted HTTPS endpoints and the selected nickname are accepted", () => {
  assert.deepEqual(
    classify(
      "https://apiv1.pubg.plus/steam/player/basic?player_id=playerone",
      "PlayerOne",
    ),
    { kind: "basic" },
  );
  for (const url of [
    "http://apiv1.pubg.plus/steam/player/basic?player_id=PlayerOne",
    "https://evil.test/steam/player/basic?player_id=PlayerOne",
    "https://apiv1.pubg.plus.evil.test/steam/player/basic?player_id=PlayerOne",
    "https://apiv1.pubg.plus/steam/player/basic?player_id=Other",
    "nonsense",
  ])
    assert.equal(classify(url, "PlayerOne"), null);
  assert.equal(
    classify(
      "https://apiv1.pubg.plus/player/info?player_id=PlayerOne",
      "PlayerOne",
    ).kind,
    "legacy",
  );
  assert.deepEqual(
    classify(
      "https://apiv1.pubg.plus/steam/player/survival_mastery?acc_id=account.one",
      "PlayerOne",
    ),
    { kind: "mastery", accountId: "account.one" },
  );
  assert.equal(
    classify(
      "https://apiv1.pubg.plus/steam/player/survival_mastery",
      "PlayerOne",
    ),
    null,
  );
});
test("basic result must match the task nickname or account ID", () => {
  const result = parse(JSON.stringify(basic), { kind: "basic" }, "PlayerOne");
  assert.equal(result.Status, "正常");
  assert.equal(result.AccountId, "account.one");
  assert.equal(result.HasLevel, false);
  assert.equal(parse(JSON.stringify(basic), { kind: "basic" }, "Other"), null);
  assert.equal(
    parse(JSON.stringify({ data: basic }), { kind: "basic" }, "account.one")
      .Status,
    "正常",
  );
  for (const body of [
    "{",
    "null",
    "{}",
    JSON.stringify({ code: 403, data: basic }),
    JSON.stringify({ attributes: { name: "PlayerOne" } }),
  ])
    assert.equal(parse(body, { kind: "basic" }, "PlayerOne"), null);
});
test("mastery result is bound to account ID and validates level", () => {
  const match = { kind: "mastery", accountId: "account.one" };
  assert.equal(parse(JSON.stringify(mastery), match, "PlayerOne").Level, 150);
  assert.equal(
    parse(
      JSON.stringify(mastery),
      { ...match, accountId: "account.other" },
      "PlayerOne",
    ),
    null,
  );
  for (const level of [-1, "150", null, 1.5])
    assert.equal(
      parse(JSON.stringify({ attributes: { level } }), match, "PlayerOne"),
      null,
    );
  assert.equal(
    parse(
      JSON.stringify({ attributes: { level: 0, tier: 8 } }),
      match,
      "PlayerOne",
    ).Tier,
    0,
  );
});
test("legacy results validate nickname, platform, status and level", () => {
  const player = {
    name: "PlayerOne",
    shardId: "steam",
    ban: "PermanentBan",
    level: 100,
    tier: 2,
  };
  assert.equal(
    parse(
      JSON.stringify({ code: 0, data: { player } }),
      { kind: "legacy" },
      "PlayerOne",
    ).Status,
    "永久封禁",
  );
  for (const replacement of [
    { name: "Other" },
    { shardId: "kakao" },
    { level: -1 },
    { ban: "" },
  ])
    assert.equal(
      parse(
        JSON.stringify({ player: { ...player, ...replacement } }),
        { kind: "legacy" },
        "PlayerOne",
      ),
      null,
    );
  assert.equal(parse('{"data":[]}', { kind: "legacy" }, "PlayerOne"), null);
});
test("ban labels normalize without treating unknown responses as healthy", () => {
  for (const raw of ["Not banned", "Innocent", "正常", "none", "0"])
    assert.equal(normalize(raw), "正常");
  for (const raw of ["TemporaryBan", "Temporarily banned", "临时封禁"])
    assert.equal(normalize(raw), "临时封禁");
  for (const raw of ["PermanentBan", "Permanently banned", "永久封禁"])
    assert.equal(normalize(raw), "永久封禁");
  assert.match(normalize("unexpected"), /^未知状态/);
});
