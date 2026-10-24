import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Under Aspire the API's address is injected through service discovery variables. Everything the API owns is
// proxied, so the browser only ever talks to one origin (needed for the sign-in cookie and the event stream).
const apiTarget =
  process.env['services__knowledgebase-agent__https__0'] ??
  process.env['services__knowledgebase-agent__http__0'] ??
  'http://localhost:5555'

// xfwd passes the browser-facing scheme and host on, so the API builds sign-in redirects to this origin.
const apiProxy = { target: apiTarget, secure: false, xfwd: true }

export default defineConfig({
  plugins: [react()],
  server: {
    port: process.env.PORT ? Number(process.env.PORT) : 5173,
    strictPort: true,
    proxy: {
      '/api': apiProxy,
      '/auth': apiProxy,
      '/signin-oidc': apiProxy,
      '/signout-callback-oidc': apiProxy,
    },
  },
})
