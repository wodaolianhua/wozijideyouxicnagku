import { defineConfig } from 'vite';
import { resolve } from 'path';

/**
 * 常规构建：产出 dist/index.html + app.js
 * 随后由 build_single.mjs 合并为单文件。
 */
export default defineConfig({
  base: './',
  build: {
    outDir: 'dist',
    emptyOutDir: true,
    assetsInlineLimit: 200 * 1024 * 1024,
    cssCodeSplit: false,
    chunkSizeWarningLimit: 4000,
    rollupOptions: {
      input: resolve(__dirname, 'standalone.html'),
      output: {
        inlineDynamicImports: true,
        entryFileNames: 'app.js',
        assetFileNames: 'app.[ext]',
      },
    },
  },
});
