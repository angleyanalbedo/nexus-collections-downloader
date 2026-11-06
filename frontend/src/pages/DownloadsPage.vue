<script setup lang="ts">
import { computed } from 'vue';
import { send } from '../bridge/host';
import { workspace } from '../state/workspace';
import type { DownloadTask } from '../domain/models';
import { taskLabels, formatBytes } from '../domain/format';

const activeTasks = computed(() =>
  workspace.host.tasks.filter(
    (task) => !['completed', 'cancelled', 'failed'].includes(task.status),
  ),
);
const finishedTasks = computed(() =>
  workspace.host.tasks
    .filter((task) => ['completed', 'cancelled', 'failed'].includes(task.status))
    .reverse(),
);
const waitingCount = computed(
  () => workspace.host.tasks.filter((task) => task.status === 'waiting').length,
);
const downloadingCount = computed(
  () => workspace.host.tasks.filter((task) => task.status === 'downloading').length,
);
const completedCount = computed(
  () => workspace.host.tasks.filter((task) => task.status === 'completed').length,
);
const receivedBytes = computed(() =>
  workspace.host.tasks.reduce((total, task) => total + task.received, 0),
);

function stageTitle(task: DownloadTask): string {
  if (task.status === 'queued') return '等待队列启动';
  if (task.status === 'waiting') return '正在获取 Nexus 签名下载链接';
  if (task.status === 'downloading') return '后台正在传输文件';
  if (task.status === 'paused') return '已暂停，可直接继续';
  if (task.status === 'completed') return '文件已保存';
  if (task.status === 'cancelled') return '任务已取消，可重新开始';
  return '下载未能完成';
}
function progress(task: DownloadTask): number | null {
  return task.total > 0 ? Math.min(100, (task.received / task.total) * 100) : null;
}
function canCancel(task: DownloadTask): boolean {
  return ['queued', 'waiting', 'downloading', 'paused'].includes(task.status);
}
function canRetry(task: DownloadTask): boolean {
  return task.status === 'failed' || task.status === 'cancelled';
}
function cancel(task: DownloadTask) {
  send({ action: 'cancel', id: task.id });
}
function retry(task: DownloadTask) {
  send({ action: 'retry', id: task.id });
}
function remove(task: DownloadTask) {
  send({ action: 'deleteTask', id: task.id });
}
function clearFinished() {
  send({ action: 'clearFinished' });
}
</script>

