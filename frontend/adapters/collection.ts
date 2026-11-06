import type { CollectionManifest, SiteItem } from '../src/domain/models';
import { modResource, coverUrl, text } from './dom';
export function readCollection(
  doc: Document,
  pageUrl: string,
  title: string,
): { items: SiteItem[]; manifest: CollectionManifest } {
  const url = new URL(pageUrl);
  const root = url.pathname.match(/^(.+\/collections\/[^/]+)/)?.[1] || url.pathname;
  const rows = [...doc.querySelectorAll('tr.collection-mod-row')];
  const items = new Map<string, SiteItem>();
  const files = new Map<string, SiteItem>();
  const revision = text(doc.body).match(/Revision\s+(\d+)/i)?.[1] || '';
  for (const row of rows) {
    const resource = modResource(row);
    if (!resource) continue;
    const key = `${resource.game.domainName}:${resource.modId}`;
    const href = `https://www.nexusmods.com/${resource.game.domainName}/mods/${resource.modId}`;
    items.set(key, {
      id: key,
      name: resource.name,
      url: href,
      image: coverUrl(resource.pictureUrl),
      meta: [resource.uploader?.name, resource.category].filter(Boolean).join(' · '),
      kind: 'mod',
    });
    for (const file of resource.files || []) {
      const fileId = String(file.fileId || '');
      if (!/^\d+$/.test(fileId)) continue;
      const page = `${href}?tab=files&file_id=${fileId}`;
      const id = `${key}:${fileId}`;
      files.set(id, {
        id,
        modName: resource.name,
        group: key,
        name: file.name || `文件 ${fileId}`,
        version: file.version || '',
        size: Number(file.sizeInBytes) || 0,
        optional: typeof file.optional === 'boolean' ? file.optional : null,
        url: page,
        fileId,
        downloadUrl: `${page}&nmm=0`,
        kind: 'file',
        collectionName: title,
        revision,
      });
    }
  }
  const modTab = [...doc.querySelectorAll<HTMLAnchorElement>('a[href]')].find((anchor) => {
    try {
      return new URL(anchor.href, pageUrl).pathname === `${root}/mods` && /\d/.test(text(anchor));
    } catch {
      return false;
    }
  });
  const expected =
    Number(
      text(modTab)
        .match(/(\d[\d,]*)/)?.[1]
        ?.replace(/,/g, ''),
    ) || 0;
  const fileList = [...files.values()];
  const complete =
    expected > 0 && expected === files.size && rows.length === items.size && revision !== '';
  return {
    items: [...items.values()],
    manifest: {
      key: `${root}:${revision}:${fileList
        .map((file) => `${file.id}@${file.version}`)
        .sort()
        .join('|')}`,
      revision,
      expectedFiles: expected,
      complete,
      files: fileList,
      message: complete
        ? ''
        : rows.length
          ? '正在核对合集文件清单…'
          : '请进入 Mod 清单读取合集文件。',
    },
  };
}
