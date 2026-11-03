const same = (a, b) =>
  typeof a === "string" &&
  typeof b === "string" &&
  a.toLowerCase() === b.toLowerCase();
function classify(url, expected) {
  try {
    const u = new URL(url);
    if (u.protocol !== "https:" || u.hostname !== "apiv1.pubg.plus")
      return null;
    if (
      /\/steam\/player\/basic$/.test(u.pathname) &&
      same(u.searchParams.get("player_id"), expected)
    )
      return { kind: "basic" };
    if (
      /\/player\/info$/.test(u.pathname) &&
      same(u.searchParams.get("player_id"), expected)
    )
      return { kind: "legacy" };
    if (
      /\/steam\/player\/survival_mastery$/.test(u.pathname) &&
      u.searchParams.get("acc_id")
    )
      return { kind: "mastery", accountId: u.searchParams.get("acc_id") };
  } catch {}
  return null;
}
function normalize(value) {
  const key = String(value).trim().toLowerCase().replace(/[_ ]/g, "");
  if (
    ["正常", "未封禁", "innocent", "normal", "notbanned", "none", "0"].includes(
      key,
    )
  )
    return "正常";
  if (/临时封禁|temporary|temporarily|tempban/.test(key)) return "临时封禁";
  if (/永久封禁|permanent|permanently|permban/.test(key)) return "永久封禁";
  return `未知状态：${value}`;
}
function parse(body, match, expected) {
  try {
    const root = JSON.parse(body),
      data = root.data ?? root;
    if ("code" in root && Number(root.code) !== 0) return null;
    if (match.kind === "basic") {
      const a = data.attributes;
      if (
        !a ||
        !data.id ||
        !a.banType ||
        !(same(a.name, expected) || same(data.id, expected))
      )
        return null;
      return {
        GameId: expected,
        AccountId: data.id,
        RawStatus: a.banType,
        Status: normalize(a.banType),
        HasLevel: false,
      };
    }
    if (match.kind === "mastery") {
      const a = data.attributes;
      if (
        !a ||
        (data.id && !same(data.id, match.accountId)) ||
        !Number.isInteger(a.level) ||
        a.level < 0
      )
        return null;
      return {
        AccountId: match.accountId,
        HasLevel: true,
        Level: a.level,
        Tier:
          Number.isInteger(a.tier) && a.tier >= 1 && a.tier <= 5 ? a.tier : 0,
      };
    }
    const find = (n) => {
      if (!n || typeof n !== "object") return null;
      if (n.player && typeof n.player === "object") return n.player;
      for (const value of Object.values(n)) {
        const p = find(value);
        if (p) return p;
      }
      return null;
    };
    const p = find(root);
    if (
      !p ||
      !same(p.name, expected) ||
      (p.shardId && !same(p.shardId, "steam")) ||
      !p.ban ||
      !Number.isInteger(p.level) ||
      p.level < 0
    )
      return null;
    return {
      GameId: expected,
      RawStatus: p.ban,
      Status: normalize(p.ban),
      HasLevel: true,
      Level: p.level,
      Tier: p.tier >= 1 && p.tier <= 5 ? p.tier : 0,
    };
  } catch {
    return null;
  }
}
module.exports = { classify, normalize, parse, same };
