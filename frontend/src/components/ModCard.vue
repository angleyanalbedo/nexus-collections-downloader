<script setup lang="ts">
import { t, locale, setLocale } from '../i18n';
import { computed, ref, watch } from 'vue';
import type { SiteItem } from '../domain/models';
import { imageUrl } from '../domain/format';
const props = defineProps<{ item: SiteItem; index: number }>();
defineEmits<{ open: [item: SiteItem] }>();
const failed = ref(false);
const source = computed(() => imageUrl(props.item.image));
watch(source, () => {
  failed.value = false;
});
</script>
<template>
  <button class="card" @click="$emit('open', item)">
    <img
      v-if="source && !failed"
      :src="source"
      :loading="index < 8 ? 'eager' : 'lazy'"
      alt=""
      @error="failed = true"
    />
    <div v-else class="placeholder">{{ failed ? t('封面暂不可用') : t('暂无封面') }}</div>
    <div class="card-body">
      <h3>{{ item.name }}</h3>
      <small>{{ item.meta }}</small
      ><span class="go">{{
        item.kind === 'game'
          ? t('浏览 Mod →')
          : item.kind === 'collection'
            ? t('查看合集 →')
            : t('查看详情 →')
      }}</span>
    </div>
  </button>
</template>
