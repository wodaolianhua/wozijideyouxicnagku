import { defineConfig } from 'vite';

export default defineConfig({
  // 相对路径：构建产物由 ASP.NET Core wwwroot 托管
  base: './',
  publicDir: false,
  build: {
    target: 'es2022',
    chunkSizeWarningLimit: 1400,
  },
});
