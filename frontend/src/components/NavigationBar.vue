<script setup lang="ts">
import { computed } from 'vue';
import { breadcrumbs, parentPath } from '../domain/navigation';
import {
  workspace,
  currentPath,
  currentPage,
  currentGameName,
  back,
  forward,
  up,
  navigate,
} from '../state/workspace';
defineEmits<{ openLink: [] }>();
const crumbs = computed(() =>
  currentPath.value === '/downloads'
    ? [{ label: '下载队列', path: '/downloads' }]
    : breadcrumbs(currentPath.value, currentGameName.value, currentPage.value?.title || ''),
);
const hasParent = computed(
  () => currentPath.value === '/downloads' || Boolean(parentPath(currentPath.value)),
);
</script>
<template>
  <header>
    <div class="navigation-buttons">
      <button
        aria-label="返回"
        title="返回（Alt + ←）"
        :disabled="!workspace.canBack"
        @click="back"
      >
        ←
      </button>
      <button
        aria-label="前进"
        title="前进（Alt + →）"
        :disabled="!workspace.canForward"
        @click="forward"
      >
        →
      </button>
      <button aria-label="上一级" title="上一级（Alt + ↑）" :disabled="!hasParent" @click="up">
        ↑
      </button>
    </div>
    <nav class="breadcrumbs" aria-label="页面位置">
      <template v-for="(crumb, index) in crumbs" :key="crumb.path"
        ><span v-if="index">/</span
        ><button
          :aria-current="index === crumbs.length - 1 ? 'page' : undefined"
          @click="navigate(crumb.path)"
        >
          {{ crumb.label }}
        </button></template
      >
    </nav>
    <button class="header-link" @click="$emit('openLink')">打开 Nexus 链接 ↗</button>
  </header>
</template>
