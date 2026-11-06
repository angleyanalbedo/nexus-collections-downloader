import { computed, reactive, ref } from 'vue';
import type { Router } from 'vue-router';
import { send, subscribe } from '../bridge/host';
import type { HostState, PageSnapshot, SiteItem } from '../domain/models';
import { gameDomain, localPath, nexusUrl, parentPath } from '../domain/navigation';

const emptyHost: HostState = {
  type: 'state',
  tasks: [],
  snapshot: null,
  busy: false,
  queueEnabled: false,
  output: '',
  message: '正在连接桌面服务…',
  siteUrl: '',
  browserVisible: false,
};
export const workspace = reactive({
  host: emptyHost,
  canBack: false,
  canForward: false,
  lastBrowse: '/games',
  linkError: '',
});
export const currentPath = ref('/games');
const pages = reactive(new Map<string, PageSnapshot>());
const gameNames = reactive(new Map<string, string>());
const lists = new Map<string, string>();
let router: Router;
let initialized = false;
let hostOwnsNavigation = false;
let diagnostics = false;
declare global {
  interface Window {
    __ASH_NAV_TRACE__?: { action: string; path: string; current: string }[];
  }
}
function trace(action: string, path: string) {
  if (diagnostics) {
    window.__ASH_NAV_TRACE__ ??= [];
    window.__ASH_NAV_TRACE__.push({ action, path, current: currentPath.value });
    window.__ASH_NAV_TRACE__ = window.__ASH_NAV_TRACE__.slice(-60);
  }
}
const suppressed = new Set<string>();

export const currentPage = computed(() => pages.get(currentPath.value) || null);
export const currentGame = computed(() =>
  gameDomain(currentPath.value === '/downloads' ? workspace.lastBrowse : currentPath.value),
);
export const currentGameName = computed(
  () => gameNames.get(currentGame.value || '') || currentGame.value || '',
);
export const pendingTasks = computed(() =>
  workspace.host.tasks.filter((task) => !['completed', 'cancelled'].includes(task.status)),
);

export const overviewItems = computed(() => {
  const root = currentPath.value.split('?')[0].replace(/\/mods$/, '');
  if (!/^\/games\/[^/]+\/collections\/[^/]+$/.test(root)) return [];
  const ownItems = currentPage.value?.items.filter((item) => item.kind === 'mod') || [];
  if (ownItems.length) return ownItems;
  const revision = currentPage.value?.collection?.revision;
  for (const [path, page] of pages) {
    if (
      path.split('?')[0] === `${root}/mods` &&
      page.collection?.complete &&
      (!revision || page.collection.revision === revision)
    )
      return page.items.filter((item) => item.kind === 'mod');
  }
  return [];
});
const requestedManifests = new Set<string>();
function prefetchOverviewManifest() {
  if (workspace.host.busy || workspace.host.tasks.some((task) => task.status === 'waiting')) return;
  const page = currentPage.value;
  const root = currentPath.value.split('?')[0];
  if (
    !page ||
    page.kind !== 'collection' ||
    page.status !== 'ready' ||
    !/^(\/games\/[^/]+\/collections\/[^/]+)$/.test(root) ||
    overviewItems.value.length
  )
    return;
  const key = `${root}:${page.collection?.revision || ''}`;
  if (requestedManifests.has(key)) return;
  requestedManifests.add(key);
  send({ action: 'browse', url: nexusUrl(`${root}/mods`) });
}
function requestPage(path: string) {
  if (path === '/downloads') return;
  send({ action: 'browse', url: nexusUrl(path) });
}
function normalize(path: string): string {
  return router.resolve(path).fullPath;
}
function replaceFromHost(path: string) {
  trace('host', path);
  if (path === currentPath.value) return;
  suppressed.add(path);
  void router.replace(path);
}
function acceptSnapshot(page: PageSnapshot) {
  let path: string;
  try {
    path = normalize(localPath(page.url));
  } catch {
    return;
  }
  pages.set(path, page);
  if (page.kind === 'games') {
    for (const item of page.items) {
      const domain = gameDomain(new URL(item.url).pathname);
      if (domain) gameNames.set(domain, item.name);
    }
  }
  if (['games', 'mods', 'collections'].includes(page.kind)) lists.set(path.split('?')[0], path);
  if (currentPath.value === '/downloads') return;
  const samePage = path.split('?')[0] === currentPath.value.split('?')[0];
  if (hostOwnsNavigation || samePage) replaceFromHost(path);
}
export function installWorkspace(appRouter: Router): () => void {
  router = appRouter;
  router.afterEach((to, _from, failure) => {
    if (failure) return;
    trace('route', to.fullPath);
    currentPath.value = to.fullPath;
    workspace.canBack = Boolean(router.options.history.state.back);
    workspace.canForward = Boolean(router.options.history.state.forward);
    if (to.path !== '/downloads') workspace.lastBrowse = to.fullPath;
    if (suppressed.delete(to.fullPath) || !initialized) return;
    hostOwnsNavigation = false;
    requestPage(to.fullPath);
  });
  const unsubscribe = subscribe((state) => {
    workspace.host = state;
    if (!initialized) {
      initialized = true;
      hostOwnsNavigation = state.navigationManagedByHost || false;
      diagnostics = hostOwnsNavigation;
      let initial = '/games';
      try {
        initial = normalize(
          localPath(state.initialUrl || state.snapshot?.url || nexusUrl('/games'), true),
        );
      } catch {
        /* Keep the game library as the fallback. */
      }
      suppressed.add(initial);
      void router.replace(initial).then(() => {
        suppressed.delete(initial);
        if (!hostOwnsNavigation) requestPage(initial);
      });
    }
    if (state.snapshot) acceptSnapshot(state.snapshot);
    prefetchOverviewManifest();
  });
  send({ action: 'ready' });
  return unsubscribe;
}
export function navigate(path: string) {
  void router.push(path);
}
export function back() {
  router.back();
}
export function forward() {
  router.forward();
}
export function up() {
  trace('up', currentPath.value);
  if (currentPath.value === '/downloads') return navigate(workspace.lastBrowse);
  const parent = parentPath(currentPath.value);
  if (parent) navigate(lists.get(parent) || parent);
}
export function openItem(item: SiteItem) {
  const path = localPath(item.url, true);
  if (item.kind === 'game') {
    const domain = gameDomain(path);
    if (domain) {
      gameNames.set(domain, item.name);
      navigate(`/games/${domain}/mods`);
    }
  } else navigate(path);
}
export function openLink(raw: string) {
  try {
    workspace.linkError = '';
    navigate(localPath(raw, true));
  } catch (error) {
    workspace.linkError = error instanceof Error ? error.message : '链接无法识别。';
  }
}
export function openTab(tab: string) {
  const url = new URL(nexusUrl(currentPath.value));
  const collection = url.pathname.match(/^(.+\/collections\/[^/]+)/);
  if (collection) {
    url.pathname = collection[1] + (tab === 'mods' ? '/mods' : '');
    url.search = '';
  } else {
    if (tab === 'files') url.searchParams.set('tab', 'files');
    else url.searchParams.delete('tab');
  }
  navigate(localPath(url.href));
}
export function search(query: string) {
  const game = currentGame.value || '';
  const scope = !game
    ? 'games'
    : currentPath.value.includes('/collections')
      ? 'collections'
      : 'mods';
  send({ action: 'search', scope, game, query });
}
export function refresh() {
  requestPage(currentPath.value);
}
