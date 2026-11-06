import { describe, expect, it } from 'vitest';
import { localPath, nexusUrl, parentPath, breadcrumbs } from '../src/domain/navigation';
describe('navigation hierarchy', () => {
  it('opens a collection at its selectable manifest, while preserving explicit overview navigation', () => {
    const url = 'https://www.nexusmods.com/games/phantombladezero/collections/1ebi4o';
    expect(localPath(url, true)).toBe('/games/phantombladezero/collections/1ebi4o/mods');
    expect(localPath(url)).toBe('/games/phantombladezero/collections/1ebi4o');
  });
  it('maps Mod routes back to the real classic website URL', () => {
    const path = localPath('https://www.nexusmods.com/cyberpunk2077/mods/37439?tab=files');
    expect(path).toBe('/games/cyberpunk2077/mods/37439?tab=files');
    expect(nexusUrl(path)).toBe('https://www.nexusmods.com/cyberpunk2077/mods/37439?tab=files');
  });
  it('goes from file tab to Mod to game list to library', () => {
    let path = '/games/cyberpunk2077/mods/37439?tab=files';
    path = parentPath(path)!;
    expect(path).toBe('/games/cyberpunk2077/mods/37439');
    path = parentPath(path)!;
    expect(path).toBe('/games/cyberpunk2077/mods');
    path = parentPath(path)!;
    expect(path).toBe('/games');
    expect(parentPath(path)).toBeNull();
  });
  it('goes from a collection manifest to its overview and collection catalog', () => {
    expect(parentPath('/games/pbz/collections/abc/mods')).toBe('/games/pbz/collections/abc');
    expect(parentPath('/games/pbz/collections/abc')).toBe('/games/pbz/collections');
  });
  it('gives each breadcrumb its own destination', () => {
    expect(
      breadcrumbs('/games/pbz/mods/12?tab=files', 'PBZ', 'Example').map((item) => item.path),
    ).toEqual(['/games', '/games/pbz/mods', '/games/pbz/mods/12', '/games/pbz/mods/12?tab=files']);
  });
  it('rejects lookalike hosts and embedded credentials', () => {
    expect(() => localPath('https://nexusmods.com.evil.test/games')).toThrow();
    expect(() => localPath('https://user@nexusmods.com/games')).toThrow();
  });
});
