(() => {
  const expected = __QUERY_ID_JSON__;
  const state = window.__steamLoginQuery || (window.__steamLoginQuery = { filled: false, submitted: false });
  if (state.submitted) return 'submitted';
  const input = document.querySelector('.search-input input') ||
    [...document.querySelectorAll('input')].find(i => /玩家|昵称|player|nickname/i.test(i.placeholder));
  if (!input || input.disabled || input.readOnly) return 'waiting';
  if (!state.filled) {
    Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, expected);
    input.dispatchEvent(new Event('input', { bubbles: true }));
    input.dispatchEvent(new Event('change', { bubbles: true }));
    state.filled = input.value === expected;
    return state.filled ? 'filled' : 'waiting';
  }
  if (input.value !== expected) return 'manual';
  const scope = input.closest('form') || document;
  const button = [...scope.querySelectorAll('button')].find(b =>
    /^(查询|搜索|search|query)$/i.test(b.textContent.trim()) && !b.disabled &&
    b.getAttribute('aria-disabled') !== 'true' && b.getClientRects().length);
  if (!button) return 'filled';
  state.submitted = true;
  button.click();
  return 'submitted';
})()
