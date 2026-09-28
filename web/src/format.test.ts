import { describe, expect, it } from 'vitest'
import { formatDate, formatPeso, parseMoney, shortPeriodLabel, todayIso, vatComplement, vatWarnings } from './format'

describe('formatPeso', () => {
  it('formats pesos with centavos', () => expect(formatPeso(10495)).toBe('₱10,495.00'))
  it('shows a dash for missing values', () => expect(formatPeso(null)).toBe('—'))
})

describe('formatDate', () => {
  it('does not shift the day across timezones', () => expect(formatDate('2025-12-20')).toContain('20'))
  it('shows a dash for missing dates', () => expect(formatDate(null)).toBe('—'))
})

describe('todayIso', () => {
  it('uses the local calendar date', () => expect(todayIso(new Date(2026, 0, 5, 23, 59))).toBe('2026-01-05'))
})

describe('parseMoney', () => {
  it('accepts commas and peso signs', () => expect(parseMoney('₱10,495.00')).toBe(10495))
  it('treats empty as null', () => expect(parseMoney('  ')).toBeNull())
  it('returns NaN for junk so the form can reject it', () => expect(parseMoney('abc')).toBeNaN())
})

describe('shortPeriodLabel', () => {
  it.each([
    ['2026-04', 'Apr'],
    ['2026-W14', 'W14'],
    ['2026-Q1', 'Q1'],
    ['2026-09-07', '7'],
    ['2026', '2026'],
  ])('%s -> %s', (label, short) => expect(shortPeriodLabel(label)).toBe(short))
})

const amounts = (total: number | null, vat: number | null, vatable: number | null, exempt = 0, discount = 0) => ({
  total,
  vat,
  vatable,
  exempt,
  discount,
})

describe('vatWarnings (mirrors ReceiptValidator.cs)', () => {
  it('passes a real DataBlitz receipt', () => expect(vatWarnings(amounts(10495, 1124.46, 9370.54))).toEqual([]))
  it('skips non-VAT receipts', () => expect(vatWarnings(amounts(27840, 0, 0))).toEqual([]))
  it('flags dropped centavos', () => expect(vatWarnings(amounts(10495, 1124, 9370))).toHaveLength(2))
  it('flags misread digits', () => expect(vatWarnings(amounts(15190, 827.5, 6552.5))).toHaveLength(2))
  it('passes a senior citizen receipt (500 exempt − 100 discount = 400)', () =>
    expect(vatWarnings(amounts(400, 0, 0, 500, 100))).toEqual([]))
  it('passes mixed regular + senior citizen items', () => expect(vatWarnings(amounts(512, 12, 100, 500, 100))).toEqual([]))
  it('passes a promo discount already inside the breakdown', () =>
    expect(vatWarnings(amounts(1000, 107.14, 892.86, 0, 50))).toEqual([]))
  it('flags an exempt breakdown that does not add up', () =>
    expect(vatWarnings(amounts(400, 0, 0, 500, 80))[0]).toContain('VAT-exempt sales'))
})

describe('vatComplement', () => {
  it('fills VATable sales from VAT', () => expect(vatComplement(amounts(10495, 1124.46, null), 'vat')).toBe(9370.54))
  it('fills VAT from VATable sales', () => expect(vatComplement(amounts(10495, null, 9370.54), 'vatable')).toBe(1124.46))
  it('takes VAT-exempt sales and the SC discount into account', () =>
    expect(vatComplement(amounts(512, 12, null, 500, 100), 'vat')).toBe(100))
  it('ignores a promo discount when the breakdown already adds up', () =>
    expect(vatComplement(amounts(1000, 107.14, null, 0, 50), 'vat')).toBe(892.86))
  it('needs a total', () => expect(vatComplement(amounts(null, 1124.46, null), 'vat')).toBeNull())
  it('ignores zero (non-VAT receipts)', () => expect(vatComplement(amounts(10495, 0, null), 'vat')).toBeNull())
  it('ignores amounts above the total', () => expect(vatComplement(amounts(100, 150, null), 'vat')).toBeNull())
  it('ignores junk input', () => expect(vatComplement(amounts(100, NaN, null), 'vat')).toBeNull())
})
