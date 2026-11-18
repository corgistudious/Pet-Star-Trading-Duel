import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

// Interactive Web Preview: browser-only build with a relative base so it works from any folder or host path.
export default defineConfig({
  plugins: [react()],
  base: './',
  define: { 'import.meta.env.VITE_NO_THUMBS': JSON.stringify('1') },
  build: { target: 'es2020', outDir: 'dist-preview', emptyOutDir: true, sourcemap: false, rollupOptions: { input: 'preview.html' } },
});
