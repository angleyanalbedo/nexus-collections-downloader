<script setup lang="ts">
import { t, locale, setLocale } from '../i18n';
import { computed, ref, watch } from 'vue';
import type { CollectionManifest } from '../domain/models';
import { groupFiles, defaultSelection, setSelected, selectionSummary } from '../domain/selection';
import { formatBytes, imageUrl } from '../domain/format';
import { localPath } from '../domain/navigation';
import { send } from '../bridge/host';
import { navigate } from '../state/workspace';
import { collectionSelections } from '../state/selections';
const props = defineProps<{ manifest: CollectionManifest; covers?: Record<string, string> }>();
const selected = ref(new Set<string>());
watch(
  () => props.manifest.key,
  () => {
    selected.value =
      collectionSelections.get(props.manifest.key) || defaultSelection(props.manifest);
  },
  { immediate: true },
);
watch(selected, (value) => {
  collectionSelections.set(props.manifest.key, value);
});
const groups = computed(() => groupFiles(props.manifest.files));
const summary = computed(() => selectionSummary(props.manifest, selected.value));
function toggle(ids: string[], event: Event) {
  selected.value = setSelected(selected.value, ids, (event.target as HTMLInputElement).checked);
}
function enqueue(start: boolean) {
  send({ action: 'enqueueCollection', key: props.manifest.key, ids: [...selected.value], start });
  if (start) navigate('/downloads');
}
function openMod(url: string) {
  navigate(localPath(url.split('?')[0]));
}
function downloadFile(id: string) {
  send({ action: 'enqueueCollection', key: props.manifest.key, ids: [id], start: true });
  navigate('/downloads');
}
function coverFor(url: string) {
  return imageUrl(props.covers?.[localPath(url.split('?')[0])]);
}
</script>
<template>
  <section class="collection-picker">
    <div class="collection-summary">
      <div>
        <h2>合集下载 · 修订版 {{ manifest.revision }}</h2>
        <p class="muted">
          已选择 {{ summary.count }} / {{ manifest.files.length }} 个文件 ·
          {{ formatBytes(summary.size) }}
        </p>
      </div>
      <button
        class="primary"
        data-collection-start
        :disabled="!manifest.complete || !summary.count"
        @click="enqueue(true)"
      >
        下载所选（{{ summary.count }}）
      </button>
    </div>
    <div class="collection-tools">
      <button @click="selected = new Set(manifest.files.map((file) => file.id))">{{ t("全选") }}</button
      ><button @click="selected = new Set()">{{ t("全不选") }}</button
      ><button @click="selected = defaultSelection(manifest)">{{ t("恢复默认选择") }}</button
      ><button
        data-collection-enqueue
        :disabled="!manifest.complete || !summary.count"
        @click="enqueue(false)"
      >
        {{ t("仅加入队列") }}
      </button>
    </div>
    <p v-if="!manifest.complete" class="muted">
      {{ manifest.message }} 已读取 {{ manifest.files.length }} /
      {{ manifest.expectedFiles || '?' }} 项。
    </p>
    <p class="muted">
      按合集指定版本下载，可取消整个 Mod 或单个文件。网站生成链接后由 ASH
      下载；遇到登录或验证时会展开网页。
    </p>
    <details v-for="(group, index) in groups" :key="group.id" class="collection-group" open>
      <summary>
        <input
          type="checkbox"
          :data-mod-group="index"
          :aria-label="`选择 ${group.name}`"
          :checked="group.files.every((file) => selected.has(file.id))"
          :indeterminate="
            group.files.some((file) => selected.has(file.id)) &&
            !group.files.every((file) => selected.has(file.id))
          "
          @change="
            toggle(
              group.files.map((file) => file.id),
              $event,
            )
          "
        />
        <button class="group-mod-link" @click.stop.prevent="openMod(group.files[0].url)">
          <img v-if="coverFor(group.files[0].url)" :src="coverFor(group.files[0].url)" alt="" />
          <span class="group-mod-placeholder" v-else>MOD</span>
          <span
            ><strong>{{ group.name }}</strong
            ><small>{{ t("进入 Mod 详情与文件页 ↗") }}</small></span
          ></button
        ><small
          >{{ group.files.filter((file) => selected.has(file.id)).length }} /
          {{ group.files.length }} 个文件</small
        >
      </summary>
      <div class="collection-files">
        <label v-for="file in group.files" :key="file.id" class="collection-file"
          ><input
            type="checkbox"
            :data-collection-file="file.id"
            :checked="selected.has(file.id)"
            @change="toggle([file.id], $event)"
          />
          <div>
            <strong>{{ file.name }}</strong
            ><small
              >版本 {{ file.version || t('未标注') }} · {{ formatBytes(file.size) }} · 文件
              {{ file.fileId }}<template v-if="file.optional === true"> · 作者标为可选</template
              ><template v-else-if="file.optional === false"> · 作者标为必选</template></small
            >
          </div>
          <button class="file-download" @click.stop.prevent="downloadFile(file.id)">
            {{ t("单独下载") }}
          </button></label
        >
      </div>
    </details>
  </section>
</template>
