// The demo site's stand-in for the API (`npm run build:demo`, see api.ts). There's no server and no model:
// receipts live in this browser's localStorage, and "reading" a sample receipt replays the answer the real app gave
// for it (model-answers.json, made by eval/make_demo_receipts.py). Mirrors the rules in Endpoints/ReceiptEndpoints.cs.
// While the author runs scripts/live-demo.ps1, visitors' own photos are read on the author's PC (liveReader.ts).
import {
  ApiError,
  CATEGORIES,
  type BulkConfirmResult,
  type BulkDeleteResult,
  type Category,
  type LockableField,
  type Period,
  type Receipt,
  type ReceiptUpdate,
  type Summary,
} from '../api'
import { formatPeso, todayIso, vatWarnings } from '../format'
import { csvDataUrl, receiptsCsv } from './csv'
import { createLiveReader, type LiveHints, type LiveReader, type LiveStatus } from './liveReader'
import modelAnswers from './model-answers.json'
import { summarize } from './periods'
import { sampleReceipts } from './sampleReceipts'

/** The synthetic receipt photos in public/demo/receipts, and what the real app read from each. */
// Pure: lets the normal build (no demo) drop this and the answers JSON.
export const SAMPLE_PHOTOS = /* @__PURE__ */ Object.entries(modelAnswers).map(([key, answer]) => ({
  key,
  fileName: `${key}.jpg`,
  url: `/demo/receipts/${key}.jpg`,
  answer,
}))

export const NEEDS_MODEL =
  'Reading receipts needs the AI model, which runs on your own computer, not on this demo site. ' +
  'Try one of the sample receipts on the Add page, or fill in the fields yourself.'

export const PAGE_CLOSED = "The page was closed before the author's PC finished reading it; upload the photo again."
export const UPLOAD_AGAIN = 'This photo is no longer open in this page; upload it again.'

// The author's PC (the live demo), if this build has an address for it (VITE_LIVE_URL).
let live: LiveReader | undefined = /* @__PURE__ */ createLiveReader(import.meta.env.VITE_LIVE_URL)
// Visitors' own photos, for Try again: this page only, never saved.
const livePhotos = /* @__PURE__ */ new Map<string, File>()

/** The author's PC, if it's reading photos right now (null when it isn't, or with no live demo). */
export const liveStatus = (): Promise<LiveStatus | null> => live?.status() ?? Promise.resolve(null)

/** Tests: stand in for the author's PC. */
export function setLiveReader(reader: LiveReader | undefined) {
  live = reader
}

const STORAGE_KEY = 'receipt-tracker-demo-v1'
const PLACEHOLDER = '/demo/sample-receipt.png'
const PRELOADED = ['botika-san-roque', 'petrolina-gas-station'] // waiting in "To review" on a first visit

/** A receipt plus what the model answered (to list the user's corrections) and when "reading" finishes. */
interface DemoReceipt extends Receipt {
  model?: ReceiptUpdate
  readyAt?: number
  live?: boolean // read (or being read) on the author's PC
}

// --- State: one list per browser, saved after every change -------------------------------------------------------

let receipts: DemoReceipt[] | undefined

function load(): DemoReceipt[] {
  if (receipts) return receipts
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved) {
      receipts = JSON.parse(saved) as DemoReceipt[]
      // A live read can't outlast the page: the photo was only in that page.
      for (const r of receipts) {
        if (r.live && (r.status === 'Queued' || r.status === 'Processing')) {
          r.status = 'Failed'
          r.error = PAGE_CLOSED
        }
      }
    }
  } catch {
    // private window or blocked storage: start fresh, just don't remember it
  }
  receipts ??= initialReceipts()
  return receipts
}

function save() {
  try {
    // Photos the visitor added are blob: URLs that die with the page; don't store them.
    const storable = load().map((r) => (r.imageUrl.startsWith('blob:') ? { ...r, imageUrl: PLACEHOLDER } : r))
    localStorage.setItem(STORAGE_KEY, JSON.stringify(storable))
  } catch {
    // not remembered across visits, that's all
  }
}

/** Back to the first-visit state (the Reset demo button). */
export function resetDemo() {
  try {
    localStorage.removeItem(STORAGE_KEY)
  } catch {
    // nothing saved anyway
  }
  receipts = undefined
}

