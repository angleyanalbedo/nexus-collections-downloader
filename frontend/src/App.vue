<script setup lang="ts">
import { t, locale, setLocale } from './i18n';
import { computed, onMounted, onUnmounted, ref } from 'vue';
import { RouterView, useRoute } from 'vue-router';
import Sidebar from './components/Sidebar.vue';
import NavigationBar from './components/NavigationBar.vue';
import { workspace, pendingTasks, navigate, openLink, back, forward, up } from './state/workspace';
import { send } from './bridge/host';
const route = useRoute();
const showLink = ref(false);
const link = ref('');
const notice = computed(
  () =>
    workspace.host.message &&
    /失败|无法|需要|请在|验证|失效|未就绪|未能|fail|error|required|sign in|expired|verification/i.test(workspace.host.message),
);
function submitLink() {
  openLink(link.value.trim());
  if (!workspace.linkError) showLink.value = false;
}
function keyboard(event: KeyboardEvent) {
  if (!event.altKey) return;
  if (event.key === 'ArrowLeft') {
    event.preventDefault();
    back();
  } else if (event.key === 'ArrowRight') {
    event.preventDefault();
    forward();
  } else if (event.key === 'ArrowUp') {
    event.preventDefault();
    up();
  }
}
onMounted(() => window.addEventListener('keydown', keyboard));
onUnmounted(() => window.removeEventListener('keydown', keyboard));
</script>
<template>
  <div class="workspace-layout">
    <Sidebar />

    <main>
      <NavigationBar @open-link="showLink = true" />
      <form v-if="showLink" id="linkForm" @submit.prevent="submitLink">
        <input
          v-model="link"
          type="url"
          required
          :placeholder="t('粘贴 Nexus 的游戏、Mod 或合集链接')"
          :aria-label="t('Nexus 链接')"
        /><button class="primary">{{ t("打开") }}</button
        ><button type="button" @click="showLink = false">{{ t("取消") }}</button>
      </form>
      <p v-if="workspace.linkError" role="alert">{{ t(workspace.linkError) }}</p>
      <div v-if="notice" id="notice" role="status">
        <span id="message">{{ workspace.host.message }}</span
        ><button @click="send({ action: 'browser' })">{{ t("查看网页 ↗") }}</button>
      </div>
      <RouterView />
      <footer v-if="pendingTasks.length && route.path !== '/downloads'" id="downloadBar">
        <button @click="navigate('/downloads')">
          ↓ {{ pendingTasks.length }} {{ t("个任务 · 查看下载队列") }}</button
        ><button class="primary" @click="send({ action: 'start' })">{{ t("开始队列") }}</button>
      </footer>
    </main>
  </div>
</template>
