// Math for the receipt image viewer, plus the canvas code that bakes a rotation into the image file.
// The math is kept separate from React so it can be unit-tested.

export type Rotation = 0 | 90 | 180 | 270

export interface Size {
  width: number
  height: number
}

export interface Point {
  x: number
  y: number
}

export const MIN_ZOOM = 1
export const MAX_ZOOM = 6

/** Adds degrees and wraps into 0/90/180/270. rotateBy(0, -90) -> 270 */
export function rotateBy(rotation: Rotation, degrees: number): Rotation {
  return ((((rotation + degrees) % 360) + 360) % 360) as Rotation
}

/** The image's width/height once rotated: 90° and 270° swap them. */
export function rotatedSize(image: Size, rotation: Rotation): Size {
  return rotation % 180 === 0 ? image : { width: image.height, height: image.width }
}

/** Scale that makes the (rotated) image fit entirely inside the viewer at zoom 1. */
export function fitScale(image: Size, viewer: Size, rotation: Rotation): number {
  const r = rotatedSize(image, rotation)
  if (!r.width || !r.height || !viewer.width || !viewer.height) return 1
  return Math.min(viewer.width / r.width, viewer.height / r.height)
}

export function clampZoom(zoom: number): number {
  return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, zoom))
}

/**
 * Keeps the image from being dragged out of view: you can pan only as far as the zoomed image
 * overflows the viewer. At zoom 1 (image fits) the pan is always 0.
 */
export function clampPan(pan: Point, image: Size, viewer: Size, rotation: Rotation, zoom: number): Point {
  const scale = fitScale(image, viewer, rotation) * zoom
  const r = rotatedSize(image, rotation)
  const maxX = Math.max(0, (r.width * scale - viewer.width) / 2)
  const maxY = Math.max(0, (r.height * scale - viewer.height) / 2)
  const clamp = (v: number, max: number) => Math.min(max, Math.max(-max, v)) || 0 // `|| 0` avoids -0
  return { x: clamp(pan.x, maxX), y: clamp(pan.y, maxY) }
}

/**
 * Zooms while keeping the point under the cursor/fingers in place (like Google Maps).
 * `focus` is relative to the viewer's centre.
 */
export function zoomAt(pan: Point, zoom: number, nextZoom: number, focus: Point): Point {
  const ratio = nextZoom / zoom
  return { x: focus.x - (focus.x - pan.x) * ratio, y: focus.y - (focus.y - pan.y) * ratio }
}

/**
 * Produces a new image file with the rotation baked in (for saving). Zoom and pan are view-only.
 * createImageBitmap(..., 'from-image') applies the photo's EXIF orientation first, so what you
 * saw on screen is exactly what gets saved.
 */
export async function renderRotated(imageUrl: string, rotation: Rotation): Promise<{ blob: Blob; fileName: string }> {
  const response = await fetch(imageUrl, { cache: 'no-store' })
  if (!response.ok) throw new Error(`Couldn't load the image (${response.status}).`)
  const source = await response.blob()
  // Keep PNGs as PNG (sharp text on screenshots/e-receipts); photos stay JPEG.
  const [type, fileName] = source.type === 'image/png' ? ['image/png', 'receipt.png'] : ['image/jpeg', 'receipt.jpg']
  const bitmap = await createImageBitmap(source, { imageOrientation: 'from-image' })

  const out = rotatedSize(bitmap, rotation)
  const canvas = document.createElement('canvas')
  canvas.width = out.width
  canvas.height = out.height

  const ctx = canvas.getContext('2d')
  if (!ctx) throw new Error('Canvas is not available in this browser.')
  ctx.translate(out.width / 2, out.height / 2)
  ctx.rotate((rotation * Math.PI) / 180)
  ctx.drawImage(bitmap, -bitmap.width / 2, -bitmap.height / 2)
  bitmap.close()

  const blob = await new Promise<Blob>((resolve, reject) =>
    canvas.toBlob((b) => (b ? resolve(b) : reject(new Error("Couldn't encode the image."))), type, 0.92),
  )
  return { blob, fileName }
}
