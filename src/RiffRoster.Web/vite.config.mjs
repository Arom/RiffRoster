import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';

export default defineConfig({
  define: { 'process.env.NODE_ENV': JSON.stringify('production') },
  build: {
    outDir: fileURLToPath(new URL('../../wwwroot/generated', import.meta.url)),
    emptyOutDir: true,
    target: 'es2022',
    lib: {
      entry: fileURLToPath(new URL('./src/app/entry.ts', import.meta.url)),
      name: 'RiffRosterWeb',
      formats: ['iife'],
      fileName: () => 'riffroster-web.js'
    }
  }
});
