import type { TaskStatus } from './models';
export const taskLabels: Record<TaskStatus, string> = {
  queued: '等待下载',
  waiting: '正在获取下载链接',
  downloading: '正在下载',
  completed: '已完成',
  paused: '已暂停',
  failed: '失败',
  cancelled: '已取消',
};
export function formatBytes(value = 0): string {
  if (value <= 0) return '大小未知';
  const unit = Math.min(3, Math.floor(Math.log(value) / Math.log(1024)));
  return `${(value / 1024 ** unit).toFixed(unit ? 1 : 0)} ${['B', 'KB', 'MB', 'GB'][unit]}`;
}
export function imageUrl(value?: string): string {
  try {
    const url = new URL(value ?? '');
    return url.protocol === 'https:' ? url.href : '';
  } catch {
    return '';
  }
}
