import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { api, PERIODS, type Period, type Summary } from '../api'
import { ErrorBox } from '../components'
import { formatPeso, shortPeriodLabel } from '../format'

export function DashboardPage() {
  const [period, setPeriod] = useState<Period>('Monthly')
  const [date, setDate] = useState<string>() // any day inside the period; undefined = today
  const [summary, setSummary] = useState<Summary>()
  const [error, setError] = useState<string>()

  useEffect(() => {
    let cancelled = false
    api
      .getSummary(period, date)
      .then((s) => {
        if (cancelled) return
        setSummary(s)
        setError(undefined)
      })
      .catch((e: Error) => {
        if (!cancelled) setError(e.message)
      })
    return () => {
      cancelled = true
    }
  }, [period, date])

  const maxCategory = Math.max(1, ...(summary?.byCategory.map((c) => c.total) ?? []))
  const maxHistory = Math.max(1, ...(summary?.history.map((h) => h.total) ?? []))

  return (
    <section>
      <h1>Spending</h1>

      <div className="tabs" role="tablist" aria-label="Period">
        {PERIODS.map((p) => (
          <button
            key={p}
            role="tab"
            aria-selected={period === p}
            className={period === p ? 'tab tab-active' : 'tab'}
            onClick={() => {
              setPeriod(p)
              setDate(summary?.start) // stay around the same dates when switching period
            }}
          >
            {p}
          </button>
        ))}
      </div>

      <ErrorBox message={error} />

      {summary && (
        <>
          <div className="period-nav">
            <button className="small" onClick={() => setDate(summary.previous)} aria-label="Previous period">
              ‹
            </button>
            <strong>{summary.label}</strong>
            <button className="small" onClick={() => setDate(summary.next)} aria-label="Next period">
              ›
            </button>
            <button className="small link" onClick={() => setDate(undefined)}>
              Today
            </button>
          </div>

          <div className="stats">
            <div className="stat">
              <span className="stat-label">Total spent</span>
              <span className="stat-value">{formatPeso(summary.total)}</span>
            </div>
            <div className="stat">
              <span className="stat-label">Receipts</span>
              <span className="stat-value">{summary.count}</span>
            </div>
            <div className="stat">
              <span className="stat-label">VAT paid</span>
              <span className="stat-value">{formatPeso(summary.vatTotal)}</span>
            </div>
          </div>

          <h2>By category</h2>
          {summary.byCategory.length === 0 ? (
            <p className="empty">
              No confirmed receipts in {summary.label}. <Link to="/upload">Add some</Link> or confirm the ones{' '}
              <Link to="/receipts">waiting for review</Link>.
            </p>
          ) : (
            <ul className="bars">
              {summary.byCategory.map((c) => (
                <li key={c.category} className="bar-row">
                  <span className="bar-label">{c.category}</span>
                  <span className="bar-track">
                    <span className="bar-fill" style={{ width: `${(c.total / maxCategory) * 100}%` }} />
                  </span>
                  <span className="bar-value">
                    {formatPeso(c.total)} <span className="muted">· {c.count}</span>
                  </span>
                </li>
              ))}
            </ul>
          )}

          <h2>Recent {period.toLowerCase()} totals</h2>
          <div className="history" role="img" aria-label={`Totals for the last ${summary.history.length} periods`}>
            {summary.history.map((h) => (
              <button
                key={h.start}
                className={`history-col ${h.label === summary.label ? 'history-current' : ''}`}
                onClick={() => setDate(h.start)}
                title={`${h.label}: ${formatPeso(h.total)} (${h.count} receipts)`}
              >
                <span className="history-bar" style={{ height: `${Math.max(2, (h.total / maxHistory) * 100)}%` }} />
                <span className="history-label">{shortPeriodLabel(h.label)}</span>
              </button>
            ))}
          </div>
          <p className="muted small-print">Only confirmed receipts are counted.</p>
        </>
      )}
    </section>
  )
}
