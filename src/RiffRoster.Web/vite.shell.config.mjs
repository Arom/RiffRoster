import { defineConfig } from 'vite';
import { fileURLToPath } from 'node:url';
export default defineConfig({build:{
  outDir:fileURLToPath(new URL('../../wwwroot/generated',import.meta.url)),emptyOutDir:false,target:'es2022',
  lib:{entry:fileURLToPath(new URL('./src/features/shell/entry.ts',import.meta.url)),name:'RiffRosterShell',formats:['iife'],fileName:() => 'shell-features.js'}
}});
