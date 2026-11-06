import { ref } from 'vue';
import english from './en.json';
export type Locale = 'zh-CN' | 'en';
export const locale = ref<Locale>(localStorage.getItem('ash.locale') === 'en' ? 'en' : 'zh-CN');
export function t(text: string): string {
  return locale.value === 'en' ? (english as Record<string, string>)[text] ?? text : text;
}
export function setLocale(value: Locale) {
  locale.value = value;
  localStorage.setItem('ash.locale', value);
  document.documentElement.lang = value;
  window.chrome?.webview?.postMessage({ action: 'setLanguage', value } as never);
}
