import { afterEach, describe, expect, it, vi } from 'vitest'
import type { Receipt, ReceiptUpdate } from '../api'
import { vatWarnings } from '../format'
import { receiptsCsv } from './csv'
import { demoApi, NEEDS_MODEL, PAGE_CLOSED, resetDemo, SAMPLE_PHOTOS, setLiveReader, UPLOAD_AGAIN } from './demoApi'
import type { LiveAnswer, LiveHints, LiveReader } from './liveReader'
import { periodFor, summarize } from './periods'
import { sampleReceipts } from './sampleReceipts'

describe('periodFor (mirrors PeriodRangeTests.cs)', () => {
  it.each([
    ['2025-12-20', '2025-12-15', '2025-12-22', '2025-W51'], // Saturday -> Monday..Sunday
    ['2025-12-15', '2025-12-15', '2025-12-22', '2025-W51'], // Monday is the first day
    ['2025-12-21', '2025-12-15', '2025-12-22', '2025-W51'], // Sunday is the last day
    ['2026-01-01', '2025-12-29', '2026-01-05', '2026-W01'], // ISO week 1 starts in December
    ['2021-01-03', '2020-12-28', '2021-01-04', '2020-W53'], // ...and Jan days can be last year's week 53
  ])('week of %s', (day, start, end, label) => expect(periodFor('Weekly', day)).toMatchObject({ start, end, label }))

  it.each([
    ['2025-01-01', '2025-01-01', '2025-04-01', '2025-Q1'],
    ['2025-03-31', '2025-01-01', '2025-04-01', '2025-Q1'],
    ['2025-04-01', '2025-04-01', '2025-07-01', '2025-Q2'],
    ['2025-12-20', '2025-10-01', '2026-01-01', '2025-Q4'],
  ])('quarter of %s', (day, start, end, label) => expect(periodFor('Quarterly', day)).toMatchObject({ start, end, label }))

  it('handles a leap day', () => {
    expect(periodFor('Daily', '2024-02-29')).toMatchObject({ start: '2024-02-29', end: '2024-03-01', label: '2024-02-29' })
    expect(periodFor('Monthly', '2024-02-29')).toMatchObject({ start: '2024-02-01', end: '2024-03-01', label: '2024-02' })
    expect(periodFor('Yearly', '2024-02-29')).toMatchObject({ start: '2024-01-01', end: '2025-01-01', label: '2024' })
  })
})

const receipt = (over: Partial<Receipt>): Receipt => ({
  id: crypto.randomUUID(), name: '', status: 'Confirmed', merchant: 'DataBlitz', date: '2025-12-20', total: 100,
  vatAmount: null, vatableSales: null, vatExemptSales: null, discount: null, serviceCharge: null, category: 'Shopping', flags: [],
  corrections: [], extractionSeconds: null, error: null, originalFileName: 'x.jpg', uploadedAtUtc: '', confirmedAtUtc: null,
  imageUrl: '', ...over,
})

describe('summarize (mirrors SummaryEndpoints.cs)', () => {
  const receipts = [
    receipt({ date: '2025-12-01', total: 100, vatAmount: 10.71, category: 'Food' }),
    receipt({ date: '2025-12-31', total: 250.5, category: 'Shopping' }),
    receipt({ date: '2025-12-15', total: 50, category: 'Food' }),
    receipt({ date: '2026-01-01', total: 999 }), // next month
    receipt({ date: '2025-12-10', total: 999, status: 'Extracted' }), // not confirmed
    receipt({ date: '2025-07-01', total: 70 }),
  ]
  const s = summarize(receipts, 'Monthly', '2025-12-20')

  it('adds up confirmed receipts in the period only', () =>
    expect(s).toMatchObject({ label: '2025-12', start: '2025-12-01', end: '2026-01-01', total: 400.5, vatTotal: 10.71, count: 3 }))
  it('breaks it down by category, biggest first', () =>
    expect(s.byCategory).toEqual([
      { category: 'Shopping', total: 250.5, count: 1 },
      { category: 'Food', total: 150, count: 2 },
    ]))
  it('has the last 6 periods, oldest first', () => {
    expect(s.history.map((h) => h.label)).toEqual(['2025-07', '2025-08', '2025-09', '2025-10', '2025-11', '2025-12'])
    expect(s.history[0]).toMatchObject({ total: 70, count: 1 })
  })
  it('links the previous and next periods', () => expect([s.previous, s.next]).toEqual(['2025-11-01', '2026-01-01']))
})

