import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  build: { target: 'es2020', sourcemap: false, chunkSizeWarningLimit: 600 },
  server: { port: 5173 },
  test: { environment: 'jsdom', include: ['src/**/*.test.ts', 'src/**/*.test.tsx'] },
} as never);
