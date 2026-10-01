import { describe, expect, it } from 'vitest'
import cases from '../../tests/confidence-cases.json'
import { confidenceFlag, formatConfidence, unsureFields, withLocked, type FieldConfidence } from './confidence'

describe('confidence gate (same cases as ConfidenceGateTests.cs)', () => {
  it.each(cases.gate.map((c) => [c.name, c] as const))('%s', (_, c) => {
    const fields = c.fields as FieldConfidence
    const values = Object.values(fields) as number[]
    const scored = { overall: values.length ? Math.min(...values) : null, fields }

    const gated = withLocked(scored, c.locked)

    expect(gated.overall).toBe(c.overall)
    expect(confidenceFlag(gated, c.min)).toBe(c.flag)
  })
})

describe('formatConfidence', () => {
  it('rounds down', () => expect(formatConfidence(0.849)).toBe('84%'))
  it('survives float error', () => expect(formatConfidence(0.29)).toBe('29%'))
})

describe('unsureFields', () => {
  const r = { status: 'NeedsReview' as const, fieldConfidence: { date: 0.6, total: 0.99, discount: 0.5 }, minConfidence: 0.85 }

  it('lists fields under the threshold in form order', () => expect(unsureFields(r)).toEqual(['date', 'discount']))
  it('none once confirmed', () => expect(unsureFields({ ...r, status: 'Confirmed' })).toEqual([]))
  it('none for a receipt saved before scores existed', () =>
    expect(unsureFields({ status: 'Extracted', fieldConfidence: undefined as unknown as FieldConfidence, minConfidence: 0.85 })).toEqual([]))
})
