<script setup lang="ts">
import { t, locale, setLocale } from '../i18n';
import { computed } from 'vue';
import { useRoute } from 'vue-router';
import { send } from '../bridge/host';
import {
  workspace,
  currentGame,
  currentGameName,
  pendingTasks,
  navigate,
} from '../state/workspace';
const route = useRoute();
const inDownloads = computed(() => route.path === '/downloads');
</script>
<template>
  <aside class="sidebar">
    <div class="brand">⋀ ASH</div>
    <small>YOUR GAME. REIMAGINED.</small>
    <nav :aria-label="t('主导航')">
      <button data-home="games" :class="{ active: !inDownloads }" @click="navigate('/games')">
        ▦ 游戏库
      </button>
      <button
        data-view="downloads"
        :class="{ active: inDownloads }"
        @click="navigate('/downloads')"
      >
        ↓ 下载 <span>{{ pendingTasks.length }}</span>
      </button>
    </nav>
    <div class="side-label">{{ t("当前游戏") }}</div>
    <button id="currentGame" @click="navigate('/games')">
      {{ currentGameName || t('选择游戏') }} ⇄
    </button>
    <div v-if="currentGame" id="gameNav">
      <button
        :class="{ active: route.path.includes('/mods') }"
        @click="navigate(`/games/${currentGame}/mods`)"
      >
        Mod
      </button>
      <button
        :class="{ active: route.path.includes('/collections') }"
        @click="navigate(`/games/${currentGame}/collections`)"
      >
        {{ t("合集") }}
      </button>
    </div>
    <div class="account">
      <button @click="send({ action: 'login' })">{{ t("登录 / Nexus 账户 ↗") }}</button
      ><button @click="send({ action: 'browser' })">{{ t("打开原网页 ↗") }}</button>
    </div>
  </aside>
</template>