function initialReceipts(): DemoReceipt[] {
  const today = todayIso()
  const confirmed = sampleReceipts(today).map((fields) => {
    const at = `${fields.date}T04:00:00.000Z` // noon in the Philippines
    return finish({ ...blank(PLACEHOLDER, 'sample-receipt.png', at), ...(fields as Partial<Receipt>), status: 'Confirmed', confirmedAtUtc: at })
  })
  const toReview = SAMPLE_PHOTOS.filter((p) => PRELOADED.includes(p.key)).map((p) => {
    const r: DemoReceipt = { ...blank(p.url, p.fileName, new Date().toISOString()), model: answerFields(p.answer) }
    applyAnswer(r, answerFields(p.answer), p.answer.extractionSeconds)
    return r
  })
  return [...toReview, ...confirmed]
}

// --- Helpers ---------------------------------------------------------------------------------------------------------

function blank(imageUrl: string, originalFileName: string, uploadedAtUtc: string): DemoReceipt {
  return {
    id: crypto.randomUUID(),
    name: '',
    status: 'Queued',
    merchant: null,
    date: null,
    total: null,
    vatAmount: null,
    vatableSales: null,
    vatExemptSales: null,
    discount: null,
    serviceCharge: null,
    category: null,
    flags: [],
    corrections: [],
    extractionSeconds: null,
    error: null,
    originalFileName,
    uploadedAtUtc,
    confirmedAtUtc: null,
    imageUrl,
  }
}

type Answer = (typeof SAMPLE_PHOTOS)[number]['answer']

const answerFields = (a: Answer): ReceiptUpdate => ({
  merchant: a.merchant,
  date: a.date,
  total: a.total,
  vatAmount: a.vatAmount,
  vatableSales: a.vatableSales,
  vatExemptSales: a.vatExemptSales,
  discount: a.discount,
  serviceCharge: a.serviceCharge,
  category: a.category,
})

/** The model is done: its fields, the checks, and Extracted / NeedsReview (like ExtractionWorker). */
function applyAnswer(r: DemoReceipt, fields: ReceiptUpdate, extractionSeconds: number) {
  Object.assign(r, fields, { extractionSeconds, readyAt: undefined })
  r.flags = checks(r)
  r.status = r.flags.length > 0 ? 'NeedsReview' : 'Extracted'
  finish(r)
}

/** Record name from SCOPE.md: "<date>_<merchant>", "<date>", or "Undated - <upload time>". */
function finish<T extends DemoReceipt>(r: T): T {
  const uploaded = r.uploadedAtUtc.slice(0, 16).replace('T', ' ')
  r.name = r.date ? (r.merchant ? `${r.date}_${r.merchant}` : r.date) : `Undated - ${uploaded}`
  return r
}

/** Same checks as ReceiptValidator.Validate (the VAT part is vatWarnings, which mirrors it). */
function checks(
  r: Pick<Receipt, 'total' | 'date' | 'vatAmount' | 'vatableSales' | 'vatExemptSales' | 'discount' | 'serviceCharge'>,
): string[] {
  const flags: string[] = []
  if (r.total == null) flags.push('Total is missing.')
  else if (r.total <= 0) flags.push('Total must be more than zero.')
  if (r.date == null) flags.push('Date is missing or unreadable.')
  else if (r.date > todayIso()) flags.push(`Date ${r.date} is in the future.`)
  if (r.total != null)
    flags.push(
      ...vatWarnings({
        total: r.total, vat: r.vatAmount, vatable: r.vatableSales, exempt: r.vatExemptSales, discount: r.discount,
        serviceCharge: r.serviceCharge,
      }),
    )
  return flags
}

