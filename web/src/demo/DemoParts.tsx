// What only the demo site shows (`npm run build:demo`): a banner saying it's a demo, sample receipts to "upload",
// and, while the author runs scripts/live-demo.ps1, a note that visitors' own photos are read on the author's PC.
import { useEffect, useState } from 'react'
import { liveStatus, resetDemo, SAMPLE_PHOTOS } from './demoApi'
import type { LiveStatus } from './liveReader'

/** The author's PC, if it's reading photos right now (null while checking, when it isn't, or with no live demo). */
function useLiveStatus(): LiveStatus | null {
  const [status, setStatus] = useState<LiveStatus | null>(null)
  useEffect(() => {
    let current = true
    liveStatus().then((s) => {
      if (current) setStatus(s)
    })
    return () => {
      current = false
    }
  }, [])
  return status
}

export function DemoBanner() {
  const live = useLiveStatus()
  return (
    <div className="demo-banner" role="note">
      <span>
        <strong>Demo</strong> with made-up receipts. Everything stays in your browser; the real app runs on your own
        computer, AI model included.
        {live && " Your own photos can be read on the author's PC right now."}
      </span>
      <button
        className="small"
        onClick={() => {
          resetDemo()
          window.location.assign('/')
        }}
      >
        Reset demo
      </button>
    </div>
  )
}

/** The synthetic receipts from eval/make_demo_receipts.py; picking one "uploads" it like a real photo. */
export function SamplePicker({ onPick }: { onPick: (file: File) => void }) {
  const [busy, setBusy] = useState<string>()
  const live = useLiveStatus()

  async function pick(sample: (typeof SAMPLE_PHOTOS)[number]) {
    setBusy(sample.key)
    try {
      const blob = await (await fetch(sample.url)).blob()
      onPick(new File([blob], sample.fileName, { type: 'image/jpeg' }))
    } finally {
      setBusy(undefined)
    }
  }

  return (
    <div className="sample-picker">
      {live && (
        <p className="live-note" role="status">
          <strong>The author's PC is online.</strong> Photos you add are read there by {live.model}, then deleted;
          nothing is kept. Don't upload receipts with personal details.
          {live.perVisitorPerHour != null && ` Up to ${live.perVisitorPerHour} an hour.`}
        </p>
      )}
      <h2>Try a sample receipt</h2>
      <p className="muted">
        This demo site has no AI model. These made-up receipts were read by the real model on a PC; picking one replays
        its actual answer (mistakes included) after the time it really took.
      </p>
      <div className="sample-grid">
        {SAMPLE_PHOTOS.map((s) => (
          <button key={s.key} className="sample" onClick={() => pick(s)} disabled={busy === s.key}>
            <img src={s.url} alt="" loading="lazy" />
            <span>{s.key.replaceAll('-', ' ')}</span>
          </button>
        ))}
      </div>
    </div>
  )
}
