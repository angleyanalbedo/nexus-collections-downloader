import type { PageSnapshot, PageKind } from '../src/domain/models';
import { text, nexusLink } from './dom';
import { readCards } from './cards';
import { readCollection } from './collection';
import { readFiles } from './files';
function pageKind(url: URL): PageKind {
  if (/^\/games\/?$/.test(url.pathname)) return 'games';
  if (/\/collections\/[^/]+/.test(url.pathname)) return 'collection';
  if (/\/collections\/?$/.test(url.pathname)) return 'collections';
  if (/\/mods\/\d+/.test(url.pathname))
    return url.searchParams.get('tab') === 'files' ? 'files' : 'detail';
  return 'mods';
}
export function snapshot(doc: Document, href: string): PageSnapshot {
  const url = new URL(href);
  const body = text(doc.body);
  const title = text(doc.querySelector('h1')) || doc.title;
  let status: PageSnapshot['status'] = 'ready';
  if (
    /just a moment|verify you are human|checking your browser|安全验证/i.test(
      doc.title + ' ' + body.slice(0, 1500),
    )
  )
    status = 'challenge';
  else if (url.hostname === 'users.nexusmods.com' || /\/auth\/|\/login/.test(url.pathname))
    status = 'login';
  else if (/page not found|404 not found/i.test(title + ' ' + body.slice(0, 200))) status = 'error';
  else if (!doc.body) status = 'loading';
  const kind = pageKind(url);
  const result: PageSnapshot = {
    title,
    url: href,
    kind,
    status,
    description: '',
    resultText: '',
    items: [],
    navigation: [],
  };
  if (status !== 'ready') return result;
  if (kind === 'collection') {
    const collection = readCollection(doc, href, title);
    result.items = collection.items;
    result.collection = collection.manifest;
  } else if (kind === 'detail' || kind === 'files') result.items = readFiles(doc, href);
  else result.items = readCards(doc, href, kind);
  result.resultText = body.match(/[\d,]+ results/i)?.[0] || '';
  for (const anchor of doc.querySelectorAll<HTMLAnchorElement>('a[href]')) {
    const name = text(anchor),
      link = nexusLink(anchor.getAttribute('href'), href);
    if (!link || !name) continue;
    const target = new URL(link);
    if (
      target.pathname === url.pathname &&
      /^(next|previous|prev|\d{1,4}|下一页|上一页)$/i.test(name) &&
      target.searchParams.has('page') &&
      !result.navigation.some((item) => item.url === link)
    )
      result.navigation.push({ name, url: link });
  }
  if (['detail', 'files', 'collection'].includes(kind)) {
    const selectors = [
      '#mod_description_container',
      '.mod_description_container',
      '.mod_description',
      '#description .container',
      '[data-testid="mod-description"]',
      '[data-testid="collection-summary"]',
      '#description',
      '.modpage .description',
    ];
    if (kind === 'collection' && !/\/mods\/?$/.test(url.pathname))
      selectors.push('.collection-page-tabs__tab-content');
    for (const selector of selectors) {
      for (const node of doc.querySelectorAll<HTMLElement>(selector)) {
        const description = (node.innerText || node.textContent || '')
          .replace(/\r\n?/g, '\n')
          .replace(/[^\S\n]+/g, ' ')
          .replace(/ *\n */g, '\n')
          .replace(/\n{3,}/g, '\n\n')
          .trim();
        if (description) {
          result.description = description;
          break;
        }
      }
      if (result.description) break;
    }
  }
  if (kind === 'detail' && !result.description) result.status = 'loading';
  return result;
}
export default function readSnapshot(): string {
  return JSON.stringify(snapshot(document, location.href));
}