/** What the user changed compared with the model's answer ("field: model → you"), like DiffFromModel. */
function corrections(model: ReceiptUpdate | undefined, confirmed: ReceiptUpdate): string[] {
  if (!model) return []
  const money = (n: number | null) => (n == null ? '(empty)' : formatPeso(n))
  const text = (s: string | null) => s || '(empty)'
  const changes: string[] = []
  const compare = <T>(name: string, a: T, b: T, show: (v: T) => string) => {
    if (a !== b) changes.push(`${name}: ${show(a)} → ${show(b)}`)
  }
  compare('merchant', model.merchant, confirmed.merchant, text)
  compare('date', model.date, confirmed.date, text)
  compare('total', model.total, confirmed.total, money)
  compare('vat_amount', model.vatAmount ?? 0, confirmed.vatAmount ?? 0, money)
  compare('vatable_sales', model.vatableSales ?? 0, confirmed.vatableSales ?? 0, money)
  compare('vat_exempt_sales', model.vatExemptSales ?? 0, confirmed.vatExemptSales ?? 0, money)
  compare('discount', model.discount ?? 0, confirmed.discount ?? 0, money)
  compare('service_charge', model.serviceCharge ?? 0, confirmed.serviceCharge ?? 0, money)
  compare('category', model.category, confirmed.category, text)
  return changes
}

/** Sample receipts "being read" finish once the time the real model took has passed. */
function tick() {
  let changed = false
  for (const r of load()) {
    if (r.status === 'Processing' && r.readyAt != null && Date.now() >= r.readyAt) {
      const { answer } = SAMPLE_PHOTOS.find((p) => p.fileName === r.originalFileName)!
      applyAnswer(r, answerFields(answer), answer.extractionSeconds)
      changed = true
    }
  }
  if (changed) save()
}

/** Sends a visitor's photo to the author's PC: Queued → Processing → Extracted / NeedsReview, or Failed. */
function startLiveRead(reader: LiveReader, id: string, file: File, hints?: LiveHints) {
  const current = () => load().find((x) => x.id === id) // gone if deleted, or the demo was reset, meanwhile
  reader
    .read(file, () => {
      const r = current()
      if (!r) return false
      r.status = 'Processing'
      save()
      return true
    }, hints)
    .then(
      (answer) => {
        const r = current()
        if (!answer || !r) return
        // To list the visitor's corrections later: the first, unaided answer (like ExtractionWorker).
        if (!hints || !r.model) r.model = answer.fields
        applyAnswer(r, answer.fields, answer.extractionSeconds)
        save()
      },
      (e: Error) => {
        const r = current()
        if (!r) return
        r.status = 'Failed'
        r.error = e.message
        save()
      },
    )
}

function find(id: string): DemoReceipt {
  tick()
  const r = load().find((x) => x.id === id)
  if (!r) throw new ApiError(404, 'Request failed (404)')
  return r
}

/** Re-extract's field names (the model's JSON) → the receipt's. */
const FIELD_OF: Record<LockableField, keyof ReceiptUpdate> = {
  merchant: 'merchant',
  date: 'date',
  total: 'total',
  vat_amount: 'vatAmount',
  vatable_sales: 'vatableSales',
  vat_exempt_sales: 'vatExemptSales',
  discount: 'discount',
  service_charge: 'serviceCharge',
  category: 'category',
}

const normalizeCategory = (c: string | null) => CATEGORIES.find((x) => x.toLowerCase() === c?.trim().toLowerCase()) ?? null

const publicCopy = ({ model: _model, readyAt: _readyAt, live: _live, ...r }: DemoReceipt): Receipt => ({ ...r })

// Answer asynchronously, like a real request.
const later = <T>(make: () => T): Promise<T> => new Promise((resolve, reject) => setTimeout(() => {
  try {
    resolve(make())
  } catch (e) {
    reject(e)
  }
}, 150))

// --- The API ----------------------------------------------------------------------------------------------------------

