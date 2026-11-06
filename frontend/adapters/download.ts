declare global {
  interface Window {
    __ashDownloadClicks?: WeakSet<Element>;
  }
}

export interface DownloadRequest {
  fileId: string;
  pagePath: string;
}

export interface DownloadResult {
  step:
    | 'invalid-file'
    | 'challenge'
    | 'waiting'
    | 'clicked-slow'
    | 'clicked-manual'
    | 'countdown'
    | 'found-url'
    | 'blocked'
    | 'failed';
  url?: string;
  error?: string;
}

const REQUEST_TIMEOUT_MS = 12_000;

function result(data: DownloadResult): string {
  return JSON.stringify(data);
}

function visible(element: HTMLElement | null | undefined): element is HTMLElement {
  return Boolean(
    element &&
      element.getClientRects().length &&
      !(element as HTMLButtonElement).disabled &&
      element.getAttribute('aria-disabled') !== 'true' &&
      !element.matches('.disabled,[disabled]'),
  );
}

function parseGameMod(pagePath: string): { game: string; mod: string } | null {
  const match = /^\/([a-z0-9]+)\/mods\/(\d+)(?:\/|$)/i.exec(pagePath);
  return match ? { game: match[1], mod: match[2] } : null;
}

function numericGameIdFromDom(): string | null {
  for (const element of document.querySelectorAll<HTMLElement>('[data-game-id]')) {
    const value = element.getAttribute('data-game-id');
    if (value && /^\d+$/.test(value)) return value;
  }
  const html = document.documentElement.outerHTML.slice(0, 800_000);
  const match = /data-game-id\s*=\s*["'](\d+)["']/i.exec(html);
  return match ? match[1] : null;
}

function extractUrl(value: unknown): string | null {
  if (typeof value === 'string' && /^https?:\/\//i.test(value)) return value;
  if (Array.isArray(value)) {
    for (const item of value) {
      const url = extractUrl(item);
      if (url) return url;
    }
    return null;
  }
  if (value && typeof value === 'object') {
    const record = value as Record<string, unknown>;
    for (const key of ['url', 'URL', 'URI', 'link', 'src', 'download_url', 'downloadUrl']) {
      const url = extractUrl(record[key]);
      if (url) return url;
    }
    for (const item of Object.values(record)) {
      const url = extractUrl(item);
      if (url) return url;
    }
  }
  return null;
}

export function detectBlockedText(value: string): string | null {
  const text = value.replace(/\s+/g, ' ');
  if (
    /archived version of this mod|no longer supported|(?:file|version).{0,30}(?:archived|retired|removed|deleted)|download (?:was|has been) (?:disabled|removed)/i.test(
      text,
    )
  )
    return '该文件已归档或作者关闭了下载入口，无法自动下载。';
  if (
    /(?:premium|supporter).{0,40}(?:required|only|exclusive)|(?:requires|need|needs).{0,30}(?:premium|supporter)/i.test(
      text,
    )
  )
    return '该文件仅限 Premium 账户下载，普通账户无法自动获取链接。';
  return null;
}

function blockedText(): string | null {
  return detectBlockedText(document.body?.innerText || '');
}

async function fetchWithTimeout(input: RequestInfo | URL, init: RequestInit = {}): Promise<Response> {
  const controller = new AbortController();
  const timeout = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);
  try {
    return await fetch(input, { ...init, signal: controller.signal });
  } finally {
    window.clearTimeout(timeout);
  }
}

async function postGenerateDownloadUrl(fileId: string, gameId: string): Promise<string | null> {
  try {
    const response = await fetchWithTimeout(
      'https://www.nexusmods.com/Core/Libs/Common/Managers/Downloads?GenerateDownloadUrl',
      {
        method: 'POST',
        credentials: 'include',
        headers: {
          'Content-Type': 'application/x-www-form-urlencoded; charset=UTF-8',
          'X-Requested-With': 'XMLHttpRequest',
          Accept: 'application/json, text/javascript, */*; q=0.01',
        },
        body: `fid=${encodeURIComponent(fileId)}&game_id=${encodeURIComponent(gameId)}`,
      },
    );
    if (!response.ok) return null;
    const contentType = response.headers.get('content-type') || '';
    if (contentType.includes('application/json') || contentType.includes('text/javascript')) {
      return extractUrl(await response.json());
    }
    const body = await response.text();
    return extractUrl(body) || extractFromHtml(body);
  } catch {
    return null;
  }
}

function extractFromHtml(html: string): string | null {
  const patterns = [
    /https:\/\/filedelivery\.nexusmods\.com\/[^\s"'<>\\)]+/i,
    /https:\/\/[^\s"'<>\\)]*nexus-cdn\.com\/[^\s"'<>\\)]+/i,
    /https:\/\/[^\s"'<>\\)]*cf-files\.nexusmods\.com\/[^\s"'<>\\)]+/i,
    /https:\/\/[^\s"'<>\\)]+\?[^\s"'<>\\)]*key=[^\s"'<>\\)]*expires=[^\s"'<>\\)]+/i,
    /https:\/\/[^\s"'<>\\)]+\?[^\s"'<>\\)]*expires=[^\s"'<>\\)]*key=[^\s"'<>\\)]+/i,
    /data-(?:url|link|download)=["'](https:\/\/[^"'\s]+)/i,
    /https:\/\/[^\s"'<>\\)]+\.(?:zip|rar|7z|pak|ba2|esm|esp)[^\s"'<>\\)]*/i,
  ];
  for (const pattern of patterns) {
    const match = pattern.exec(html);
    if (match?.[1] || match?.[0]) {
      const value = (match[1] || match[0] || '').trim().replace(/[)"'\s]+$/, '');
      if (/^https?:\/\//i.test(value)) return value;
    }
  }
  return null;
}

function scrapeCurrentPage(): string | null {
  return extractFromHtml(document.documentElement.outerHTML) || extractFromHtml(document.body?.innerText || '');
}

export default function advanceDownload({ fileId, pagePath }: DownloadRequest): Promise<string> {
  if (!/^\d+$/.test(fileId)) return Promise.resolve(result({ step: 'invalid-file' }));
  if (/just a moment|verify you are human|checking your browser/i.test(document.title))
    return Promise.resolve(result({ step: 'challenge' }));

  const current = new URL(location.href);
  if (current.pathname !== pagePath) return Promise.resolve(result({ step: 'waiting' }));

  const blocked = blockedText();
  if (blocked) return Promise.resolve(result({ step: 'blocked', error: blocked }));

  const gameMod = parseGameMod(pagePath);
  if (!gameMod) return Promise.resolve(result({ step: 'failed', error: '无法从页面路径识别游戏和 Mod。' }));
  const gameId = numericGameIdFromDom() || gameMod.game;

  return (async () => {
    const viaAjax = await postGenerateDownloadUrl(fileId, gameId);
    if (viaAjax) return result({ step: 'found-url', url: viaAjax });

    const blockedAfterRequest = blockedText();
    if (blockedAfterRequest) return result({ step: 'blocked', error: blockedAfterRequest });

    const pinned = current.searchParams.get('file_id') === fileId;
    const slow =
      document.querySelector<HTMLElement>(
        '#slowDownloadButton,[data-testid="slow-download-button"]',
      ) ||
      [...document.querySelectorAll<HTMLElement>('button,a')].find((element) =>
        /^slow download$/i.test(element.textContent?.trim() || ''),
      );
    window.__ashDownloadClicks ??= new WeakSet();
    if (pinned && visible(slow)) {
      if (window.__ashDownloadClicks.has(slow)) return result({ step: 'countdown' });
      window.__ashDownloadClicks.add(slow);
      slow.click();
      return result({ step: 'clicked-slow' });
    }
    const row = document.querySelector(`[data-id="${fileId}"]`);
    const manual =
      row &&
      [...row.querySelectorAll<HTMLAnchorElement>('a[href]')].find(
        (anchor) =>
          /manual/i.test(anchor.textContent || '') &&
          new URL(anchor.href).searchParams.get('file_id') === fileId,
      );
    if (visible(manual) && !window.__ashDownloadClicks.has(manual)) {
      window.__ashDownloadClicks.add(manual);
      manual.click();
      return result({ step: 'clicked-manual' });
    }
    const scraped = scrapeCurrentPage();
    if (scraped) return result({ step: 'found-url', url: scraped });
    return result({ step: 'waiting' });
  })();
}
