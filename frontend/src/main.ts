import { createApp } from 'vue';
import App from './App.vue';
import { router } from './router';
import { installWorkspace } from './state/workspace';
import './styles.css';
async function bootstrap() {
  const app = createApp(App);
  app.use(router);
  await router.isReady();
  app.mount('#app');
  const unsubscribe = installWorkspace(router);
  if (import.meta.hot) import.meta.hot.dispose(unsubscribe);
}
void bootstrap();
