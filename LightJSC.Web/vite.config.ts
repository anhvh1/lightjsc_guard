import fs from 'node:fs';
import path from 'node:path';
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const certDir = path.resolve(__dirname, '.cert');
const certFile = path.join(certDir, 'dev-cert.pem');
const keyFile = path.join(certDir, 'dev-key.pem');
const https =
  fs.existsSync(certFile) && fs.existsSync(keyFile)
    ? {
        cert: fs.readFileSync(certFile),
        key: fs.readFileSync(keyFile)
      }
    : undefined;

export default defineConfig({
  plugins: [react()],
  server: {
    host: '0.0.0.0',
    port: 5173,
    strictPort: true,
    https,
    proxy: {
      '/api': 'http://localhost:5177',
      '/health': 'http://localhost:5177',
      '/metrics': 'http://localhost:5177',
      '/hubs': {
        target: 'http://localhost:5177',
        ws: true,
        changeOrigin: true
      }
      ,
      '/mapstyles': 'http://localhost:5177',
      '/maptiles': 'http://localhost:5177'
    }
  }
});
