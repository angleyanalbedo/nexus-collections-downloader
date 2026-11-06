import { defineConfig } from 'vite';
import vue from '@vitejs/plugin-vue';
export default defineConfig({
  plugins: [vue()],
  base: './',
  build: { outDir: '../desktop/Assets', emptyOutDir: true, minify: false, sourcemap: false },
});
