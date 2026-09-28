import type { ReceiptStatus } from './api'
import { STATUS_LABEL } from './format'

export function StatusBadge({ status }: { status: ReceiptStatus }) {
  return (
    <span className={`badge badge-${status}`}>
      {(status === 'Queued' || status === 'Processing') && <span className="spinner" aria-hidden />}
      {STATUS_LABEL[status]}
    </span>
  )
}

export function ErrorBox({ message }: { message?: string }) {
  if (!message) return null
  return (
    <div className="notice notice-error" role="alert">
      {message}
    </div>
  )
}

export function Flags({ flags, title = 'Check these' }: { flags: string[]; title?: string }) {
  if (flags.length === 0) return null
  return (
    <div className="notice notice-warn" role="status">
      <strong>{title}</strong>
      <ul>
        {flags.map((f) => (
          <li key={f}>{f}</li>
        ))}
      </ul>
    </div>
  )
}