<template>
  <section id="downloadsView" class="downloads-page">
    <header class="downloads-hero">
      <div>
        <span class="download-kicker">TRANSFER CENTER</span>
        <h1>下载管理</h1>
        <p>链接获取与文件传输分开进行。拿到签名链接后会立即交给后台下载。</p>
      </div>
      <div class="queue-actions">
        <button
          class="primary"
          :disabled="!workspace.host.tasks.some((task) => task.status === 'queued')"
          @click="send({ action: 'start' })"
        >
          {{ workspace.host.queueEnabled ? '队列运行中' : '开始队列' }}
        </button>
        <button @click="send({ action: 'stop' })">停止排队</button>
        <button
          v-if="activeTasks.length"
          class="danger-ghost"
          @click="send({ action: 'cancelAll' })"
        >
          取消全部
        </button>
      </div>
    </header>

    <section class="download-overview">
      <div class="summary-tile">
        <strong>{{ downloadingCount }}</strong
        ><span>正在下载</span>
      </div>
      <div class="summary-tile">
        <strong>{{ waitingCount }}</strong
        ><span>获取链接</span>
      </div>
      <div class="summary-tile">
        <strong>{{ completedCount }}</strong
        ><span>已完成</span>
      </div>
      <div class="summary-tile wide">
        <strong>{{ formatBytes(receivedBytes) }}</strong
        ><span>当前累计接收</span>
      </div>
      <div class="save-location">
        <span>保存位置</span>
        <code>{{ workspace.host.output }}</code>
        <div>
          <button @click="send({ action: 'folder' })">更改</button>
          <button @click="send({ action: 'openFolder' })">打开目录 ↗</button>
        </div>
      </div>
    </section>

    <section class="task-group">
      <div class="section-heading">
        <h2>进行中的任务</h2>
        <span>{{ activeTasks.length }} 项</span>
      </div>
      <div class="task-stack">
        <article
          v-for="task in activeTasks"
          :key="task.id"
          class="task-card"
          :class="`is-${task.status}`"
        >
          <div class="task-visual">
            <span>
              {{
                task.status === 'waiting'
                  ? '↗'
                  : task.status === 'paused'
                    ? 'Ⅱ'
                    : task.status === 'failed'
                      ? '!'
                      : '↓'
              }}
            </span>
          </div>
          <div class="task-body">
            <div class="task-heading">
              <div>
                <small>{{ task.file.modName || 'Nexus Mods 文件' }}</small>
                <h3>{{ task.file.name }}</h3>
                <p v-if="task.file.version">版本 {{ task.file.version }}</p>
              </div>
              <span class="status-chip">{{ taskLabels[task.status] }}</span>
            </div>
            <p class="stage-title">{{ stageTitle(task) }}</p>
            <div
              class="task-progress"
              :class="{ indeterminate: task.status === 'waiting' || !task.total }"
            >
              <span v-if="progress(task) !== null" :style="{ width: `${progress(task)}%` }" />
            </div>
            <div class="task-meta">
              <span>
                {{ formatBytes(task.received)
                }}<template v-if="task.total"> / {{ formatBytes(task.total) }}</template>
              </span>
              <span v-if="progress(task) !== null">{{ Math.round(progress(task)!) }}%</span>
            </div>
            <p v-if="task.error && task.status === 'failed'" class="task-error">
              {{ task.error }}
            </p>
            <div class="task-actions">
              <button
                v-if="task.status === 'downloading'"
                @click="send({ action: 'pause', id: task.id })"
              >
                暂停
              </button>
              <button
                v-else-if="task.status === 'paused' && task.canResume"
                class="primary"
                @click="send({ action: 'resume', id: task.id })"
              >
                继续下载
              </button>
              <button v-else-if="canRetry(task)" class="primary" @click="retry(task)">
                重新尝试
              </button>
              <button v-if="task.status === 'waiting'" @click="send({ action: 'browser' })">
                查看网页步骤 ↗
              </button>
              <button v-if="canCancel(task)" class="danger-ghost" @click="cancel(task)">
                取消
              </button>
            </div>
          </div>
        </article>
      </div>
      <div v-if="!activeTasks.length" class="empty">
        <strong>队列当前为空</strong>
        <p>进入 Mod 文件页或合集清单，选择文件后加入下载队列。</p>
      </div>
    </section>

    <section v-if="finishedTasks.length" class="task-group finished-section">
      <div class="section-heading">
        <h2>已结束</h2>
        <div class="finished-heading-actions">
          <span>{{ finishedTasks.length }} 项</span>
          <button class="danger-ghost" @click="clearFinished">清空已结束</button>
        </div>
      </div>
      <div class="finished-list">
        <article
          v-for="task in finishedTasks"
          :key="task.id"
          class="finished-row"
          :class="`is-${task.status}`"
        >
          <span class="finished-mark">{{ task.status === 'completed' ? '✓' : '×' }}</span>
          <div>
            <h3>{{ task.file.name }}</h3>
            <small>{{ taskLabels[task.status] }} · {{ formatBytes(task.received) }}</small>
          </div>
          <p v-if="task.error" class="task-error">{{ task.error }}</p>
          <div class="finished-actions">
            <button v-if="canRetry(task)" @click="retry(task)">重新加入</button>
            <button class="danger-ghost" @click="remove(task)">删除记录</button>
          </div>
        </article>
      </div>
    </section>
  </section>
</template>
