import { beforeEach, describe, expect, it, vi } from 'vitest';
import { createMemoryHistory, createRouter } from 'vue-router';
import type { HostCommand } from '../src/domain/models';
const tick = () => new Promise((resolve) => setTimeout(resolve, 0));
describe('workspace navigation integration', () => {
  beforeEach(() => vi.resetModules());
  it('restores browse location after the queue and keeps query filters on parent navigation', async () => {
    const commands: HostCommand[] = [];
    let receive: (event: MessageEvent) => void = () => {};
    vi.stubGlobal('window', {
      scrollY: 0,
      chrome: {
        webview: {
          postMessage: (command: HostCommand) => commands.push(command),
          addEventListener: (_name: string, listener: typeof receive) => {
            receive = listener;
          },
          removeEventListener: () => {},
        },
      },
    });
    const { installWorkspace, navigate, currentPath, up, back } =
      await import('../src/state/workspace');
    const router = createRouter({
      history: createMemoryHistory(),
      routes: [{ path: '/:pathMatch(.*)*', component: {} }],
    });
    await router.push('/games');
    await router.isReady();
    installWorkspace(router);
    receive(
      new MessageEvent('message', {
        data: {
          type: 'state',
          tasks: [],
          snapshot: null,
          busy: false,
          queueEnabled: false,
          output: '',
          message: '',
          siteUrl: '',
          browserVisible: false,
          initialUrl: 'https://www.nexusmods.com/games',
        },
      }),
    );
    await tick();
    navigate('/games/pbz/mods?keyword=blade');
    await tick();
    receive(
      new MessageEvent('message', {
        data: {
          type: 'state',
          tasks: [],
          busy: false,
          snapshot: {
            url: 'https://www.nexusmods.com/games/pbz/mods?keyword=blade',
            kind: 'mods',
            status: 'ready',
            title: 'Mods',
            items: [],
            navigation: [],
            description: '',
            resultText: '',
          },
        },
      }),
    );
    navigate('/games/pbz/mods/12');
    await tick();
    up();
    await tick();
    expect(currentPath.value).toBe('/games/pbz/mods?keyword=blade');
    const before = commands.filter((command) => command.action === 'browse').length;
    navigate('/downloads');
    await tick();
    expect(currentPath.value).toBe('/downloads');
    expect(commands.filter((command) => command.action === 'browse').length).toBe(before);
    back();
    await tick();
    expect(currentPath.value).toBe('/games/pbz/mods?keyword=blade');
    vi.unstubAllGlobals();
  });
});
