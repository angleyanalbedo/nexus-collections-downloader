import { createRouter, createWebHashHistory } from 'vue-router';
import BrowsePage from './pages/BrowsePage.vue';
import DownloadsPage from './pages/DownloadsPage.vue';
export const router = createRouter({
  history: createWebHashHistory(),
  routes: [
    { path: '/', redirect: '/games' },
    { path: '/games', component: BrowsePage },
    { path: '/games/:game/mods', component: BrowsePage },
    { path: '/games/:game/mods/:mod', component: BrowsePage },
    { path: '/games/:game/collections', component: BrowsePage },
    { path: '/games/:game/collections/:collection/:section?', component: BrowsePage },
    { path: '/downloads', component: DownloadsPage },
    { path: '/:pathMatch(.*)*', redirect: '/games' },
  ],
  scrollBehavior(to, from, savedPosition) {
    return savedPosition || { top: Number(to.meta.scrollTop || 0) };
  },
});
router.beforeEach((_to, from) => {
  from.meta.scrollTop = window.scrollY;
});
