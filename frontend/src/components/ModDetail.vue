<script setup lang="ts">
import { t, locale, setLocale } from '../i18n';
import { computed } from 'vue';
import type { PageSnapshot } from '../domain/models';
import { localPath } from '../domain/navigation';
import { send } from '../bridge/host';
import { workspace, navigate, openTab, refresh } from '../state/workspace';
import ModCard from './ModCard.vue';
import { overviewItems, openItem } from '../state/workspace';
import CollectionPicker from './CollectionPicker.vue';
const props = defineProps<{ page: PageSnapshot }>();
const isCollection = computed(() => props.page.kind === 'collection');
const inCollectionFiles = computed(() => /\/mods\/?$/.test(new URL(props.page.url).pathname));
const files = computed(() => props.page.items.filter((item) => item.kind === 'file'));
const queued = computed(
  () =>
    new Set(
      workspace.host.tasks
        .filter((task) => task.status !== 'cancelled')
        .map((task) => task.file.id),
    ),
);
const covers = computed(() =>
  Object.fromEntries(
    overviewItems.value.map((item) => [localPath(item.url.split('?')[0]), item.image || '']),
  ),
);
function enqueueFile(id: string, start: boolean) {
  send({ action: 'enqueue', ids: [id], start });
  if (start) navigate('/downloads');
}
</script>
<template>
  <div id="detail">
    <div class="detail-actions">
      <template v-if="isCollection"
        ><button :class="{ primary: !inCollectionFiles }" @click="openTab('overview')">{{ t("概览") }}</button
        ><button :class="{ primary: inCollectionFiles }" @click="openTab('mods')">
          {{ t("Mod 清单") }}
        </button></template
      >
      <template v-else
        ><button :class="{ primary: page.kind === 'detail' }" @click="openTab('description')">
          {{ t("介绍") }}</button
        ><button :class="{ primary: page.kind === 'files' }" @click="openTab('files')">
          {{ t("文件") }}<span v-if="files.length"> · {{ files.length }}</span>
        </button></template
      >
      <button @click="send({ action: 'browser' })">{{ t("在 Nexus 查看 ↗") }}</button>
    </div>
    <div v-if="page.description" class="detail-summary">{{ page.description }}</div>
    <p v-else-if="page.kind === 'detail'" class="muted">{{ t("介绍正文正在加载…") }}</p>
    <template v-if="isCollection">
      <CollectionPicker
        v-if="inCollectionFiles && page.collection?.files.length"
        :manifest="page.collection"
        :covers="covers"
      />
      <div v-else-if="inCollectionFiles" class="empty">
        <p>{{ t("正在读取合集指定的文件清单…") }}</p>
        <button @click="refresh">{{ t("重新读取") }}</button
        ><button @click="send({ action: 'browser' })">{{ t("查看原网页 ↗") }}</button>
      </div>
      <div v-else class="collection-entry">
        <button class="primary" @click="openTab('mods')">{{ t("选择合集文件 →") }}</button>
      </div>
      <section v-if="!inCollectionFiles" class="overview-mods">
        <div class="overview-heading">
          <h2>
            {{ t("合集中的 Mod") }} <small v-if="overviewItems.length">{{ overviewItems.length }}</small>
          </h2>
          <button @click="openTab('mods')">{{ t("查看 Mod 清单 →") }}</button>
        </div>
        <div v-if="overviewItems.length" class="cards">
          <ModCard
            v-for="(item, index) in overviewItems"
            :key="item.id"
            :item="item"
            :index="index"
            @open="openItem"
          />
        </div>
        <p v-else class="muted">{{ t("正在读取合集的 Mod 卡片…") }}</p>
      </section>
    </template>
    <template v-else-if="page.kind === 'files'">
      <p v-if="files.length" class="muted">{{ t("按需要选择文件版本，并阅读作者说明。") }}</p>
      <div v-for="file in files" :key="file.id" class="file-row">
        <div class="file-info">
          <small>{{ file.group || t('文件') }}</small>
          <h3>{{ file.name }}</h3>
          <small>{{ file.meta }}</small>
        </div>
        <div class="file-actions">
          <button :disabled="queued.has(file.id)" @click="enqueueFile(file.id, false)">
            {{ queued.has(file.id) ? t('已加入队列') : t('加入队列') }}
          </button>
          <button
            class="primary"
            :disabled="queued.has(file.id)"
            @click="enqueueFile(file.id, true)"
          >
            {{ t("立即下载") }}
          </button>
        </div>
      </div>
      <div v-if="!files.length" class="empty">
        {{ t("正在读取文件…") }}<br /><button @click="send({ action: 'browser' })">{{ t("在原网页查看 ↗") }}</button>
      </div>
    </template>
  </div>
</template>
