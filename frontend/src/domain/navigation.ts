export const NEXUS = 'https://www.nexusmods.com';
export function localPath(raw: string, preferFiles = false): string {
  const url = new URL(raw);
  if (
    url.protocol !== 'https:' ||
    (url.hostname !== 'nexusmods.com' && !url.hostname.endsWith('.nexusmods.com')) ||
    url.username
  )
    throw new Error('请输入 Nexus Mods 的 HTTPS 页面链接。');
  let path = url.pathname.replace(/\/$/, '') || '/games';
  const classic = path.match(/^\/([^/]+)\/mods\/(\d+)$/);
  if (classic) path = `/games/${classic[1]}/mods/${classic[2]}`;
  if (preferFiles && /^\/games\/[^/]+\/collections\/[^/]+$/.test(path)) path += '/mods';
  return path + url.search;
}
export function nexusUrl(path: string): string {
  const url = new URL(path, NEXUS);
  const mod = url.pathname.match(/^\/games\/([^/]+)\/mods\/(\d+)$/);
  if (mod) url.pathname = `/${mod[1]}/mods/${mod[2]}`;
  return url.href;
}
export function parentPath(path: string): string | null {
  const url = new URL(path, NEXUS);
  const mod = url.pathname.match(/^\/games\/([^/]+)\/mods\/(\d+)$/);
  if (mod && url.searchParams.get('tab') === 'files') return url.pathname;
  if (mod) return `/games/${mod[1]}/mods`;
  const collection = url.pathname.match(
    /^(\/games\/([^/]+)\/collections\/[^/]+)(?:\/(mods|comments|changelog|bugs))?$/,
  );
  if (collection) return collection[3] ? collection[1] : `/games/${collection[2]}/collections`;
  if (/^\/games\/[^/]+\/(mods|collections)$/.test(url.pathname)) return '/games';
  return null;
}
export function gameDomain(path: string): string | null {
  return path.match(/^\/games\/([^/?#]+)/)?.[1] || null;
}
export interface Crumb {
  label: string;
  path: string;
}
export function breadcrumbs(path: string, gameName: string, title: string): Crumb[] {
  const game = gameDomain(path);
  const result: Crumb[] = [{ label: '游戏库', path: '/games' }];
  if (!game) return result;
  const collection = path.includes('/collections');
  result.push({
    label: gameName || game,
    path: `/games/${game}/${collection ? 'collections' : 'mods'}`,
  });
  const url = new URL(path, NEXUS);
  if (/\/mods\/\d+$/.test(url.pathname)) {
    result.push({ label: title || 'Mod 详情', path: url.pathname });
    if (url.searchParams.get('tab') === 'files') result.push({ label: '文件', path });
  } else if (/\/collections\/[^/]+/.test(url.pathname)) {
    const root = url.pathname.match(/^(.+\/collections\/[^/]+)/)![1];
    result.push({ label: title || '合集', path: root });
    if (url.pathname.endsWith('/mods')) result.push({ label: 'Mod 清单', path });
  }
  return result;
}
