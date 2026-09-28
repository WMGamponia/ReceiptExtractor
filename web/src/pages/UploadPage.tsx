import { useRef, useState, type DragEvent } from 'react'
import { Link } from 'react-router-dom'
import { api, IS_DEMO, isBusy, type Receipt } from '../api'
import { ErrorBox, StatusBadge } from '../components'
import { SamplePicker } from '../demo/DemoParts'
import { formatDate, formatPeso } from '../format'
import { useReceipt } from '../useReceipt'

const ACCEPTED = ['image/jpeg', 'image/png']

export function UploadPage() {
  const [uploads, setUploads] = useState<Receipt[]>([])
  const [error, setError] = useState<string>()
  const [dragging, setDragging] = useState(false)
  const cameraInput = useRef<HTMLInputElement>(null)
  const fileInput = useRef<HTMLInputElement>(null)

  async function upload(files: FileList | File[] | null) {
    if (!files) return
    setError(undefined)

    for (const file of Array.from(files)) {
      if (!ACCEPTED.includes(file.type)) {
        setError(`${file.name}: only JPG and PNG photos are supported for now.`)
        continue
      }
      try {
        const receipt = await api.uploadReceipt(file)
        setUploads((current) => [receipt, ...current])
      } catch (e) {
        setError(`${file.name}: ${(e as Error).message}`)
      }
    }
  }

  function onDrop(e: DragEvent) {
    e.preventDefault()
    setDragging(false)
    upload(e.dataTransfer.files)
  }

  return (
    <section>
      <h1>Add receipts</h1>

      <div
        className={`dropzone ${dragging ? 'dropzone-active' : ''}`}
        onDragOver={(e) => {
          e.preventDefault()
          setDragging(true)
        }}
        onDragLeave={() => setDragging(false)}
        onDrop={onDrop}
      >
        <p className="dropzone-hint">Snap a photo, pick files, or drop receipt images here.</p>
        <div className="button-row">
          {/* On a phone, capture="environment" opens the back camera directly */}
          <button className="primary" onClick={() => cameraInput.current?.click()}>
            📷 Take photo
          </button>
          <button onClick={() => fileInput.current?.click()}>Choose files</button>
        </div>
        <input
          ref={cameraInput}
          type="file"
          accept="image/*"
          capture="environment"
          hidden
          onChange={(e) => {
            upload(e.target.files)
            e.target.value = ''
          }}
        />
        <input
          ref={fileInput}
          type="file"
          accept={ACCEPTED.join(',')}
          multiple
          hidden
          onChange={(e) => {
            upload(e.target.files)
            e.target.value = ''
          }}
        />
      </div>

      {IS_DEMO && <SamplePicker onPick={(file) => upload([file])} />}

      <ErrorBox message={error} />

      {uploads.length > 0 && (
        <>
          <h2>This session</h2>
          <ul className="list">
            {uploads.map((r) => (
              <UploadRow key={r.id} initial={r} />
            ))}
          </ul>
        </>
      )}
    </section>
  )
}

function UploadRow({ initial }: { initial: Receipt }) {
  const { receipt = initial, error } = useReceipt(initial.id, initial)

  return (
    <li className="list-row">
      <div className="list-main">
        <span className="list-title">{isBusy(receipt) ? receipt.originalFileName : receipt.name}</span>
        <span className="muted">
          {isBusy(receipt)
            ? 'The model is reading it — usually a few seconds.'
            : `${receipt.merchant ?? 'Unknown merchant'} · ${formatDate(receipt.date)} · ${formatPeso(receipt.total)}`}
        </span>
        {error && <span className="text-error">{error}</span>}
      </div>
      <StatusBadge status={receipt.status} />
      {!isBusy(receipt) && (
        <Link className="button small" to={`/receipts/${receipt.id}`}>
          Review
        </Link>
      )}
    </li>
  )
}
