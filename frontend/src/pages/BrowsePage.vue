<script setup lang="ts">
import { t, locale, setLocale } from '../i18n';
import { computed, ref, watch } from 'vue';
import { useRoute } from 'vue-router';
import {
  workspace,
  currentPage,
  currentGame,
  currentGameName,
  navigate,
  openItem,
  search,
  refresh,
} from '../state/workspace';
import { localPath } from '../domain/navigation';
import { send } from '../bridge/host';
import ModCard from '../components/ModCard.vue';
import ModDetail from '../components/ModDetail.vue';
const route = useRoute();
const query = ref('');
watch(
  () => route.fullPath,
  () => {
    query.value = String(route.query.keyword || route.query.title || '');
  },
  { immediate: true },
);
const detailed = computed(
  () =>
    ['detail', 'files', 'collection'].includes(currentPage.value?.kind || '') ||
    /\/(mods\/\d+|collections\/[^/]+)/.test(route.path),
);
const loading = computed(
  () => workspace.host.busy || currentPage.value?.status === 'loading' || !currentPage.value,
);
const title = computed(() =>
  route.path === '/games'
    ? t("游戏库")
    : currentPage.value?.title || currentGameName.value || t("正在加载…"),
);
const scope = computed(() =>
  !currentGame.value ? t("游戏") : route.path.includes('/collections') ? t("合集") : 'Mod',
);
const cards = computed(() =>
  (currentPage.value?.items || []).filter((item) => item.kind !== 'file'),
);
function submit() {
  search(query.value.trim());
}
function clear() {
  query.value = '';
  search('');
}
</script>
<template>
  <section id="browseView">
    <div class="title-row">
      <div>
        <small id="eyebrow">{{ detailed ? 'NEXUS MODS' : t('选择游戏，浏览 MOD 与合集') }}</small>
        <h1 id="title">{{ title }}</h1>
      </div>
      <span v-if="loading" class="muted" role="status">{{ t("正在加载…") }}</span>
    </div>
    <div v-if="currentGame && !detailed" class="tabs">
      <button
        :class="{ active: !route.path.includes('/collections') }"
        @click="navigate(`/games/${currentGame}/mods`)"
      >
        Mod</button
      ><button
        :class="{ active: route.path.includes('/collections') }"
        @click="navigate(`/games/${currentGame}/collections`)"
      >
        {{ t("合集") }}</button
      ><button @click="navigate('/games')">{{ t("切换游戏") }}</button>
    </div>
    <form v-if="!detailed" id="searchForm" @submit.prevent="submit">
      <input
        id="query"
        v-model="query"
        :placeholder="`搜索 Nexus ${scope}`"
        :aria-label="`搜索${scope}`"
      /><button class="primary" :disabled="workspace.host.busy">{{ t("搜索") }}</button
      ><button type="button" :disabled="workspace.host.busy" @click="clear">{{ t("清除") }}</button>
    </form>
    <div class="toolbar">
      <span id="resultCount" class="muted">{{
        currentPage?.resultText || (cards.length ? `${cards.length} 项` : '')
      }}</span
      ><button v-if="currentGame && !detailed" @click="send({ action: 'browser' })">
        {{ t("筛选与排序 ↗") }}</button
      ><button :disabled="workspace.host.busy" @click="refresh">{{ t("刷新") }}</button>
    </div>
    <ModDetail v-if="detailed && currentPage" :page="currentPage" />
    <div v-else-if="!detailed" id="cards" class="cards">
      <ModCard
        v-for="(item, index) in cards"
        :key="item.id"
        :item="item"
        :index="index"
        @open="openItem"
      />
    </div>
    <div v-if="!loading && !detailed && !cards.length" class="empty">
      <p>{{ t("当前页面没有可显示的内容。") }}</p>
      <button @click="refresh">{{ t("重新加载") }}</button
      ><button @click="send({ action: 'browser' })">{{ t("查看 Nexus 页面 ↗") }}</button>
    </div>
    <div class="pagination">
      <button
        v-for="link in currentPage?.navigation || []"
        :key="link.url"
        @click="navigate(localPath(link.url))"
      >
        {{ link.name }}
      </button>
    </div>
  </section>
</template>
