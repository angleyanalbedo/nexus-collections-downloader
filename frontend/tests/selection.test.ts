import { describe, expect, it } from 'vitest';
import type { CollectionManifest, SiteItem } from '../src/domain/models';
import {
  defaultSelection,
  groupFiles,
  setSelected,
  selectionSummary,
} from '../src/domain/selection';
const files: SiteItem[] = [
  {
    id: 'a:1',
    name: 'Old main',
    url: 'https://www.nexusmods.com/game/mods/1',
    kind: 'file',
    modName: 'Same title',
    group: 'a',
    fileId: '1',
    version: 'old',
    size: 10,
  },
  {
    id: 'a:2',
    name: 'Optional',
    url: 'https://www.nexusmods.com/game/mods/1',
    kind: 'file',
    modName: 'Same title',
    group: 'a',
    fileId: '2',
    size: 20,
    optional: true,
  },
  {
    id: 'b:3',
    name: 'Another Mod',
    url: 'https://www.nexusmods.com/game/mods/2',
    kind: 'file',
    modName: 'Same title',
    group: 'b',
    fileId: '3',
    size: 30,
  },
];
const manifest: CollectionManifest = {
  key: 'revision7',
  revision: '7',
  files,
  expectedFiles: 3,
  complete: true,
  message: '',
};
describe('manifest selections', () => {
  it('does not merge different Mods that share a display name', () =>
    expect(groupFiles(files).map((group) => group.files.length)).toEqual([2, 1]));
  it('respects explicit optional flags without inventing flags for unmarked files', () =>
    expect([...defaultSelection(manifest)]).toEqual(['a:1', 'b:3']));
  it('supports group removal and per-file re-selection with exact IDs and versions', () => {
    let selection = new Set(files.map((file) => file.id));
    selection = setSelected(selection, ['a:1', 'a:2'], false);
    expect(selectionSummary(manifest, selection)).toMatchObject({ count: 1, size: 30 });
    selection = setSelected(selection, ['a:1'], true);
    expect(selectionSummary(manifest, selection).files.map((file) => file.fileId)).toEqual([
      '1',
      '3',
    ]);
    expect(selectionSummary(manifest, selection).files[0].version).toBe('old');
  });
});
