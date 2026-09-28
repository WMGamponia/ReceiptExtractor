// The demo site's stand-in for the API (`npm run build:demo`, see api.ts). There's no server and no model:
// receipts live in this browser's localStorage, and "reading" a sample receipt replays the answer the real app gave
// for it (model-answers.json, made by eval/make_demo_receipts.py). Mirrors the rules in Endpoints/ReceiptEndpoints.cs.
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

const STORAGE_KEY = 'receipt-tracker-demo-v1'
const PLACEHOLDER = '/demo/sample-receipt.png'
const PRELOADED = ['botika-san-roque', 'petrolina-gas-station'] // waiting in "To review" on a first visit

/** A receipt plus what the model answered (to list the user's corrections) and when "reading" finishes. */
interface DemoReceipt extends Receipt {
  model?: ReceiptUpdate
  readyAt?: number
}

// --- State: one list per browser, saved after every change -------------------------------------------------------

let receipts: DemoReceipt[] | undefined

function load(): DemoReceipt[] {
  if (receipts) return receipts
  try {
    const saved = localStorage.getItem(STORAGE_KEY)
    if (saved) receipts = JSON.parse(saved) as DemoReceipt[]
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
    applyAnswer(r, p.answer)
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
  category: a.category,
})

/** The model is done: its fields, the checks, and Extracted / NeedsReview (like ExtractionWorker). */
function applyAnswer(r: DemoReceipt, a: Answer) {
  Object.assign(r, answerFields(a), { extractionSeconds: a.extractionSeconds, readyAt: undefined })
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
function checks(r: Pick<Receipt, 'total' | 'date' | 'vatAmount' | 'vatableSales' | 'vatExemptSales' | 'discount'>): string[] {
  const flags: string[] = []
  if (r.total == null) flags.push('Total is missing.')
  else if (r.total <= 0) flags.push('Total must be more than zero.')
  if (r.date == null) flags.push('Date is missing or unreadable.')
  else if (r.date > todayIso()) flags.push(`Date ${r.date} is in the future.`)
  if (r.total != null)
    flags.push(
      ...vatWarnings({ total: r.total, vat: r.vatAmount, vatable: r.vatableSales, exempt: r.vatExemptSales, discount: r.discount }),
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
  compare('category', model.category, confirmed.category, text)
  return changes
}

/** Sample receipts "being read" finish once the time the real model took has passed. */
function tick() {
  let changed = false
  for (const r of load()) {
    if (r.status === 'Processing' && r.readyAt != null && Date.now() >= r.readyAt) {
      applyAnswer(r, SAMPLE_PHOTOS.find((p) => p.fileName === r.originalFileName)!.answer)
      changed = true
    }
  }
  if (changed) save()
}

function find(id: string): DemoReceipt {
  tick()
  const r = load().find((x) => x.id === id)
  if (!r) throw new ApiError(404, 'Request failed (404)')
  return r
}

const publicCopy = ({ model: _model, readyAt: _readyAt, ...r }: DemoReceipt): Receipt => ({ ...r })

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

  uploadReceipt: (file: File) =>
    later(() => {
      const sample = SAMPLE_PHOTOS.find((p) => p.fileName === file.name)
      const now = new Date().toISOString()
      const r: DemoReceipt = sample
        ? { ...blank(sample.url, sample.fileName, now), status: 'Processing', model: answerFields(sample.answer), readyAt: Date.now() + sample.answer.extractionSeconds * 1000 }
        : { ...blank(URL.createObjectURL(file), file.name, now), status: 'Failed', error: NEEDS_MODEL }
      load().unshift(finish(r))
      save()
      return publicCopy(r)
    }),

  confirmReceipt: (id: string, update: ReceiptUpdate) =>
    later(() => {
      const r = find(id)
      if (r.status === 'Queued' || r.status === 'Processing') throw new ApiError(409, 'Extraction is still running for this receipt.')

      const errors: Record<string, string[]> = {}
      if (!update.date) errors.date = ['Date is required.']
      if (!(update.total != null && update.total > 0)) errors.total = ['Total is required and must be more than zero.']
      const category = CATEGORIES.find((c) => c.toLowerCase() === update.category?.trim().toLowerCase()) ?? null
      if (!category) errors.category = [`Category must be one of: ${CATEGORIES.join(', ')}.`]
      if ((update.vatExemptSales ?? 0) < 0) errors.vatExemptSales = ["VAT-exempt sales can't be negative."]
      if ((update.discount ?? 0) < 0) errors.discount = ['Enter the discount as a positive amount.']
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

  retryReceipt: (_id: string): Promise<Receipt> => later(() => {
    throw new ApiError(409, NEEDS_MODEL)
  }),

  reextractReceipt: (_id: string, _fields: ReceiptUpdate, _locked: LockableField[]): Promise<Receipt> => later(() => {
    throw new ApiError(409, NEEDS_MODEL)
  }),

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
