// The confidence gate: same rules and wording as ConfidenceGate.cs (tests/confidence-cases.json).
// The demo site uses it in place of the server; the review page uses unsureFields for its markers.
import type { Receipt } from './api'

export type ScoredField = 'date' | 'total' | 'vat_amount' | 'vatable_sales' | 'vat_exempt_sales' | 'discount' | 'service_charge'
export type FieldConfidence = Partial<Record<ScoredField, number>>
export interface Scored {
  overall: number | null
  fields: FieldConfidence
}

/** Dates and amounts, in review-form order. Merchant and category are not scored. */
export const SCORED_FIELDS: ScoredField[] = ['date', 'total', 'vat_amount', 'vatable_sales', 'vat_exempt_sales', 'discount', 'service_charge']

const LABELS: Record<ScoredField, string> = {
  date: 'Date',
  total: 'Total',
  vat_amount: 'VAT',
  vatable_sales: 'VATable sales',
  vat_exempt_sales: 'VAT-exempt sales',
  discount: 'Discount',
  service_charge: 'Service charge',
}

/** 0.62 -> "62%". Rounds down, so a score under the threshold never shows as the threshold. */
export const formatConfidence = (x: number) => `${Math.floor(x * 100 + 1e-9)}%`

/** Fields the user corrected and kept for a re-extraction are theirs, not the model's guess: 1.0. */
export function withLocked(c: Scored, locked: readonly string[]): Scored {
  if (c.overall == null) return c
  const fields: FieldConfidence = { ...c.fields }
  for (const f of SCORED_FIELDS) if (locked.includes(f)) fields[f] = 1
  return { overall: Math.min(...(Object.values(fields) as number[])), fields }
}

/** The flag to add, or null at or above the threshold (or with no score). */
export function confidenceFlag(c: Scored, min: number): string | null {
  if (c.overall == null || c.overall >= min) return null
  const unsure = SCORED_FIELDS.filter((f) => (c.fields[f] ?? 1) < min).map((f) => LABELS[f])
  const pct = formatConfidence(c.overall)
  return unsure.length === 1
    ? `The model was unsure about ${unsure[0]} (${pct}). Check it against the photo.`
    : `The model was unsure about ${unsure.join(', ')} (lowest ${pct}). Check them against the photo.`
}

/** Fields to mark on the review page: the model's shaky reads, until the receipt is confirmed. */
export function unsureFields(r: Pick<Receipt, 'status' | 'fieldConfidence' | 'minConfidence'>): ScoredField[] {
  if (r.status === 'Confirmed' || !r.fieldConfidence) return []
  return SCORED_FIELDS.filter((f) => (r.fieldConfidence[f] ?? 1) < r.minConfidence)
}
