import { describe, expect, it } from 'vitest'
import { clampPan, clampZoom, fitScale, rotateBy, rotatedSize, zoomAt } from './imageTools'

const portrait = { width: 3000, height: 4000 } // typical phone photo
const viewer = { width: 600, height: 800 }

describe('rotateBy', () => {
  it('wraps around', () => {
    expect(rotateBy(270, 90)).toBe(0)
    expect(rotateBy(0, -90)).toBe(270)
    expect(rotateBy(90, 180)).toBe(270)
  })
})

describe('rotatedSize / fitScale', () => {
  it('swaps width and height for 90° and 270°', () => {
    expect(rotatedSize(portrait, 90)).toEqual({ width: 4000, height: 3000 })
    expect(rotatedSize(portrait, 180)).toEqual(portrait)
  })

  it('fits the whole image in the viewer', () => {
    expect(fitScale(portrait, viewer, 0)).toBeCloseTo(0.2) // 3000*0.2 = 600 wide, 4000*0.2 = 800 tall
    expect(fitScale(portrait, viewer, 90)).toBeCloseTo(0.15) // rotated: 4000 wide -> 600
  })

  it('does not divide by zero before the image loads', () => {
    expect(fitScale({ width: 0, height: 0 }, viewer, 0)).toBe(1)
  })
})

describe('clampZoom', () => {
  it('keeps zoom between 1x and 6x', () => {
    expect(clampZoom(0.5)).toBe(1)
    expect(clampZoom(10)).toBe(6)
    expect(clampZoom(2.5)).toBe(2.5)
  })
})

describe('clampPan', () => {
  it('does not allow panning when the image fits', () => {
    expect(clampPan({ x: 100, y: -50 }, portrait, viewer, 0, 1)).toEqual({ x: 0, y: 0 })
  })

  it('allows panning up to the zoomed overflow', () => {
    // zoom 2: image is 1200x1600 on screen, viewer 600x800 -> can move 300 sideways, 400 vertically
    expect(clampPan({ x: 1000, y: -1000 }, portrait, viewer, 0, 2)).toEqual({ x: 300, y: -400 })
    expect(clampPan({ x: 50, y: 60 }, portrait, viewer, 0, 2)).toEqual({ x: 50, y: 60 })
  })
})

describe('zoomAt', () => {
  it('keeps the point under the cursor still', () => {
    // Zooming 1x -> 2x at a point 100px right of centre moves the image 100px left
    expect(zoomAt({ x: 0, y: 0 }, 1, 2, { x: 100, y: 0 })).toEqual({ x: -100, y: 0 })
  })

  it('zooming at the centre does not pan', () => {
    expect(zoomAt({ x: 0, y: 0 }, 1, 3, { x: 0, y: 0 })).toEqual({ x: 0, y: 0 })
  })
})
