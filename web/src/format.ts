import type { ReceiptStatus } from './api'

const peso = new Intl.NumberFormat('en-PH', { style: 'currency', currency: 'PHP' })

/** 10495 -> "₱10,495.00"; null -> "—" */
export function formatPeso(value: number | null | undefined): string {
  return value == null ? '—' : peso.format(value)
}

const moneyInput = new Intl.NumberFormat('en-PH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const shiftedInput = new Intl.NumberFormat('en-PH', { minimumFractionDigits: 2, maximumFractionDigits: 6 })

/** Money for an input box: 10495 -> "10,495.00"; null -> "" (parseMoney reads it back) */
export function formatMoneyInput(value: number | null | undefined): string {
  return value == null ? '' : moneyInput.format(value)
}

/**
 * Moves the decimal point one place, for amounts the model read with the point in the wrong spot:
 * "112446.00" -left-> "11,244.60" -left-> "1,124.46". Digits past the centavos are kept (not rounded) so
 * shifting back and forth never loses any. Empty or unparseable text is returned as-is.
 */
export function shiftDecimal(text: string, direction: 'left' | 'right'): string {
  const value = parseMoney(text)
  if (value == null || Number.isNaN(value)) return text
  const shifted = direction === 'left' ? value / 10 : value * 10
  return shiftedInput.format(Number(shifted.toFixed(6))) // toFixed: drop floating-point noise
}

/** "2025-12-20" -> "Dec 20, 2025" (parsed as a calendar date, so no timezone shifts) */
export function formatDate(isoDate: string | null | undefined): string {
  if (!isoDate) return '—'
  const [y, m, d] = isoDate.split('-').map(Number)
  return new Date(y, m - 1, d).toLocaleDateString('en-PH', { month: 'short', day: 'numeric', year: 'numeric' })
}

/** Today's date as YYYY-MM-DD in the user's local timezone. */
export function todayIso(now = new Date()): string {
  const pad = (n: number) => String(n).padStart(2, '0')
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`
}

/** Parses what the user typed in a money field. "10,495.00" -> 10495; "" -> null; "abc" -> NaN */
export function parseMoney(text: string): number | null {
  const cleaned = text.replace(/[₱,\s]/g, '')
  if (cleaned === '') return null
  return Number(cleaned)
}

const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec']

/** Short chart labels: "2026-04" -> "Apr", "2026-W14" -> "W14", "2026-Q1" -> "Q1", "2026-09-27" -> "27". */
export function shortPeriodLabel(label: string): string {
  const week = label.match(/-W(\d+)$/)
  if (week) return `W${week[1]}`
  const quarter = label.match(/-(Q\d)$/)
  if (quarter) return quarter[1]
  const day = label.match(/^\d{4}-\d{2}-(\d{2})$/)
  if (day) return String(Number(day[1]))
  const month = label.match(/^\d{4}-(\d{2})$/)
  if (month) return MONTHS[Number(month[1]) - 1]
  return label // yearly: "2026"
}

export const STATUS_LABEL: Record<ReceiptStatus, string> = {
  Queued: 'Queued',
  Processing: 'Reading…',
  Extracted: 'Ready to confirm',
  NeedsReview: 'Needs review',
  Confirmed: 'Confirmed',
  Failed: 'Failed',
}

/** The money fields of a receipt, as numbers (null = empty, NaN = not a number). */
export interface Amounts {
  total: number | null
  vat: number | null
  vatable: number | null
  exempt: number | null
  discount: number | null
}

const VAT_RATE = 0.12
const TOLERANCE = 0.02 // 2 centavos of rounding
const round2 = (n: number) => Math.round(n * 100) / 100
const num = (n: number | null) => (n == null || Number.isNaN(n) ? 0 : n)

/**
 * Same checks the backend runs (ReceiptValidator.VatFlags), so the review form can warn as you type.
 * VATable sales + VAT + VAT-exempt sales = total, or = total + discount (senior citizen / PWD
 * discounts are taken off after the breakdown); and VAT = 12% of VATable sales.
 */
export function vatWarnings(a: Amounts): string[] {
  const vat = num(a.vat)
  const vatable = num(a.vatable)
  const exempt = num(a.exempt)
  const discount = num(a.discount)
  if (a.total == null || Number.isNaN(a.total) || (vat === 0 && vatable === 0 && exempt === 0)) return []
  const total = a.total

  const warnings: string[] = []
  const breakdown = vatable + vat + exempt
  const adds =
    Math.abs(breakdown - total) <= TOLERANCE || (discount !== 0 && Math.abs(breakdown - discount - total) <= TOLERANCE)
  if (!adds) {
    const parts = exempt !== 0 ? 'VATable sales + VAT + VAT-exempt sales' : 'VATable sales + VAT'
    const less = discount !== 0 ? ` (${formatPeso(breakdown - discount)} after the ${formatPeso(discount)} discount)` : ''
    warnings.push(`${parts} = ${formatPeso(breakdown)}${less}, but total is ${formatPeso(total)}.`)
  }

  const expectedVat = round2(vatable * VAT_RATE)
  if (Math.abs(vat - expectedVat) > TOLERANCE)
    warnings.push(`VAT should be 12% of VATable sales: ${formatPeso(expectedVat)}.`)

  return warnings
}

/**
 * Given the total (and any VAT-exempt sales / discount) plus one of VAT or VATable sales, works out the other
 * so the breakdown adds up. With a discount it can go either way (see vatWarnings), so it picks the answer
 * where VAT is closest to 12% of VATable sales. Null when it can't be worked out.
 */
export function vatComplement(a: Amounts, known: 'vat' | 'vatable'): number | null {
  const value = a[known]
  if (a.total == null || value == null || [a.total, value, a.exempt, a.discount].some((n) => Number.isNaN(n))) return null
  if (value <= 0) return null

  const base = a.total - num(a.exempt)
  const bases = num(a.discount) > 0 ? [base, base + num(a.discount)] : [base]

  let best: { other: number; error: number } | null = null
  for (const b of bases) {
    const other = round2(b - value)
    if (other <= 0) continue
    const [vat, vatable] = known === 'vat' ? [value, other] : [other, value]
    const error = Math.abs(vat - vatable * VAT_RATE)
    if (!best || error < best.error) best = { other, error }
  }
  return best?.other ?? null
}

/**
 * PH VAT is included in the total: VATable sales × 1.12 = total − VAT-exempt sales. Splits the total that way
 * when the receipt's VAT lines are missing or unreadable. VAT is whatever is left after rounding VATable sales,
 * so the two always add up exactly. Null when there's no usable total.
 */
export function vatFromTotal(a: Amounts): { vat: number; vatable: number } | null {
  if (a.total == null || Number.isNaN(a.total) || Number.isNaN(a.exempt ?? 0)) return null
  const base = a.total - num(a.exempt)
  if (base <= 0) return null
  const vatable = round2(base / (1 + VAT_RATE))
  return { vat: round2(base - vatable), vatable }
}
