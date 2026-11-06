export interface SearchRequest {
  scope: 'games' | 'mods' | 'collections';
  query: string;
}
export default function searchPage({ scope, query }: SearchRequest): boolean {
  const inputs = [...document.querySelectorAll<HTMLInputElement>('input')].filter(
    (input) => !input.disabled && input.type !== 'hidden' && input.getClientRects().length > 0,
  );
  let input =
    scope === 'games'
      ? inputs.find((candidate) => candidate.id === 'keyword-game' && candidate.name === 'keyword')
      : scope === 'mods'
        ? inputs.find((candidate) => candidate.name === 'title')
        : undefined;
  if (!input) {
    const candidates = inputs
      .filter((candidate) => !candidate.closest('header,nav,[role="banner"]'))
      .filter((candidate) => {
        const label = [
          candidate.placeholder,
          candidate.name,
          candidate.getAttribute('aria-label'),
          ...[...(candidate.labels || [])].map((node) => node.textContent),
        ].join(' ');
        if (/genre|类型|分类/i.test(label)) return false;
        if (scope === 'games' && /search.*game|game.*search|搜索游戏/i.test(label)) return true;
        if (
          scope === 'collections' &&
          /search.*collection|collection.*search|搜索合集/i.test(label)
        )
          return true;
        return (
          candidate.closest('main,[role="main"]') &&
          (['text', 'search'].includes(candidate.type) || /search|搜索/i.test(label))
        );
      });
    if (candidates.length === 1) input = candidates[0];
  }
  if (!input) return false;
  Object.getOwnPropertyDescriptor(HTMLInputElement.prototype, 'value')!.set!.call(input, query);
  input.dispatchEvent(new Event('input', { bubbles: true }));
  input.dispatchEvent(new Event('change', { bubbles: true }));
  if (input.form) {
    const form = input.form;
    // Let React commit the input state before its Apply handler reads it.
    setTimeout(
      () =>
        form.requestSubmit(
          form.querySelector<HTMLButtonElement>('button[type="submit"]') || undefined,
        ),
      150,
    );
  } else {
    input.dispatchEvent(
      new KeyboardEvent('keydown', {
        key: 'Enter',
        code: 'Enter',
        bubbles: true,
        cancelable: true,
      }),
    );
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter', code: 'Enter', bubbles: true }));
  }
  return true;
}
