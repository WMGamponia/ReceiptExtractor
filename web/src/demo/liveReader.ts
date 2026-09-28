// The live demo (docs/superpowers/specs/2026-09-28-live-demo-design.md): while the author runs scripts/live-demo.ps1,
// visitors' own photos are read on the author's PC (the API's LiveDemo mode, through Tailscale Funnel at VITE_LIVE_URL).
// This module only talks to it; demoApi decides what happens to the receipts.
import type { Category, ReceiptUpdate } from '../api'

export interface LiveStatus {
  model: string
  perVisitorPerHour: number | null
}

export interface LiveAnswer {
  fields: ReceiptUpdate
  extractionSeconds: number
}

export interface LiveReader {
  /** The author's PC, if it's reading photos right now; null when it isn't. Remembered for 30 seconds. */
  status(): Promise<LiveStatus | null>
  /**
   * Waits for this photo's turn (one at a time, so a visitor's batch takes one place in the PC's line), calls
   * `onStart` and sends it. `onStart` returning false skips it (null). Errors carry a message for the visitor.
   */
  read(file: File, onStart: () => boolean): Promise<LiveAnswer | null>
}

export const WENT_OFFLINE = "The author's PC went offline; try again later."

const STATUS_TIMEOUT_MS = 3000
const STATUS_CACHE_MS = 30_000

interface Reading {
  fields: Omit<ReceiptUpdate, 'category'>
  category: Category | null
  extractionSeconds: number
}

export function createLiveReader(
  baseUrl: string | undefined,
  fetchFn: typeof fetch = (input, init) => fetch(input, init),
): LiveReader | undefined {
  if (!baseUrl) return undefined
  const base = baseUrl.replace(/\/+$/, '')
  let cached: { at: number; value: LiveStatus | null } | undefined
  let line: Promise<unknown> = Promise.resolve()

  const remember = (value: LiveStatus | null) => {
    cached = { at: Date.now(), value }
    return value
  }

  async function status(): Promise<LiveStatus | null> {
    if (cached && Date.now() - cached.at < STATUS_CACHE_MS) return cached.value
    try {
      const response = await fetchFn(`${base}/api/live`, { signal: AbortSignal.timeout(STATUS_TIMEOUT_MS) })
      return remember(response.ok ? ((await response.json()) as LiveStatus) : null)
    } catch {
      return remember(null)
    }
  }

  async function send(file: File): Promise<LiveAnswer> {
    const body = new FormData()
    body.append('file', file)
    let response: Response
    try {
      response = await fetchFn(`${base}/api/extract`, { method: 'POST', body })
    } catch {
      remember(null)
      throw new Error(WENT_OFFLINE)
    }
    if (!response.ok) throw new Error(await reason(response, () => remember(null)))
    const reading = (await response.json()) as Reading
    return { fields: { ...reading.fields, category: reading.category }, extractionSeconds: reading.extractionSeconds }
  }

  function read(file: File, onStart: () => boolean): Promise<LiveAnswer | null> {
    const turn = line.then(() => (onStart() ? send(file) : null))
    line = turn.catch(() => undefined) // a failed read doesn't hold up the next one
    return turn
  }

  return { status, read }
}

/** The service's own message (ProblemDetails `detail`, or the first validation error); otherwise it's gone. */
async function reason(response: Response, wentOffline: () => void): Promise<string> {
  try {
    const problem = (await response.json()) as { detail?: string; errors?: Record<string, string[]> }
    const message = problem.detail ?? Object.values(problem.errors ?? {})[0]?.[0]
    if (message) return message
  } catch {
    // not JSON: Funnel's own error page when the service has stopped
  }
  wentOffline()
  return WENT_OFFLINE
}
