import { useEffect, useState } from 'react'
import { api, isBusy, type Receipt } from './api'

const POLL_MS = 1500

/**
 * Loads one receipt and keeps re-fetching it while the backend is extracting (status Queued/Processing).
 * Polling restarts whenever the receipt becomes busy again, e.g. after setReceipt() with the result of a
 * retry or re-extract.
 */
export function useReceipt(id: string, initial?: Receipt) {
  const [receipt, setReceipt] = useState<Receipt | undefined>(initial)
  const [error, setError] = useState<string>()

  // First load (and whenever the id changes).
  useEffect(() => {
    let cancelled = false
    api.getReceipt(id).then(
      (r) => {
        if (cancelled) return
        setReceipt(r)
        setError(undefined)
      },
      (e: Error) => {
        if (!cancelled) setError(e.message)
      },
    )
    return () => {
      cancelled = true
    }
  }, [id])

  // While busy: fetch again after a pause. Each new receipt object schedules the next fetch.
  useEffect(() => {
    if (!receipt || receipt.id !== id || !isBusy(receipt)) return
    let cancelled = false
    const timer = window.setTimeout(() => {
      api.getReceipt(id).then(
        (r) => {
          if (cancelled) return
          setReceipt(r)
          setError(undefined)
        },
        (e: Error) => {
          if (!cancelled) setError(e.message)
        },
      )
    }, POLL_MS)
    return () => {
      cancelled = true
      window.clearTimeout(timer)
    }
  }, [id, receipt])

  return { receipt, setReceipt, error }
}
