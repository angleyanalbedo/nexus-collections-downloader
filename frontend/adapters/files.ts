import type { SiteItem } from '../src/domain/models';
import { nexusLink, text } from './dom';

function clean(value: string): string {
  return value.replace(/\s+/g, ' ').trim();
}

function parseSize(value: string): number {
  const match = /(\d[\d,.]*)\s*(B|KB|MB|GB|TB)/i.exec(value);
  if (!match) return 0;
  const number = Number(match[1].replace(/,/g, ''));
  const unit = match[2].toUpperCase();
  const power = { B: 0, KB: 1, MB: 2, GB: 3, TB: 4 }[unit] ?? 0;
  return Number.isFinite(number) ? Math.round(number * 1024 ** power) : 0;
}

const fileLabels =
  /^(file|files|main files|optional files|old files|miscellaneous files|manual download|mod manager download)$/i;

function fileTitle(row: Element, fileId: string, extra = ''): string {
  for (const selector of [
    '[data-name]',
    '[class*="file-name" i]',
    '[class*="filename" i]',
    'h2',
    'h3',
    'h4',
    'strong',
    'a:not(.btn)',
  ]) {
    const candidate = row.querySelector(selector);
    const value = clean(candidate?.getAttribute('data-name') || text(candidate));
    if (value && !fileLabels.test(value) && !value.includes(fileId)) return value;
  }
  const full = clean(`${text(row)} ${extra}`);
  const lead = clean(
    full.split(
      /\s*(?:Downloaded|Date uploaded|Uploaded|File size|Unique DLs|Total DLs|Version|Manual download|Mod manager download|Preview file contents|Version history)/i,
    )[0],
  );
  return !lead || fileLabels.test(lead) ? `文件 ${fileId}` : lead;
}

export function readFiles(doc: Document, pageUrl: string): SiteItem[] {
  const page = new URL(pageUrl);
  const items = new Map<string, SiteItem>();
  const target = new URL(pageUrl);
  target.search = 'tab=files';

  function add(
    fileId: string | null,
    row: Element | null,
    downloadUrl: string,
    extra = '',
    preferName = false,
  ) {
    if (!fileId || !/^\d+$/.test(fileId)) return;
    const existing = items.get(fileId);
    if (existing && !preferName) return;
    const name = row ? fileTitle(row, fileId, extra) : `文件 ${fileId}`;
    const full = clean(`${row ? text(row) : ''} ${extra}`);
    const sizeText = full.match(/File size\s*([\d.,]+\s*(?:B|KB|MB|GB|TB))/i)?.[0] || '';
    const version = full.match(/Version\s*([A-Za-z0-9._+-]+)/i)?.[1] || '';
    const section = row?.closest('.file-expander,.files-tabs,[id^="file-container-"]');
    const category = section?.querySelector('.file-category-header h2,.file-category-header h3,h2');
    items.set(fileId, {
      id: `${page.pathname}:${fileId}`,
      name,
      url: target.href,
      fileId,
      downloadUrl: downloadUrl || `${target.href}&file_id=${fileId}`,
      group: clean(text(category)),
      meta: full.slice(0, 500),
      version,
      size: parseSize(sizeText),
      kind: 'file',
    });
  }

  const headers: Element[] = [
    ...doc.querySelectorAll('dt.file-expander-header[data-id],.file-expander-header[data-id]'),
  ];
  if (!headers.length) headers.push(...doc.querySelectorAll('.file-expander[data-id]'));
  for (const header of headers) {
    const fileId = header.getAttribute('data-id');
    if (!fileId) continue;
    let sibling: Element | null = header.nextElementSibling;
    let extra = '';
    for (let depth = 0; sibling && depth < 4; depth++, sibling = sibling.nextElementSibling) {
      if (sibling.matches('.file-expander-header[data-id]')) break;
      extra += ` ${text(sibling)}`;
    }
    const scope =
      header.closest('.file-expander,.files-tabs,[id^="file-container-"]') || header.parentElement;
    const anchor = scope?.querySelector<HTMLAnchorElement>(
      `a[href*="file_id=${fileId}"],a[href*="id=${fileId}"]`,
    );
    add(fileId, header, anchor ? nexusLink(anchor.getAttribute('href'), pageUrl) : '', extra, true);
  }

  for (const anchor of doc.querySelectorAll<HTMLAnchorElement>('a[href]')) {
    const href = nexusLink(anchor.getAttribute('href'), pageUrl);
    if (!href) continue;
    const url = new URL(href);
    const fileId = url.searchParams.get('file_id') || url.searchParams.get('id');
    if (
      !fileId ||
      items.has(fileId) ||
      !(/file_id=/.test(href) || /\/Core\/Libs\/Common\/Widgets\/DownloadPopUp/.test(href))
    )
      continue;
    const row = anchor.closest('[data-id],.file-expander-header,dl,article');
    add(fileId, row || anchor.parentElement, href);
  }
  return [...items.values()];
}
