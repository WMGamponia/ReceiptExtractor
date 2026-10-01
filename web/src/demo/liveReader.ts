// The live demo (docs/superpowers/specs/2026-09-28-live-demo-design.md): while the author runs scripts/live-demo.ps1,
// visitors' own photos are read on the author's PC (the API's LiveDemo mode, through Tailscale Funnel at VITE_LIVE_URL).
// This module only talks to it; demoApi decides what happens to the receipts.
import type { Category, LockableField, ReceiptUpdate } from '../api'
import type { Scored } from '../confidence'

export interface LiveStatus {
  model: string
  perVisitorPerHour: number | null
}

/** Re-extract: the fields the visitor corrected. The PC tells the model they're correct and keeps them. */
export interface LiveHints {
  fields: ReceiptUpdate
  locked: LockableField[]
}

export interface LiveAnswer {
  fields: ReceiptUpdate
  extractionSeconds: number
  confidence?: Scored | null // null/missing: the PC runs a version without scores
}

export interface LiveReader {
  /** The author's PC, if it's reading photos right now; null when it isn't. Remembered for 30 seconds. */
  status(): Promise<LiveStatus | null>
  /**
   * Waits for this photo's turn (one at a time, so a visitor's batch takes one place in the PC's line), calls
   * `onStart` and sends it (with `hints` for Re-extract). `onStart` returning false skips it (null). Errors carry a
   * message for the visitor.
   */
  read(file: File, onStart: () => boolean, hints?: LiveHints): Promise<LiveAnswer | null>
}

export const WENT_OFFLINE = "The author's PC went offline; try again later."

const MAX_PHOTO_BYTES = 15 * 1024 * 1024
const TOO_BIG = 'File must be between 1 byte and 15 MB.' // the service's own message

const STATUS_TIMEOUT_MS = 3000
const STATUS_CACHE_MS = 30_000

interface Reading {
  fields: Omit<ReceiptUpdate, 'category'>
  category: Category | null
  extractionSeconds: number
  confidence?: Scored | null
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

  async function send(file: File, hints: LiveHints | undefined): Promise<LiveAnswer> {
    // The service cuts off anything bigger before reading it (UploadRules.MaxBytes), with no message to show.
    if (file.size > MAX_PHOTO_BYTES) throw new Error(TOO_BIG)
    const body = new FormData()
    body.append('file', file)
    if (hints) body.append('hints', JSON.stringify(hints))
    let response: Response
    try {
      response = await fetchFn(`${base}/api/extract`, { method: 'POST', body })
    } catch {
      remember(null)
      throw new Error(WENT_OFFLINE)
    }
    if (!response.ok) throw new Error(await reason(response, () => remember(null)))
    const reading = (await response.json()) as Reading
    return {
      fields: { ...reading.fields, category: reading.category },
      extractionSeconds: reading.extractionSeconds,
      confidence: reading.confidence ?? null,
    }
  }

  function read(file: File, onStart: () => boolean, hints?: LiveHints): Promise<LiveAnswer | null> {
    const turn = line.then(() => (onStart() ? send(file, hints) : null))
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
