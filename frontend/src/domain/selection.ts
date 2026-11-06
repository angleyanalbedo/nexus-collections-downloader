import type { CollectionManifest, SiteItem } from './models';
export interface FileGroup {
  id: string;
  name: string;
  files: SiteItem[];
}
export function groupFiles(files: SiteItem[]): FileGroup[] {
  const groups = new Map<string, FileGroup>();
  for (const file of files) {
    const id = file.group || file.url.split('?')[0];
    if (!groups.has(id)) groups.set(id, { id, name: file.modName || file.name, files: [] });
    groups.get(id)!.files.push(file);
  }
  return [...groups.values()];
}
export function defaultSelection(manifest: CollectionManifest): Set<string> {
  return new Set(manifest.files.filter((file) => file.optional !== true).map((file) => file.id));
}
export function setSelected(current: Set<string>, ids: string[], checked: boolean): Set<string> {
  const next = new Set(current);
  for (const id of ids) {
    if (checked) next.add(id);
    else next.delete(id);
  }
  return next;
}
export function selectionSummary(manifest: CollectionManifest, selected: Set<string>) {
  const files = manifest.files.filter((file) => selected.has(file.id));
  return {
    files,
    count: files.length,
    size: files.reduce((sum, file) => sum + (file.size || 0), 0),
  };
}
