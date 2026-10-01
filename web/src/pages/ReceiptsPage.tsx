import { useEffect, useState } from 'react'
import { Link, useLocation, useSearchParams } from 'react-router-dom'
import { api, isBusy, isDeletable, isReadyToConfirm, isSelectable, type Receipt } from '../api'
import { ErrorBox, StatusBadge } from '../components'
import { formatConfidence } from '../confidence'
import { formatDate, formatPeso } from '../format'

type Filter = 'todo' | 'confirmed' | 'all'

const FILTERS: { key: Filter; label: string; match: (r: Receipt) => boolean }[] = [
  { key: 'todo', label: 'To review', match: (r) => r.status !== 'Confirmed' },
  { key: 'confirmed', label: 'Confirmed', match: (r) => r.status === 'Confirmed' },
  { key: 'all', label: 'All', match: () => true },
]

/** What the checkboxes are for: confirming on the To-review tab, deleting on the others. */
type Mode = 'confirm' | 'delete'

const POLL_MS = 2000

interface BulkOutcome {
  verb: 'Confirmed' | 'Deleted'
  count: number
  skipped: { id: string; name: string; reason: string }[]
}

export function ReceiptsPage() {
  const location = useLocation()
  const { confirmed: justConfirmed, deleted: justDeleted } =
    (location.state as { confirmed?: string; deleted?: string } | null) ?? {}

  // The tab lives in the URL (?tab=confirmed) so coming back from a receipt lands on the same tab.
  const [params, setParams] = useSearchParams()
  const filter: Filter = FILTERS.some((f) => f.key === params.get('tab')) ? (params.get('tab') as Filter) : 'todo'
  const mode: Mode = filter === 'todo' ? 'confirm' : 'delete'
  const canSelect = mode === 'confirm' ? isSelectable : isDeletable

  const [receipts, setReceipts] = useState<Receipt[]>()
  const [error, setError] = useState<string>()
  const [selected, setSelected] = useState<Set<string>>(new Set())
  const [working, setWorking] = useState(false)
  const [askDelete, setAskDelete] = useState(false)
  const [outcome, setOutcome] = useState<BulkOutcome>()
  const [reloadKey, setReloadKey] = useState(0) // bump to re-fetch the list

  useEffect(() => {
    let cancelled = false
    let timer: number | undefined

    async function load() {
      try {
        const list = await api.listReceipts()
        if (cancelled) return
        setReceipts(list)
        setError(undefined)
        // Drop selections of receipts that no longer exist (deleted elsewhere).
        setSelected((current) => {
          const ids = new Set(list.map((r) => r.id))
          const next = new Set([...current].filter((id) => ids.has(id)))
          return next.size === current.size ? current : next
        })
        if (list.some(isBusy)) timer = window.setTimeout(load, POLL_MS) // refresh while any are being read
      } catch (e) {
        if (!cancelled) setError((e as Error).message)
      }
    }

    load()
    return () => {
      cancelled = true
      window.clearTimeout(timer)
    }
  }, [reloadKey])

  const visible = receipts?.filter(FILTERS.find((f) => f.key === filter)!.match) ?? []
  // Only count what's on this tab and still selectable (a status may have changed since it was ticked).
  const selectedReceipts = visible.filter((r) => selected.has(r.id) && canSelect(r))
  const count = selectedReceipts.length

  const selectAllCandidates = mode === 'confirm' ? visible.filter(isReadyToConfirm) : visible.filter(isDeletable)
  const allSelected = selectAllCandidates.length > 0 && selectAllCandidates.every((r) => selected.has(r.id))
  const selectedNeedingReview = selectedReceipts.filter((r) => r.status === 'NeedsReview').length

  function switchTab(tab: Filter) {
    setParams(tab === 'todo' ? {} : { tab }, { replace: true })
    setSelected(new Set()) // the checkboxes mean something else on each tab
    setAskDelete(false)
  }

  function toggle(id: string, on: boolean) {
    setSelected((current) => {
      const next = new Set(current)
      if (on) next.add(id)
      else next.delete(id)
      return next
    })
  }

  function clearSelection() {
    setSelected(new Set())
    setAskDelete(false)
  }

  async function confirmSelected() {
    setWorking(true)
    setOutcome(undefined)
    try {
      const res = await api.confirmMany(selectedReceipts.map((r) => r.id))
      setOutcome({ verb: 'Confirmed', count: res.confirmed.length, skipped: res.skipped })
      clearSelection()
      setReloadKey((k) => k + 1)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setWorking(false)
    }
  }

  async function deleteSelected() {
    setWorking(true)
    setOutcome(undefined)
    try {
      const res = await api.deleteMany(selectedReceipts.map((r) => r.id))
      setOutcome({ verb: 'Deleted', count: res.deleted.length, skipped: res.skipped })
      clearSelection()
      setReloadKey((k) => k + 1)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setWorking(false)
    }
  }

  // Remember the tab when opening a receipt, so saving/deleting/cancelling comes back here.
  const back = `${location.pathname}${location.search}`

  return (
    <section className={count > 0 ? 'has-bulk-bar' : undefined}>
      <div className="page-header">
        <h1>Receipts</h1>
        <Link className="button primary" to="/upload">
          + Add
        </Link>
      </div>

      {justConfirmed && !outcome && <div className="notice notice-ok">Saved “{justConfirmed}”.</div>}
      {justDeleted && !outcome && <div className="notice notice-ok">Deleted “{justDeleted}”.</div>}
      {outcome && <BulkResult outcome={outcome} back={back} onClose={() => setOutcome(undefined)} />}
      <ErrorBox message={error} />

      <div className="tabs" role="tablist">
        {FILTERS.map((f) => (
          <button
            key={f.key}
            role="tab"
            aria-selected={filter === f.key}
            className={filter === f.key ? 'tab tab-active' : 'tab'}
            onClick={() => switchTab(f.key)}
          >
            {f.label}
            {receipts && <span className="tab-count">{receipts.filter(f.match).length}</span>}
          </button>
        ))}
      </div>

      {visible.length > 0 && (
        <div className="list-toolbar">
          <button
            className="small"
            onClick={() => setSelected((current) => new Set([...current, ...selectAllCandidates.map((r) => r.id)]))}
            disabled={selectAllCandidates.length === 0 || allSelected}
          >
            ☑ {mode === 'confirm' ? 'Select all ready to confirm' : 'Select all'} ({selectAllCandidates.length})
          </button>
          {count > 0 && (
            <button className="small link" onClick={clearSelection}>
              Clear selection
            </button>
          )}
        </div>
      )}

      {!receipts && !error && <p className="muted">Loading…</p>}
      {receipts && visible.length === 0 && (
        <p className="empty">
          {filter === 'todo' ? 'Nothing to review. 🎉' : 'No receipts yet.'} <Link to="/upload">Add one</Link>
        </p>
      )}

      <ul className="list">
        {visible.map((r) => (
          <li key={r.id} className={`list-row ${selected.has(r.id) && canSelect(r) ? 'list-row-selected' : ''}`}>
            <input
              type="checkbox"
              className="row-check"
              checked={selected.has(r.id) && canSelect(r)}
              disabled={!canSelect(r)}
              onChange={(e) => toggle(r.id, e.target.checked)}
              aria-label={`Select ${r.merchant ?? r.originalFileName}`}
              title={checkboxTitle(mode, r)}
            />
            <Link to={`/receipts/${r.id}`} state={{ back }} className="list-main list-link">
              <span className="list-title">{r.merchant ?? r.originalFileName}</span>
              <span className="muted">
                {formatDate(r.date)}
                {r.category && ` · ${r.category}`}
                {r.flags.length > 0 && r.status === 'NeedsReview' && ` · ${r.flags.length} issue${r.flags.length > 1 ? 's' : ''}`}
                {r.confidence != null && (r.status === 'Extracted' || r.status === 'NeedsReview') && ` · ${formatConfidence(r.confidence)}`}
              </span>
            </Link>
            <span className="amount">{formatPeso(r.total)}</span>
            <StatusBadge status={r.status} />
          </li>
        ))}
      </ul>

      {count > 0 && (
        <div className={`bulk-bar ${askDelete ? 'bulk-bar-danger' : ''}`} role="region" aria-label="Bulk actions">
          <div className="bulk-bar-text">
            {askDelete ? (
              <strong>
                Delete {count} receipt{count === 1 ? '' : 's'} and {count === 1 ? 'its photo' : 'their photos'}? This can't be
                undone.
              </strong>
            ) : (
              <>
                <strong>{count} selected</strong>
                <span className="muted"> · {formatPeso(selectedReceipts.reduce((sum, r) => sum + (r.total ?? 0), 0))}</span>
              </>
            )}
            {mode === 'confirm' && selectedNeedingReview > 0 && (
              <div className="bulk-warning">
                ⚠ {selectedNeedingReview} failed a check and will be confirmed exactly as extracted.
              </div>
            )}
          </div>
          <div className="button-row">
            {mode === 'confirm' && (
              <>
                <button className="small" onClick={clearSelection} disabled={working}>
                  Cancel
                </button>
                <button className="small primary" onClick={confirmSelected} disabled={working}>
                  {working ? 'Confirming…' : `Confirm ${count}`}
                </button>
              </>
            )}
            {mode === 'delete' && !askDelete && (
              <>
                <button className="small" onClick={clearSelection}>
                  Cancel
                </button>
                <button className="small danger-solid" onClick={() => setAskDelete(true)}>
                  Delete {count}
                </button>
              </>
            )}
            {mode === 'delete' && askDelete && (
              <>
                <button className="small" onClick={() => setAskDelete(false)} disabled={working}>
                  Keep them
                </button>
                <button className="small danger-solid" onClick={deleteSelected} disabled={working}>
                  {working ? 'Deleting…' : `Yes, delete ${count}`}
                </button>
              </>
            )}
          </div>
        </div>
      )}
    </section>
  )
}

function checkboxTitle(mode: Mode, r: Receipt): string {
  if (mode === 'delete') return isDeletable(r) ? 'Select to delete' : 'Wait until the model has finished reading it'
  return isSelectable(r) ? 'Select for bulk confirm' : 'Open it to fix or retry first'
}

function BulkResult({ outcome, back, onClose }: { outcome: BulkOutcome; back: string; onClose: () => void }) {
  const { verb, count, skipped } = outcome
  return (
    <div className={`notice ${skipped.length ? 'notice-warn' : 'notice-ok'}`} role="status">
      <div className="notice-head">
        <strong>
          {verb} {count} receipt{count === 1 ? '' : 's'}
          {skipped.length > 0 && `, skipped ${skipped.length}`}.
        </strong>
        <button className="small link" onClick={onClose} aria-label="Dismiss">
          ✕
        </button>
      </div>
      {skipped.length > 0 && (
        <ul>
          {skipped.map((s) => (
            <li key={s.id}>
              <Link to={`/receipts/${s.id}`} state={{ back }}>
                {s.name}
              </Link>
              : {s.reason}
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}
