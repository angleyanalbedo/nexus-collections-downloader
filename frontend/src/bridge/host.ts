import type { HostCommand, HostState } from '../domain/models';
interface WebViewBridge {
  postMessage(command: HostCommand): void;
  addEventListener(name: 'message', listener: (event: MessageEvent<unknown>) => void): void;
  removeEventListener(name: 'message', listener: (event: MessageEvent<unknown>) => void): void;
}
declare global {
  interface Window {
    chrome?: { webview?: WebViewBridge };
  }
}
export function send(command: HostCommand): void {
  window.chrome?.webview?.postMessage(command);
}
function isHostState(value: unknown): value is HostState {
  if (!value || typeof value !== 'object') return false;
  const state = value as Partial<HostState>;
  return state.type === 'state' && Array.isArray(state.tasks) && typeof state.busy === 'boolean';
}
export function subscribe(callback: (state: HostState) => void): () => void {
  const listener = (event: MessageEvent<unknown>) => {
    if (isHostState(event.data)) callback(event.data);
  };
  window.chrome?.webview?.addEventListener('message', listener);
  return () => window.chrome?.webview?.removeEventListener('message', listener);
}
