/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    react(),
    // Makes the app installable ("Add to Home Screen") and caches the app shell for fast loads.
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['favicon.svg'],
      manifest: {
        name: 'Receipt Tracker',
        short_name: 'Receipts',
        description: 'Local-first receipt tracker for Philippine receipts',
        theme_color: '#0f766e',
        background_color: '#f6f7f9',
        display: 'standalone',
        start_url: '/',
        icons: [
          { src: 'pwa-192.png', sizes: '192x192', type: 'image/png' },
          { src: 'pwa-512.png', sizes: '512x512', type: 'image/png' },
          { src: 'pwa-512-maskable.png', sizes: '512x512', type: 'image/png', purpose: 'maskable' },
        ],
      },
      workbox: {
        // Never serve API calls or receipt images from the cache: they must always be fresh.
        navigateFallbackDenylist: [/^\/api\//],
      },
    }),
  ],
  server: {
    host: true, // also reachable from your phone on the same Wi-Fi (http://<your-PC-IP>:5173)
    proxy: {
      // Browser calls /api/... on the Vite dev server, Vite forwards them to ASP.NET Core.
      '/api': 'http://localhost:5036',
    },
  },
  preview: {
    proxy: { '/api': 'http://localhost:5036' },
  },
  test: {
    environment: 'node',
  },
})