describe('receiptsCsv (mirrors ReceiptCsvTests.cs)', () => {
  it('writes an Excel-friendly row', () =>
    expect(receiptsCsv([receipt({ total: 10495, vatAmount: 1124.46, vatableSales: 9370.54, name: '2025-12-20_DataBlitz' })])).toBe(
      'Date,Merchant,Category,Total,VAT,VATable sales,VAT-exempt sales,Discount,Service charge,Receipt\r\n' +
        '20251220,DataBlitz,Shopping,10495.00,1124.46,9370.54,,,,2025-12-20_DataBlitz\r\n',
    ))
  it('quotes commas and stops formulas', () =>
    expect(receiptsCsv([receipt({ merchant: '=HYPERLINK("x")', name: 'a, b' })]).split('\r\n')[1]).toBe(
      '20251220,"\'=HYPERLINK(""x"")",Shopping,100.00,,,,,,"a, b"',
    ))
})

describe('sampleReceipts (like Seeding/SampleReceipts.cs)', () => {
  const today = '2026-09-28'
  const samples = sampleReceipts(today)

  it('are 40 receipts in the 12 months up to today, every category', () => {
    expect(samples).toHaveLength(40)
    expect(samples.every((r) => r.date! <= today && r.date! >= '2025-09-29')).toBe(true)
    expect(new Set(samples.map((r) => r.category)).size).toBe(8)
  })
  it('all pass the VAT check', () =>
    expect(samples.flatMap((r) => vatWarnings({ total: r.total, vat: r.vatAmount, vatable: r.vatableSales, exempt: r.vatExemptSales, discount: r.discount, serviceCharge: r.serviceCharge }))).toEqual([]))
  it('are the same every time', () => expect(sampleReceipts(today)).toEqual(samples))
})

describe('demoApi', () => {
  afterEach(() => {
    resetDemo()
    vi.useRealTimers()
  })

  it('starts with two sample receipts to review and 40 confirmed ones', async () => {
    const list = await demoApi.listReceipts()
    expect(list.filter((r) => r.status === 'Confirmed')).toHaveLength(40)
    expect(list.filter((r) => r.status !== 'Confirmed').map((r) => r.originalFileName).sort()).toEqual([
      'botika-san-roque.jpg',
      'petrolina-gas-station.jpg',
    ])
  })

  it('"reads" a sample receipt in the time the real model took, then gives its real answer', async () => {
    const sample = SAMPLE_PHOTOS.find((s) => s.key === 'botika-san-roque')!
    const uploaded = await demoApi.uploadReceipt(new File(['x'], sample.fileName))
    expect(uploaded.status).toBe('Processing')

    vi.useFakeTimers({ toFake: ['Date'], now: Date.now() + sample.answer.extractionSeconds * 1000 + 1 })
    const read = await demoApi.getReceipt(uploaded.id)
    // The model took its VAT from the "Less 12% VAT" line, so the check sends it to review.
    expect(read).toMatchObject({ status: 'NeedsReview', merchant: sample.answer.merchant, date: sample.answer.date })
  })

  it('lists what the user corrected when confirming', async () => {
    const r = (await demoApi.listReceipts()).find((x) => x.originalFileName === 'botika-san-roque.jpg')!
    const saved = await demoApi.confirmReceipt(r.id, { ...r, vatAmount: 0, category: 'Health' })
    expect(saved.status).toBe('Confirmed')
    expect(saved.corrections).toEqual(['vat_amount: ₱120.00 → ₱0.00'])
  })

  it('checks the service charge like the API', async () => {
    const r = (await demoApi.listReceipts())[0]
    await expect(demoApi.confirmReceipt(r.id, { ...r, serviceCharge: -1 })).rejects.toMatchObject({
      status: 400,
      fieldErrors: { serviceCharge: ['Enter the service charge as a positive amount.'] },
    })
  })

  it('does not flag a service charge on top of the breakdown', async () => {
    const r = (await demoApi.listReceipts())[0]
    const saved = await demoApi.confirmReceipt(r.id, {
      ...r, date: '2026-08-08', category: 'Food', total: 1220, vatAmount: 120, vatableSales: 1000, vatExemptSales: 0, discount: 0, serviceCharge: 100,
    })
    expect(saved.flags).toEqual([])
    expect(saved.serviceCharge).toBe(100)
  })

  it('records a changed service charge, and keeps it when an older page leaves it out', async () => {
    const r = (await demoApi.listReceipts()).find((x) => x.originalFileName === 'botika-san-roque.jpg')!
    const changed = await demoApi.confirmReceipt(r.id, { ...r, vatAmount: 0, serviceCharge: 50 })
    expect(changed.corrections).toContain('service_charge: ₱0.00 → ₱50.00')

    const { serviceCharge: _omitted, ...older } = { ...r, vatAmount: 0 }
    const kept = await demoApi.confirmReceipt(r.id, older as unknown as ReceiptUpdate) // no serviceCharge key
    expect(kept.serviceCharge).toBe(50)
  })

  it('checks required fields like the API', async () => {
    const r = (await demoApi.listReceipts())[0]
    await expect(demoApi.confirmReceipt(r.id, { ...r, total: null })).rejects.toMatchObject({
      status: 400,
      fieldErrors: { total: ['Total is required and must be more than zero.'] },
    })
  })

  it("says other photos need the model on the user's computer", async () => {
    const r = await demoApi.uploadReceipt(new File(['x'], 'my-receipt.jpg'))
    expect(r).toMatchObject({ status: 'Failed', error: NEEDS_MODEL })
    await expect(demoApi.retryReceipt(r.id)).rejects.toMatchObject({ message: NEEDS_MODEL })
  })

  it('says Re-extract needs the model too', async () => {
    const r = (await demoApi.listReceipts()).find((x) => x.originalFileName === 'botika-san-roque.jpg')!
    await expect(demoApi.reextractReceipt(r.id, { ...r, total: 99 }, ['total'])).rejects.toMatchObject({ status: 409, message: NEEDS_MODEL })
  })
})

