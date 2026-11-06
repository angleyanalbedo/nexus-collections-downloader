import type { PageKind, SiteItem } from '../src/domain/models';
import { modResource, coverUrl, imageSource, nexusLink, text } from './dom';
function cardContainer(anchor: HTMLAnchorElement, href: string, base: string): Element {
  const known = anchor.closest(
    '.mod-tile,.mod-tile-left,[data-testid*="card"],[data-testid*="tile"],article,li,tr',
  );
  if (known?.querySelector('img:not([data-testid="user-avatar"])')) return known;
  let node: Element | null = anchor;
  for (let depth = 0; depth < 5 && node; depth++, node = node.parentElement) {
    if (!node.querySelector('img:not([data-testid="user-avatar"])')) continue;
    const targets = [...node.querySelectorAll<HTMLAnchorElement>('a[href]')]
      .map((link) => nexusLink(link.getAttribute('href'), base).split('?')[0])
      .filter((link) => /\/mods\/\d+/.test(link));
    if (targets.length && targets.every((link) => link === href.split('?')[0])) return node;
  }
  return known || anchor;
}
export function readCards(doc: Document, base: string, kind: PageKind): SiteItem[] {
  // A collection overview's incidental links are not its download manifest.
  if (!['games', 'mods', 'collections'].includes(kind)) return [];
  const items = new Map<string, SiteItem>();
  for (const anchor of doc.querySelectorAll<HTMLAnchorElement>('a[href]')) {
    const href = nexusLink(anchor.getAttribute('href'), base);
    if (!href) continue;
    const mod = href.match(/\/(?:games\/)?([^/?#]+)\/mods\/(\d+)(?:[/?#]|$)/);
    const collection = href.match(/\/games\/([^/?#]+)\/collections\/([^/?#]+)/);
    const game = href.match(/\/games\/([^/?#]+)\/?(?:[?#].*)?$/);
    let id: string;
    let itemKind: SiteItem['kind'];
    if (kind === 'games' && game) {
      id = game[1];
      itemKind = 'game';
    } else if (kind === 'mods' && mod) {
      id = `${mod[1]}:${mod[2]}`;
      itemKind = 'mod';
    } else if (kind === 'collections' && collection) {
      id = `${collection[1]}:${collection[2]}`;
      itemKind = 'collection';
    } else continue;
    const tile = cardContainer(anchor, href, base);
    const name =
      text(tile.querySelector('.tile-name a,.tile-name,h3,h2')) ||
      text(anchor) ||
      anchor.querySelector('img')?.alt;
    if (!name || name.length > 220 || /^\d+$/.test(name)) continue;
    const resource = modResource(anchor);
    const resourceMatches = resource && id === `${resource.game.domainName}:${resource.modId}`;
    const image =
      imageSource(tile.querySelector('img:not([data-testid="user-avatar"])'), base) ||
      (resourceMatches ? coverUrl(resource.pictureUrl) : '');
    const existing = items.get(id);
    if (existing) {
      if (!existing.image && image) existing.image = image;
      continue;
    }
    items.set(id, {
      id,
      name,
      url: href.split('#')[0],
      kind: itemKind,
      image,
      meta:
        text(tile.querySelector('.tile-author,.byline,[data-testid*="author"],p')) ||
        text(tile).replace(name, '').slice(0, 200),
    });
  }
  return [...items.values()];
}
