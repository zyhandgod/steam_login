for (const action of ["retry", "skip", "cancel"])
  document.getElementById(action).onclick = () => window.query.action(action);
window.query.subscribe((state) => {
  document.getElementById("title").textContent =
    `${state.index + 1} / ${state.total} · ${state.gameId || "查询结束"}`;
  document.getElementById("message").textContent = state.message;
  for (const action of ["retry", "skip"])
    document.getElementById(action).disabled = state.saving || state.finished;
});