describe('demoApi with the live demo', () => {
  const READING: LiveAnswer = {
    fields: { merchant: 'Kusina ni Aling Rosa', date: '2026-09-01', total: 250, vatAmount: null, vatableSales: null, vatExemptSales: null, discount: null, serviceCharge: null, category: 'Food' },
    extractionSeconds: 12.5,
  }
  const photo = () => new File(['x'], 'mine.jpg', { type: 'image/jpeg' })
  const flush = () => new Promise((resolve) => setTimeout(resolve, 0))

  interface Pending {
    file: File
    hints?: LiveHints
    onStart: () => boolean
    resolve: (answer: LiveAnswer | null) => void
    reject: (error: Error) => void
  }

  /** A stand-in for the author's PC: photos wait in `pending` until the test starts and answers them. */
  function fakePc(online = true) {
    const pending: Pending[] = []
    const reader: LiveReader = {
      status: async () => (online ? { model: 'qwen3-vl:8b-instruct', perVisitorPerHour: 20 } : null),
      read: (file, onStart, hints) => new Promise((resolve, reject) => pending.push({ file, hints, onStart, resolve, reject })),
    }
    setLiveReader(reader)
    return pending
  }

  afterEach(() => {
    resetDemo()
    setLiveReader(undefined)
    vi.unstubAllGlobals()
  })

  it("reads your own photo on the author's PC when it's online", async () => {
    const pending = fakePc()

    const r = await demoApi.uploadReceipt(photo())
    expect(r.status).toBe('Queued')
    expect(pending[0].onStart()).toBe(true)
    expect((await demoApi.getReceipt(r.id)).status).toBe('Processing')

    pending[0].resolve(READING)
    await flush()
    const read = await demoApi.getReceipt(r.id)
    expect(read).toMatchObject({ merchant: 'Kusina ni Aling Rosa', date: '2026-09-01', total: 250, category: 'Food', extractionSeconds: 12.5 })
    expect(['Extracted', 'NeedsReview']).toContain(read.status)

    const saved = await demoApi.confirmReceipt(r.id, { ...read, total: 260 })
    expect(saved.corrections).toHaveLength(1) // compared with the model's answer
    expect(saved.corrections[0]).toMatch(/^total: /)
  })

  it('runs the same checks on the answer', async () => {
    const pending = fakePc()
    const r = await demoApi.uploadReceipt(photo())
    pending[0].onStart()

    pending[0].resolve({ ...READING, fields: { ...READING.fields, total: null } })
    await flush()

    expect(await demoApi.getReceipt(r.id)).toMatchObject({ status: 'NeedsReview', flags: ['Total is missing.'] })
  })

  it('marks the photo Failed with the reason when the read goes wrong', async () => {
    const pending = fakePc()
    const r = await demoApi.uploadReceipt(photo())
    pending[0].onStart()

    pending[0].reject(new Error("The author's PC is busy; try again in a minute."))
    await flush()

    expect(await demoApi.getReceipt(r.id)).toMatchObject({ status: 'Failed', error: "The author's PC is busy; try again in a minute." })
  })

  it('Try again sends the same photo again', async () => {
    const pending = fakePc()
    const file = photo()
    const r = await demoApi.uploadReceipt(file)
    pending[0].onStart()
    pending[0].reject(new Error('Reading took too long; try again.'))
    await flush()

    const retried = await demoApi.retryReceipt(r.id)

    expect(retried).toMatchObject({ status: 'Queued', error: null })
    expect(pending[1].file).toBe(file)
  })

  /** Your own photo, read once on the PC; `pending` holds what's sent after that. */
  async function readPhoto(file = photo()) {
    const pending = fakePc()
    const r = await demoApi.uploadReceipt(file)
    pending[0].onStart()
    pending[0].resolve(READING)
    await flush()
    return { pending, r: await demoApi.getReceipt(r.id), file }
  }

  it('Re-extract sends the photo again with the corrected fields, which are kept', async () => {
    const { pending, r, file } = await readPhoto()

    const queued = await demoApi.reextractReceipt(r.id, { ...r, total: 260, category: ' food ' }, ['total', 'category', 'total'])

    expect(queued).toMatchObject({ status: 'Queued', total: 260, category: 'Food', merchant: 'Kusina ni Aling Rosa' })
    expect(pending[1].file).toBe(file)
    expect(pending[1].hints).toEqual({ fields: expect.objectContaining({ total: 260, category: 'Food' }), locked: ['total', 'category'] })

    pending[1].onStart()
    pending[1].resolve({ ...READING, fields: { ...READING.fields, total: 260, vatAmount: 27.86, vatableSales: 232.14 } })
    await flush()
    const reread = await demoApi.getReceipt(r.id)
    expect(reread).toMatchObject({ total: 260, vatAmount: 27.86, vatableSales: 232.14 })

    const saved = await demoApi.confirmReceipt(r.id, reread)
    expect(saved.corrections).toContain('total: ₱250.00 → ₱260.00') // compared with the first, unaided answer
  })

  it('Re-extract checks the kept fields like the API', async () => {
    const { pending, r } = await readPhoto()

    await expect(demoApi.reextractReceipt(r.id, { ...r, total: 0 }, ['total'])).rejects.toMatchObject({
      status: 400,
      fieldErrors: { total: ['Total must be more than zero.'] },
    })
    await expect(demoApi.reextractReceipt(r.id, r, [])).rejects.toMatchObject({ status: 400 })
    expect(pending).toHaveLength(1)
  })

  it('Re-extract rejects a negative service charge', async () => {
    const { r } = await readPhoto()

    await expect(demoApi.reextractReceipt(r.id, { ...r, serviceCharge: -1 }, ['service_charge'])).rejects.toMatchObject({
      status: 400,
      fieldErrors: { serviceCharge: ['Enter the service charge as a positive amount.'] },
    })
  })

  it('Re-extract needs the PC to be on', async () => {
    const { r } = await readPhoto()
    const pending = fakePc(false)

    await expect(demoApi.reextractReceipt(r.id, { ...r, total: 260 }, ['total'])).rejects.toMatchObject({ status: 409, message: NEEDS_MODEL })
    expect(pending).toHaveLength(0)
  })

  it('needs the model, as before, when the PC is off', async () => {
    const pending = fakePc(false)

    const r = await demoApi.uploadReceipt(photo())

    expect(r).toMatchObject({ status: 'Failed', error: NEEDS_MODEL })
    expect(pending).toHaveLength(0)
  })

  it('still replays the recorded answer for sample receipts', async () => {
    const pending = fakePc()
    const sample = SAMPLE_PHOTOS[0]

    const r = await demoApi.uploadReceipt(new File(['x'], sample.fileName))

    expect(r.status).toBe('Processing')
    expect(pending).toHaveLength(0)
  })

  it('skips a photo deleted while it waited', async () => {
    const pending = fakePc()
    const r = await demoApi.uploadReceipt(photo())

    await demoApi.deleteReceipt(r.id)

    expect(pending[0].onStart()).toBe(false)
  })

  it('ignores answers that arrive after Reset demo', async () => {
    const pending = fakePc()
    const r = await demoApi.uploadReceipt(photo())
    pending[0].onStart()

    resetDemo()
    pending[0].resolve(READING)
    await flush()

    expect((await demoApi.listReceipts()).some((x) => x.id === r.id)).toBe(false)
  })

  it('a read left unfinished by a closed page is Failed on the next visit', async () => {
    const saved = new Map<string, string>()
    vi.stubGlobal('localStorage', {
      getItem: (key: string) => saved.get(key) ?? null,
      setItem: (key: string, value: string) => void saved.set(key, value),
      removeItem: (key: string) => void saved.delete(key),
    })
    const pending = fakePc()
    const r = await demoApi.uploadReceipt(photo())
    pending[0].onStart() // Processing when the page closes

    vi.resetModules() // a new page load: fresh module state, same localStorage
    const next = await import('./demoApi')
    try {
      expect(await next.demoApi.getReceipt(r.id)).toMatchObject({ status: 'Failed', error: PAGE_CLOSED })
      await expect(next.demoApi.retryReceipt(r.id)).rejects.toMatchObject({ status: 409, message: UPLOAD_AGAIN })
    } finally {
      next.resetDemo()
    }
  })
})
