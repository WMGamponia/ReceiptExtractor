import { afterEach, describe, expect, it, vi } from 'vitest'
import { createLiveReader, WENT_OFFLINE } from './liveReader'

const BASE = 'https://receipt-demo.tail1234.ts.net'
const STATUS = { model: 'qwen3-vl:8b-instruct', perVisitorPerHour: 20 }
const READING = {
  fields: { merchant: 'Kusina ni Aling Rosa', date: '2026-09-01', total: 250, vatAmount: null, vatableSales: null, vatExemptSales: null, discount: null },
  category: 'Food',
  extractionSeconds: 12.5,
}

const json = (status: number, body: unknown) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const photo = () => new File([new Uint8Array([0xff, 0xd8, 0xff])], 'mine.jpg', { type: 'image/jpeg' })

describe('createLiveReader', () => {
  afterEach(() => vi.useRealTimers())

  it('is off without VITE_LIVE_URL', () => {
    expect(createLiveReader(undefined)).toBeUndefined()
    expect(createLiveReader('')).toBeUndefined()
  })

  it('is online when /api/live answers, and remembers it for 30 seconds', async () => {
    const fetchFn = vi.fn<typeof fetch>(async () => json(200, STATUS))
    const live = createLiveReader(`${BASE}/`, fetchFn)!

    expect(await live.status()).toEqual(STATUS)
    await live.status()
    expect(fetchFn).toHaveBeenCalledTimes(1)
    expect(fetchFn.mock.calls[0][0]).toBe(`${BASE}/api/live`)

    vi.useFakeTimers({ toFake: ['Date'], now: Date.now() + 30_001 })
    await live.status()
    expect(fetchFn).toHaveBeenCalledTimes(2)
  })

  it('is offline when the PC does not answer', async () => {
    const live = createLiveReader(BASE, vi.fn<typeof fetch>(async () => Promise.reject(new TypeError('Failed to fetch'))))!
    expect(await live.status()).toBeNull()
  })

  it('is offline when the tunnel answers but the service has stopped', async () => {
    const live = createLiveReader(BASE, vi.fn<typeof fetch>(async () => new Response('Bad Gateway', { status: 502 })))!
    expect(await live.status()).toBeNull()
  })

  it('reads a photo and returns the fields with the category', async () => {
    const fetchFn = vi.fn<typeof fetch>(async () => json(200, READING))

    const answer = await createLiveReader(BASE, fetchFn)!.read(photo(), () => true)

    expect(answer).toEqual({ fields: { ...READING.fields, category: 'Food' }, extractionSeconds: 12.5 })
    const [url, init] = fetchFn.mock.calls[0]
    expect(url).toBe(`${BASE}/api/extract`)
    expect(init?.method).toBe('POST')
    expect((init!.body as FormData).get('file')).toBeInstanceOf(File)
    expect((init!.body as FormData).has('hints')).toBe(false)
  })

  it('sends the corrected fields for Re-extract', async () => {
    const fetchFn = vi.fn<typeof fetch>(async () => json(200, READING))
    const hints = { fields: { ...READING.fields, total: 260, category: 'Food' }, locked: ['total' as const] }

    await createLiveReader(BASE, fetchFn)!.read(photo(), () => true, hints)

    const body = fetchFn.mock.calls[0][1]!.body as FormData
    expect(JSON.parse(body.get('hints') as string)).toEqual(hints)
    expect(body.get('file')).toBeInstanceOf(File)
  })

  it('sends photos one at a time', async () => {
    const answers: Array<() => void> = []
    const fetchFn = vi.fn<typeof fetch>(() => new Promise<Response>((resolve) => answers.push(() => resolve(json(200, READING)))))
    const live = createLiveReader(BASE, fetchFn)!

    const reads = [live.read(photo(), () => true), live.read(photo(), () => true), live.read(photo(), () => true)]

    await vi.waitFor(() => expect(fetchFn).toHaveBeenCalledTimes(1))
    answers.shift()!()
    await vi.waitFor(() => expect(fetchFn).toHaveBeenCalledTimes(2))
    answers.shift()!()
    await vi.waitFor(() => expect(fetchFn).toHaveBeenCalledTimes(3))
    answers.shift()!()
    expect(await Promise.all(reads)).toHaveLength(3)
  })

  it('refuses a photo over 15 MB without sending it', async () => {
    const fetchFn = vi.fn<typeof fetch>(async () => json(200, READING))
    const big = new File([new Uint8Array(15 * 1024 * 1024 + 1)], 'big.jpg', { type: 'image/jpeg' })

    await expect(createLiveReader(BASE, fetchFn)!.read(big, () => true)).rejects.toThrow('File must be between 1 byte and 15 MB.')
    expect(fetchFn).not.toHaveBeenCalled()
  })

  it('skips a photo whose receipt is gone by its turn', async () => {
    const fetchFn = vi.fn<typeof fetch>(async () => json(200, READING))

    expect(await createLiveReader(BASE, fetchFn)!.read(photo(), () => false)).toBeNull()
    expect(fetchFn).not.toHaveBeenCalled()
  })

  const LIMIT = "You've reached the demo's limit of 20 receipts an hour; try again later."
  const BUSY = "The author's PC is busy; try again in a minute."
  const COULDNT = "The model couldn't read this photo; try another."
  const NOT_IMAGE = "That file isn't a valid JPG or PNG image."

  // Typed rows: tsc -b type-checks the tests too.
  it.each<[number, string, unknown]>([
    [429, LIMIT, { detail: LIMIT }],
    [503, BUSY, { detail: BUSY }],
    [502, COULDNT, { detail: COULDNT }],
    [400, NOT_IMAGE, { errors: { file: [NOT_IMAGE] } }],
  ])('passes on the reason for a %i', async (status, message, body) => {
    const live = createLiveReader(BASE, vi.fn<typeof fetch>(async () => json(status, body)))!
    await expect(live.read(photo(), () => true)).rejects.toThrow(message)
  })

  it('a stopped service is reported as offline', async () => {
    const live = createLiveReader(BASE, vi.fn<typeof fetch>(async () => new Response('Bad Gateway', { status: 502 })))!
    await expect(live.read(photo(), () => true)).rejects.toThrow(WENT_OFFLINE)
  })

  it('a network error is reported as offline, and remembered', async () => {
    const fetchFn = vi.fn<typeof fetch>(async () => Promise.reject(new TypeError('Failed to fetch')))
    const live = createLiveReader(BASE, fetchFn)!

    await expect(live.read(photo(), () => true)).rejects.toThrow(WENT_OFFLINE)
    expect(await live.status()).toBeNull()
    expect(fetchFn).toHaveBeenCalledTimes(1) // no status call: it just went offline
  })

  it("one failure doesn't block the next photo", async () => {
    const fetchFn = vi
      .fn<typeof fetch>()
      .mockImplementationOnce(async () => json(503, { detail: "The author's PC is busy; try again in a minute." }))
      .mockImplementationOnce(async () => json(200, READING))
    const live = createLiveReader(BASE, fetchFn)!

    const first = live.read(photo(), () => true)
    const second = live.read(photo(), () => true)

    await expect(first).rejects.toThrow('busy')
    expect(await second).not.toBeNull()
  })
})
