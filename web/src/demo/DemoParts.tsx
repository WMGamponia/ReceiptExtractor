// What only the demo site shows (`npm run build:demo`): a banner saying it's a demo, and sample receipts to "upload".
import { useState } from 'react'
import { resetDemo, SAMPLE_PHOTOS } from './demoApi'

export function DemoBanner() {
  return (
    <div className="demo-banner" role="note">
      <span>
        <strong>Demo</strong> with made-up receipts. Everything stays in your browser; the real app runs on your own
        computer, AI model included.
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
