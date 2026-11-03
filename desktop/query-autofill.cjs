// Only use the site's ordinary query button; challenges stay in the visible page.
function autofillScript(expected) {
  return `(() => {
    const expected = ${JSON.stringify(expected)};
    return new Promise(resolve => {
      let filled = false, ready = false, settled = false, observer, poll, timeout;
      function finish(result) {
        if (settled) return;
        settled = true;
        observer?.disconnect(); clearInterval(poll); clearTimeout(timeout);
        resolve(result);
      }
      function attempt() {
        if (settled) return;
        const input = document.querySelector('.search-input input') ||
          [...document.querySelectorAll('input')].find(i => /玩家|昵称|player|nickname/i.test(i.placeholder));
        if (!input || input.disabled || input.readOnly) return;
        if (!filled) {
          Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value').set.call(input, expected);
          input.dispatchEvent(new Event('input', { bubbles: true }));
          input.dispatchEvent(new Event('change', { bubbles: true }));
          filled = input.value === expected;
          // Let React/Vue commit the updated query before submitting.
          if (filled) setTimeout(() => { ready = true; attempt(); }, 100);
          return;
        }
        if (!ready) return;
        if (input.value !== expected) return finish('filled');
        const scope = input.closest('form') || document;
        const button = [...scope.querySelectorAll('button')].find(b =>
          /^(查询|搜索|search|query)$/i.test(b.textContent.trim()) &&
          !b.disabled && b.getAttribute('aria-disabled') !== 'true' && b.getClientRects().length);
        if (!button) return;
        finish('submitted');
        button.click();
      }
      observer = new MutationObserver(attempt);
      observer.observe(document.documentElement, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled', 'aria-disabled'] });
      poll = setInterval(attempt, 250);
      timeout = setTimeout(() => finish(filled ? 'filled' : 'missing'), 30000);
      attempt();
    });
  })()`;
}
module.exports = { autofillScript };
