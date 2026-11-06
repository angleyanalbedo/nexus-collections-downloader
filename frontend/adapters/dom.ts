/** Read-only access to the public Mod resource attached to a rendered Nexus row. */
export interface NexusResource {
  modId: number;
  name: string;
  game: { domainName: string };
  pictureUrl?: string;
  category?: string;
  uploader?: { name?: string };
  files?: {
    fileId: number;
    name?: string;
    version?: string;
    sizeInBytes?: string | number;
    optional?: boolean;
  }[];
}
interface ReactFiber {
  memoizedProps?: { resource?: NexusResource };
  return?: ReactFiber;
}
export function modResource(node: Element): NexusResource | null {
  const property = Object.keys(node).find((key) => key.startsWith('__reactFiber'));
  let fiber = property ? (node as unknown as Record<string, ReactFiber>)[property] : undefined;
  for (let depth = 0; depth < 8 && fiber; depth++, fiber = fiber.return) {
    const resource = fiber.memoizedProps?.resource;
    if (
      resource &&
      /^\d+$/.test(String(resource.modId)) &&
      resource.name &&
      /^[a-z0-9]+$/.test(resource.game?.domainName)
    )
      return resource;
  }
  return null;
}
export function text(node: Element | null | undefined): string {
  return ((node as HTMLElement | null)?.innerText || node?.textContent || '')
    .replace(/\s+/g, ' ')
    .trim();
}
export function nexusLink(raw: string | null, base: string): string {
  try {
    const url = new URL(raw || '', base);
    if (
      url.protocol !== 'https:' ||
      url.username ||
      (url.hostname !== 'nexusmods.com' && !url.hostname.endsWith('.nexusmods.com')) ||
      url.searchParams.has('key') ||
      url.searchParams.has('expires')
    )
      return '';
    return url.href;
  } catch {
    return '';
  }
}
export function coverUrl(raw?: string): string {
  try {
    const url = new URL(raw || '');
    return url.protocol === 'https:' ? url.href : '';
  } catch {
    return '';
  }
}
export function imageSource(image: HTMLImageElement | null, base: string): string {
  if (!image) return '';
  const candidates = [
    image.getAttribute('data-src'),
    image.getAttribute('data-original'),
    image.currentSrc,
    image.getAttribute('src'),
    ...(image.getAttribute('data-srcset') || image.getAttribute('srcset') || '')
      .split(',')
      .map((value) => value.trim().split(/\s+/)[0]),
  ];
  for (const candidate of candidates) {
    if (!candidate) continue;
    try {
      const url = new URL(candidate, base);
      if (url.protocol === 'https:' && !/\.svg(?:[?#]|$)|placeholder/i.test(url.href))
        return url.href;
    } catch {
      /* Try the next source. */
    }
  }
  return '';
}
