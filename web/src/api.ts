// Typed client for the ASP.NET Core API (src/ReceiptExtractor.Api).
// In dev, Vite proxies /api to http://localhost:5036 (see vite.config.ts), so no CORS setup is needed.

export type ReceiptStatus = 'Queued' | 'Processing' | 'Extracted' | 'NeedsReview' | 'Confirmed' | 'Failed'

export const CATEGORIES = ['Food', 'Groceries', 'Transport', 'Fuel', 'Health', 'Utilities', 'Shopping', 'Other'] as const
export type Category = (typeof CATEGORIES)[number]

export type Period = 'Daily' | 'Weekly' | 'Monthly' | 'Quarterly' | 'Yearly'
export const PERIODS: Period[] = ['Daily', 'Weekly', 'Monthly', 'Quarterly', 'Yearly']

export interface Receipt {
  id: string
  name: string
  status: ReceiptStatus
  merchant: string | null
  date: string | null // YYYY-MM-DD
  total: number | null
  vatAmount: number | null
  vatableSales: number | null
  vatExemptSales: number | null
  discount: number | null // all discounts added up, positive
  category: Category | null
  flags: string[]
  corrections: string[]
  extractionSeconds: number | null
  error: string | null
  originalFileName: string
  uploadedAtUtc: string
  confirmedAtUtc: string | null
  imageUrl: string
}

export interface ReceiptUpdate {
  merchant: string | null
  date: string | null
  total: number | null
  vatAmount: number | null
  vatableSales: number | null
  vatExemptSales: number | null
  discount: number | null
  category: string | null
}

/** Field names the backend accepts for re-extract (same as the model's JSON). */
export type LockableField =
  | 'merchant'
  | 'date'
  | 'total'
  | 'vat_amount'
  | 'vatable_sales'
  | 'vat_exempt_sales'
  | 'discount'
  | 'category'

export interface BulkDeleteResult {
  deleted: string[]
  skipped: { id: string; name: string; reason: string }[]
}

export interface BulkConfirmResult {
  confirmed: Receipt[]
  skipped: { id: string; name: string; reason: string }[]
}

/** Receipts that can be confirmed straight from the list (the model's answer passed every check). */
export const isReadyToConfirm = (r: Pick<Receipt, 'status'>) => r.status === 'Extracted'

/** Receipts that can be selected for bulk confirm at all. */
export const isSelectable = (r: Pick<Receipt, 'status'>) => r.status === 'Extracted' || r.status === 'NeedsReview'

export interface Summary {
  period: Period
  label: string
  start: string
  end: string // exclusive
  total: number
  vatTotal: number
  count: number
  byCategory: { category: string; total: number; count: number }[]
  history: { label: string; start: string; total: number; count: number }[]
  previous: string
  next: string
}

/** Receipts that can be deleted (not while the model is reading the image). */
export const isDeletable = (r: Pick<Receipt, 'status'>) => r.status !== 'Processing'

/** True while the backend is still working on the receipt. */
export const isBusy = (r: Pick<Receipt, 'status'>) => r.status === 'Queued' || r.status === 'Processing'

export class ApiError extends Error {
  status: number
  fieldErrors: Record<string, string[]>

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message)
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

async function request<T>(url: string, init?: RequestInit): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, init)
  } catch {
    throw new ApiError(0, "Can't reach the API. Is it running on http://localhost:5036?")
  }

  if (!response.ok) {
    // ASP.NET Core returns RFC 7807 problem details for validation errors
    const body = await response.json().catch(() => null)
    const message = typeof body === 'string' ? body : (body?.title ?? `Request failed (${response.status})`)
    throw new ApiError(response.status, message, body?.errors ?? {})
  }

  if (response.status === 204) return undefined as T // No Content (e.g. after a delete)
  return (await response.json()) as T
}

export const api = {
  listReceipts: () => request<Receipt[]>('/api/receipts'),

  getReceipt: (id: string) => request<Receipt>(`/api/receipts/${id}`),

  uploadReceipt: (file: File) => {
    const form = new FormData()
    form.append('file', file)
    return request<Receipt>('/api/receipts', { method: 'POST', body: form })
  },

  confirmReceipt: (id: string, update: ReceiptUpdate) =>
    request<Receipt>(`/api/receipts/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(update),
    }),

  /** Replaces the stored image (e.g. rotated). Extracted fields stay as they are. */
  replaceImage: (id: string, image: Blob, fileName: string) => {
    const form = new FormData()
    form.append('file', image, fileName)
    return request<Receipt>(`/api/receipts/${id}/image`, { method: 'PUT', body: form })
  },

  /** Confirms several receipts with their extracted fields as they are. */
  confirmMany: (ids: string[]) =>
    request<BulkConfirmResult>('/api/receipts/confirm', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ids }),
    }),

  retryReceipt: (id: string) => request<Receipt>(`/api/receipts/${id}/retry`, { method: 'POST' }),

  /**
   * Keeps the `locked` fields (model JSON names, e.g. 'total') at the values in `fields`, tells the model they're
   * correct, and re-reads the rest. Returns the receipt, now Queued.
   */
  reextractReceipt: (id: string, fields: ReceiptUpdate, locked: LockableField[]) =>
    request<Receipt>(`/api/receipts/${id}/reextract`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ fields, locked }),
    }),

  /** Deletes the receipt and its image files for good. */
  deleteReceipt: (id: string) => request<void>(`/api/receipts/${id}`, { method: 'DELETE' }),

  /** Deletes several receipts and their image files. Ones still being read are skipped with a reason. */
  deleteMany: (ids: string[]) =>
    request<BulkDeleteResult>('/api/receipts/delete', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ ids }),
    }),

  getSummary: (period: Period, date?: string) =>
    request<Summary>(`/api/summary?period=${period}${date ? `&date=${date}` : ''}`),
}
