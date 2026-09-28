import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type KeyboardEvent,
  type MouseEvent as ReactMouseEvent,
  type PointerEvent,
} from 'react'
import {
  clampPan,
  clampZoom,
  fitScale,
  rotateBy,
  zoomAt,
  type Point,
  type Rotation,
  type Size,
} from './imageTools'

interface Props {
  src: string
  alt: string
  /** Called with the rotation to bake into the saved image. Zoom/pan are never saved. */
  onSaveRotation?: (rotation: Rotation) => Promise<void>
  /** Why saving is unavailable right now (e.g. extraction still running). */
  saveDisabledReason?: string
}

interface View {
  zoom: number
  pan: Point
}

const ZOOM_STEP = 1.5
const FIT: View = { zoom: 1, pan: { x: 0, y: 0 } }

/**
 * Receipt image viewer: rotate in 90° steps, zoom (buttons, wheel, pinch, double-click, +/- keys)
 * and drag to move around when zoomed in.
 */
export function ImageViewer({ src, alt, onSaveRotation, saveDisabledReason }: Props) {
  const viewerRef = useRef<HTMLDivElement>(null)
  const [viewer, setViewer] = useState<Size>({ width: 0, height: 0 })
  const [natural, setNatural] = useState<Size>({ width: 0, height: 0 })
  const [rotation, setRotation] = useState<Rotation>(0)
  const [view, setViewState] = useState<View>(FIT)
  const [dragging, setDragging] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string>()

  // Pointer handlers need the latest values without re-binding, so mirror them in a ref.
  const latest = useRef({ view, rotation, natural, viewer })
  useLayoutEffect(() => {
    latest.current = { view, rotation, natural, viewer }
  })

  /** Applies a zoom/pan change, always clamped so the image can't be lost off-screen. */
  const setView = useCallback((next: View | ((v: View) => View)) => {
    setViewState((prev) => {
      const v = typeof next === 'function' ? next(prev) : next
      const { natural, viewer, rotation } = latest.current
      const zoom = clampZoom(v.zoom)
      return { zoom, pan: clampPan(v.pan, natural, viewer, rotation, zoom) }
    })
  }, [])

  // Track the viewer's size (window resize, phone rotation).
  useEffect(() => {
    const el = viewerRef.current
    if (!el) return
    const observer = new ResizeObserver(([entry]) =>
      setViewer({ width: entry.contentRect.width, height: entry.contentRect.height }),
    )
    observer.observe(el)
    return () => observer.disconnect()
  }, [])

  // Re-clamp when the rotation or viewer size changes.
  useEffect(() => setView((v) => v), [rotation, viewer, natural, setView])

  /** Position relative to the viewer's centre, which is what the zoom math uses. */
  const fromCentre = (clientX: number, clientY: number): Point => {
    const rect = viewerRef.current!.getBoundingClientRect()
    return { x: clientX - rect.left - rect.width / 2, y: clientY - rect.top - rect.height / 2 }
  }

  const zoomTo = useCallback(
    (nextZoom: number, focus: Point = { x: 0, y: 0 }) =>
      setView((v) => {
        const zoom = clampZoom(nextZoom)
        return { zoom, pan: zoomAt(v.pan, v.zoom, zoom, focus) }
      }),
    [setView],
  )

  // Mouse wheel / trackpad zoom. Registered manually because React's onWheel can't preventDefault
  // (the page would scroll instead of zooming).
  useEffect(() => {
    const el = viewerRef.current
    if (!el) return
    const onWheel = (e: WheelEvent) => {
      e.preventDefault()
      const rect = el.getBoundingClientRect()
      const focus = { x: e.clientX - rect.left - rect.width / 2, y: e.clientY - rect.top - rect.height / 2 }
      zoomTo(latest.current.view.zoom * Math.exp(-e.deltaY * 0.0015), focus)
    }
    el.addEventListener('wheel', onWheel, { passive: false })
    return () => el.removeEventListener('wheel', onWheel)
  }, [zoomTo])

  // --- Drag to pan (1 pointer) and pinch to zoom (2 pointers) ---
  const pointers = useRef(new Map<number, Point>())
  const pinch = useRef<{ distance: number; zoom: number } | null>(null)

  const distance = () => {
    const [a, b] = [...pointers.current.values()]
    return Math.hypot(a.x - b.x, a.y - b.y)
  }

  function onPointerDown(e: PointerEvent) {
    e.currentTarget.setPointerCapture(e.pointerId)
    pointers.current.set(e.pointerId, { x: e.clientX, y: e.clientY })
    if (pointers.current.size === 2) pinch.current = { distance: distance(), zoom: view.zoom }
    setDragging(true)
  }

  function onPointerMove(e: PointerEvent) {
    const previous = pointers.current.get(e.pointerId)
    if (!previous) return
    pointers.current.set(e.pointerId, { x: e.clientX, y: e.clientY })

    if (pointers.current.size === 2 && pinch.current) {
      const [a, b] = [...pointers.current.values()]
      const centre = fromCentre((a.x + b.x) / 2, (a.y + b.y) / 2)
      zoomTo((pinch.current.zoom * distance()) / pinch.current.distance, centre)
    } else if (pointers.current.size === 1) {
      const dx = e.clientX - previous.x
      const dy = e.clientY - previous.y
      setView((v) => ({ ...v, pan: { x: v.pan.x + dx, y: v.pan.y + dy } }))
    }
  }

  function onPointerUp(e: PointerEvent) {
    pointers.current.delete(e.pointerId)
    if (pointers.current.size < 2) pinch.current = null
    if (pointers.current.size === 0) setDragging(false)
  }

  function onDoubleClick(e: ReactMouseEvent) {
    if (view.zoom > 1) setView(FIT)
    else zoomTo(2.5, fromCentre(e.clientX, e.clientY))
  }

  function onKeyDown(e: KeyboardEvent) {
    const actions: Record<string, () => void> = {
      '+': () => zoomTo(view.zoom * ZOOM_STEP),
      '=': () => zoomTo(view.zoom * ZOOM_STEP),
      '-': () => zoomTo(view.zoom / ZOOM_STEP),
      '0': () => setView(FIT),
      r: () => setRotation((r) => rotateBy(r, 90)),
      R: () => setRotation((r) => rotateBy(r, -90)),
      ArrowLeft: () => setView((v) => ({ ...v, pan: { ...v.pan, x: v.pan.x + 60 } })),
      ArrowRight: () => setView((v) => ({ ...v, pan: { ...v.pan, x: v.pan.x - 60 } })),
      ArrowUp: () => setView((v) => ({ ...v, pan: { ...v.pan, y: v.pan.y + 60 } })),
      ArrowDown: () => setView((v) => ({ ...v, pan: { ...v.pan, y: v.pan.y - 60 } })),
    }
    const action = actions[e.key]
    if (action) {
      e.preventDefault()
      action()
    }
  }

  async function saveRotation() {
    if (!onSaveRotation || rotation === 0) return
    setSaving(true)
    setError(undefined)
    try {
      await onSaveRotation(rotation)
      setRotation(0) // the saved image is now upright; the parent reloads it
      setView(FIT)
    } catch (e) {
      setError((e as Error).message)
    } finally {
      setSaving(false)
    }
  }

  const scale = fitScale(natural, viewer, rotation)
  const zoomed = view.zoom > 1.001

  return (
    <div className="image-viewer">
      <div className="viewer-toolbar" role="toolbar" aria-label="Image controls">
        <button className="small" onClick={() => setRotation((r) => rotateBy(r, -90))} title="Rotate left (Shift+R)">
          ⟲
        </button>
        <button className="small" onClick={() => setRotation((r) => rotateBy(r, 90))} title="Rotate right (R)">
          ⟳
        </button>
        <span className="toolbar-gap" />
        <button className="small" onClick={() => zoomTo(view.zoom / ZOOM_STEP)} disabled={!zoomed} title="Zoom out (−)">
          −
        </button>
        <span className="zoom-level" aria-live="polite">
          {Math.round(view.zoom * 100)}%
        </span>
        <button className="small" onClick={() => zoomTo(view.zoom * ZOOM_STEP)} title="Zoom in (+)">
          +
        </button>
        <button className="small" onClick={() => setView(FIT)} disabled={!zoomed} title="Fit to view (0)">
          Fit
        </button>
        <a className="button small" href={src} target="_blank" rel="noreferrer" title="Open the full image in a new tab">
          ↗
        </a>
      </div>

      <div
        ref={viewerRef}
        className={`viewer ${zoomed ? 'viewer-zoomed' : ''} ${dragging ? 'viewer-dragging' : ''}`}
        tabIndex={0}
        aria-label={`${alt}. Scroll or pinch to zoom, drag to move, R to rotate.`}
        onPointerDown={onPointerDown}
        onPointerMove={onPointerMove}
        onPointerUp={onPointerUp}
        onPointerCancel={onPointerUp}
        onDoubleClick={onDoubleClick}
        onKeyDown={onKeyDown}
      >
        <img
          src={src}
          alt={alt}
          draggable={false}
          onLoad={(e) => setNatural({ width: e.currentTarget.naturalWidth, height: e.currentTarget.naturalHeight })}
          style={{
            width: natural.width * scale || undefined,
            height: natural.height * scale || undefined,
            transform: `translate(-50%, -50%) translate(${view.pan.x}px, ${view.pan.y}px) scale(${view.zoom}) rotate(${rotation}deg)`,
            transition: dragging ? 'none' : undefined,
            visibility: natural.width ? 'visible' : 'hidden',
          }}
        />
      </div>

      {rotation !== 0 && onSaveRotation && (
        <div className="viewer-save">
          <span>
            Rotated {rotation}°. <span className="muted">Saving keeps the extracted fields as they are.</span>
          </span>
          <div className="button-row">
            <button className="small primary" onClick={saveRotation} disabled={saving || !!saveDisabledReason}>
              {saving ? 'Saving…' : 'Save rotation'}
            </button>
            <button className="small" onClick={() => setRotation(0)} disabled={saving}>
              Undo
            </button>
          </div>
          {saveDisabledReason && <span className="muted">{saveDisabledReason}</span>}
        </div>
      )}
      {error && <div className="notice notice-error">{error}</div>}
    </div>
  )
}
