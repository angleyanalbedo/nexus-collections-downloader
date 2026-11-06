import { describe, expect, it } from 'vitest';
import { snapshot } from '../adapters/snapshot';
import { readCollection } from '../adapters/collection';
const resource = {
  modId: 444,
  name: 'Example Mod',
  game: { domainName: 'phantombladezero' },
  pictureUrl: 'https://staticdelivery.nexusmods.com/cover.png',
  files: [{ fileId: 1328, name: 'Pinned file', version: '1', sizeInBytes: '123' }],
};
function collectionDocument(): Document {
  const row = {
    __reactFiber$fixture: { memoizedProps: {}, return: { memoizedProps: { resource } } },
  };
  const tab = {
    href: 'https://www.nexusmods.com/games/phantombladezero/collections/test/mods',
    textContent: 'Mods 1',
  };
  return {
    body: { textContent: 'Revision 7' },
    querySelectorAll: (selector: string) =>
      selector === 'tr.collection-mod-row' ? [row] : selector === 'a[href]' ? [tab] : [],
  } as unknown as Document;
}
describe('Nexus adapters', () => {
  it('reads text-only collection rows using their matched public Mod resource', () => {
    const result = readCollection(
      collectionDocument(),
      'https://www.nexusmods.com/games/phantombladezero/collections/test/mods',
      'Collection',
    );
    expect(result.items[0].image).toBe(resource.pictureUrl);
    expect(result.manifest).toMatchObject({ complete: true, expectedFiles: 1, revision: '7' });
    expect(result.manifest.files[0]).toMatchObject({ fileId: '1328', version: '1', size: 123 });
  });
  it('does not turn incidental overview Mod links into a manifest', () => {
    const doc = {
      title: 'Overview',
      body: { textContent: 'Revision 7' },
      querySelector: () => null,
      querySelectorAll: () => [],
    } as unknown as Document;
    const result = snapshot(doc, 'https://www.nexusmods.com/games/pbz/collections/test');
    expect(result.items).toEqual([]);
    expect(result.collection?.complete).toBe(false);
  });
  it('preserves paragraphs and long author descriptions', () => {
    const description = 'Install\n\nInstructions\n' + 'Author notes. '.repeat(500);
    const doc = {
      title: 'Mod',
      body: { textContent: 'Mod' },
      querySelector: () => null,
      querySelectorAll: (selector: string) =>
        selector === '.mod_description_container' ? [{ textContent: description }] : [],
    } as unknown as Document;
    const result = snapshot(doc, 'https://www.nexusmods.com/game/mods/1');
    expect(result.description).toBe(description.trim());
    expect(result.status).toBe('ready');
  });
});
