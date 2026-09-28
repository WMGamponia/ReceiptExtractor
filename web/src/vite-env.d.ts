/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Live demo: the author's PC through Tailscale Funnel (a Cloudflare Pages build variable). Unset: no live demo. */
  readonly VITE_LIVE_URL?: string
}
