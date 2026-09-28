import { describe, expect, it } from 'vitest'
import vatCases from '../../tests/vat-cases.json'
import {
  csvFileName,
  formatDate,
  formatMoneyInput,
  formatPeso,
  parseMoney,
  shiftDecimal,
  shortPeriodLabel,
  todayIso,
  vatComplement,
  vatFromTotal,
  vatWarnings,
} from './format'

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

describe('formatMoneyInput', () => {
  it('adds thousands separators and centavos', () => expect(formatMoneyInput(10495)).toBe('10,495.00'))
  it('rounds to centavos', () => expect(formatMoneyInput(1124.456)).toBe('1,124.46'))
  it('is empty for missing values', () => expect(formatMoneyInput(null)).toBe(''))
  it('round-trips through parseMoney', () => expect(parseMoney(formatMoneyInput(1234567.8))).toBe(1234567.8))
})

describe('shiftDecimal', () => {
  it('moves the point left (dropped decimal point)', () => expect(shiftDecimal('112446.00', 'left')).toBe('11,244.60'))
  it('keeps shifting without losing digits', () => expect(shiftDecimal('1,124.46', 'left')).toBe('112.446'))
  it('moves the point right', () => expect(shiftDecimal('112.446', 'right')).toBe('1,124.46'))
  it('has no floating-point noise', () => expect(shiftDecimal('1,124.46', 'right')).toBe('11,244.60'))
  it('handles small amounts', () => expect(shiftDecimal('0.5', 'left')).toBe('0.05'))
  it('leaves empty fields alone', () => expect(shiftDecimal('', 'left')).toBe(''))
  it('leaves junk alone', () => expect(shiftDecimal('abc', 'right')).toBe('abc'))
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

  // Same cases as ReceiptValidator.VatFlags (C#) and vat_check (eval), so the three can't drift apart.
  it.each(vatCases.cases)('matches the shared case: $name', (c) =>
    expect(vatWarnings({ total: c.total, vat: c.vat, vatable: c.vatable, exempt: c.exempt, discount: c.discount })).toHaveLength(
      c.problems,
    ))
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

describe('vatFromTotal (12% VAT included in the total)', () => {
  it('splits a VAT-inclusive total', () => expect(vatFromTotal(amounts(10495, null, null))).toEqual({ vat: 1124.46, vatable: 9370.54 }))
  it('always adds up exactly to the total', () => {
    const r = vatFromTotal(amounts(100, null, null))!
    expect(r.vat + r.vatable).toBeCloseTo(100, 10)
    expect(r).toEqual({ vat: 10.71, vatable: 89.29 })
  })
  it('leaves VAT-exempt sales out of the split', () =>
    expect(vatFromTotal(amounts(612, null, null, 500))).toEqual({ vat: 12, vatable: 100 }))
  it('needs a total', () => expect(vatFromTotal(amounts(null, null, null))).toBeNull())
  it('rejects junk', () => expect(vatFromTotal(amounts(NaN, null, null))).toBeNull())
  it('needs something left after exempt sales', () => expect(vatFromTotal(amounts(500, null, null, 500))).toBeNull())
})

describe('csvFileName (mirrors ReceiptCsv.FileName)', () => {
  it('names a month by its first and last day', () =>
    expect(csvFileName('2026-09-01', '2026-10-01')).toBe('receipts_2026-09-01_to_2026-09-30.csv'))
  it('names a single day once', () => expect(csvFileName('2026-09-28', '2026-09-29')).toBe('receipts_2026-09-28.csv'))
})