export const demoApi = {
  listReceipts: () =>
    later(() => {
      tick()
      return [...load()].sort((a, b) => b.uploadedAtUtc.localeCompare(a.uploadedAtUtc)).map(publicCopy)
    }),

  getReceipt: (id: string) => later(() => publicCopy(find(id))),

  uploadReceipt: async (file: File) => {
    const sample = SAMPLE_PHOTOS.find((p) => p.fileName === file.name)
    const reader = sample ? undefined : live
    const online = reader != null && (await reader.status()) != null
    return later(() => {
      const now = new Date().toISOString()
      let r: DemoReceipt
      if (sample) {
        // Samples always replay the recorded answer: quick, and they don't use up the live demo's reads.
        r = { ...blank(sample.url, sample.fileName, now), status: 'Processing', model: answerFields(sample.answer), readyAt: Date.now() + sample.answer.extractionSeconds * 1000 }
      } else {
        r = { ...blank(URL.createObjectURL(file), file.name, now), status: 'Failed', error: NEEDS_MODEL }
        if (reader) livePhotos.set(r.id, file) // Try again can send it once the PC is on
        if (online) Object.assign(r, { status: 'Queued', error: null, live: true })
      }
      load().unshift(finish(r))
      save()
      if (online) startLiveRead(reader!, r.id, file)
      return publicCopy(r)
    })
  },

  confirmReceipt: (id: string, update: ReceiptUpdate) =>
    later(() => {
      const r = find(id)
      if (r.status === 'Queued' || r.status === 'Processing') throw new ApiError(409, 'Extraction is still running for this receipt.')

      const errors: Record<string, string[]> = {}
      if (!update.date) errors.date = ['Date is required.']
      if (!(update.total != null && update.total > 0)) errors.total = ['Total is required and must be more than zero.']
      const category = normalizeCategory(update.category)
      if (!category) errors.category = [`Category must be one of: ${CATEGORIES.join(', ')}.`]
      if ((update.vatExemptSales ?? 0) < 0) errors.vatExemptSales = ["VAT-exempt sales can't be negative."]
      if ((update.discount ?? 0) < 0) errors.discount = ['Enter the discount as a positive amount.']
      if ((update.serviceCharge ?? 0) < 0) errors.serviceCharge = ['Enter the service charge as a positive amount.']
      if (Object.keys(errors).length > 0) throw new ApiError(400, 'One or more validation errors occurred.', errors)

      // Only the editable fields, whatever else the caller's object carries (like the API's ReceiptUpdate DTO).
      const fields: ReceiptUpdate = {
        merchant: update.merchant?.trim() || null,
        date: update.date,
        total: update.total,
        vatAmount: update.vatAmount,
        vatableSales: update.vatableSales,
        vatExemptSales: update.vatExemptSales,
        discount: update.discount,
        serviceCharge: update.serviceCharge,
        category,
      }
      r.corrections = corrections(r.model, fields)
      Object.assign(r, fields, { category: category as Category, error: null })
      r.flags = checks(r) // kept as warnings: the user has the final say
      r.status = 'Confirmed'
      r.confirmedAtUtc = new Date().toISOString()
      finish(r)
      save()
      return publicCopy(r)
    }),

  replaceImage: (id: string, image: Blob, _fileName: string) =>
    later(() => {
      const r = find(id)
      r.imageUrl = URL.createObjectURL(image) // this visit only (see save)
      return publicCopy(r)
    }),

  confirmMany: (ids: string[]) =>
    later((): BulkConfirmResult => {
      tick()
      const result: BulkConfirmResult = { confirmed: [], skipped: [] }
      for (const id of new Set(ids)) {
        const r = load().find((x) => x.id === id)
        const missing = r ? [!r.date && 'date', !(r.total && r.total > 0) && 'total', !r.category && 'category'].filter(Boolean) : []
        const reason = !r
          ? 'Not found.'
          : r.status === 'Queued' || r.status === 'Processing'
            ? 'Still being read.'
            : r.status === 'Failed'
              ? 'Extraction failed — open it and retry.'
              : r.status === 'Confirmed'
                ? 'Already confirmed.'
                : missing.length > 0
                  ? `Missing ${missing.join(', ')} — open it to fill in.`
                  : null
        if (reason) {
          result.skipped.push({ id, name: r?.name ?? '(unknown)', reason })
          continue
        }
        r!.corrections = []
        r!.flags = checks(r!)
        r!.status = 'Confirmed'
        r!.confirmedAtUtc = new Date().toISOString()
        result.confirmed.push(publicCopy(r!))
      }
      save()
      return result
    }),

  retryReceipt: async (id: string): Promise<Receipt> => {
    const file = livePhotos.get(id)
    const reader = file ? live : undefined
    const online = reader != null && (await reader.status()) != null
    return later(() => {
      const r = find(id)
      if (r.status === 'Queued' || r.status === 'Processing' || r.status === 'Confirmed')
        throw new ApiError(409, `Can't retry a receipt that is ${r.status}.`)
      if (!file) throw new ApiError(409, r.live ? UPLOAD_AGAIN : NEEDS_MODEL)
      if (!online) throw new ApiError(409, NEEDS_MODEL)
      Object.assign(r, { status: 'Queued', error: null, live: true })
      save()
      startLiveRead(reader!, id, file)
      return publicCopy(r)
    })
  },

  // Only for the visitor's own photos, while the author's PC is on: the rest need the model (sample receipts too).
  reextractReceipt: async (id: string, update: ReceiptUpdate, locked: LockableField[]): Promise<Receipt> => {
    const file = livePhotos.get(id)
    const reader = file ? live : undefined
    const online = reader != null && (await reader.status()) != null
    return later(() => {
      const r = find(id)
      if (r.status === 'Queued' || r.status === 'Processing') throw new ApiError(409, 'Extraction is already running for this receipt.')
      if (!file) throw new ApiError(409, r.live ? UPLOAD_AGAIN : NEEDS_MODEL)
      if (!online) throw new ApiError(409, NEEDS_MODEL)

      // Same rules as ReextractRequest.Check: confirming's rules, only for the fields being kept.
      const keep = [...new Set(locked)]
      const category = normalizeCategory(update.category)
      const errors: Record<string, string[]> = {}
      if (keep.length === 0) errors.locked = ['Change at least one field to re-extract with.']
      if (keep.includes('total') && !(update.total != null && update.total > 0)) errors.total = ['Total must be more than zero.']
      if (keep.includes('category') && !category) errors.category = [`Category must be one of: ${CATEGORIES.join(', ')}.`]
      if (keep.includes('vat_exempt_sales') && (update.vatExemptSales ?? 0) < 0) errors.vatExemptSales = ["VAT-exempt sales can't be negative."]
      if (keep.includes('discount') && (update.discount ?? 0) < 0) errors.discount = ['Enter the discount as a positive amount.']
      if (keep.includes('service_charge') && (update.serviceCharge ?? 0) < 0)
        errors.serviceCharge = ['Enter the service charge as a positive amount.']
      if (Object.keys(errors).length > 0) throw new ApiError(400, 'One or more validation errors occurred.', errors)

      // The visitor's values for the kept fields show right away; the PC keeps them and re-reads the rest.
      const fields: ReceiptUpdate = { ...update, merchant: update.merchant?.trim() || null, category }
      Object.assign(r, Object.fromEntries(keep.map((f) => [FIELD_OF[f], fields[FIELD_OF[f]]])))
      Object.assign(r, { status: 'Queued', error: null, confirmedAtUtc: null, live: true })
      finish(r)
      save()
      startLiveRead(reader!, id, file, { fields, locked: keep })
      return publicCopy(r)
    })
  },

  deleteReceipt: (id: string) =>
    later(() => {
      const r = find(id)
      if (r.status === 'Processing') throw new ApiError(409, "The model is reading this receipt right now. Try again once it's done.")
      receipts = load().filter((x) => x.id !== id)
      save()
    }),

  deleteMany: (ids: string[]) =>
    later((): BulkDeleteResult => {
      tick()
      const result: BulkDeleteResult = { deleted: [], skipped: [] }
      for (const id of new Set(ids)) {
        const r = load().find((x) => x.id === id)
        if (!r) result.skipped.push({ id, name: '(unknown)', reason: 'Not found — maybe already deleted.' })
        else if (r.status === 'Processing') result.skipped.push({ id, name: r.name, reason: "The model is reading this receipt right now. Try again once it's done." })
        else result.deleted.push(id)
      }
      receipts = load().filter((x) => !result.deleted.includes(x.id))
      save()
      return result
    }),

  getSummary: (period: Period, date?: string): Promise<Summary> =>
    later(() => {
      tick()
      return summarize(load(), period, date ?? todayIso())
    }),

  exportCsvUrl: (from: string, to: string) => {
    const rows = load()
      .filter((r) => r.status === 'Confirmed' && r.date && r.date >= from && r.date < to)
      .sort((a, b) => a.date!.localeCompare(b.date!))
    return csvDataUrl(receiptsCsv(rows))
  },
}
