// CSV export for the demo site. Mirrors Export/ReceiptCsv.cs: Excel-friendly (UTF-8 BOM, CRLF), plain-number
// amounts, dates as yyyyMMdd, and text cells that would run as a formula get a leading apostrophe.
import type { Receipt } from '../api'

const HEADER = ['Date', 'Merchant', 'Category', 'Total', 'VAT', 'VATable sales', 'VAT-exempt sales', 'Discount', 'Receipt']

const money = (n: number | null) => (n == null ? '' : n.toFixed(2))
const text = (s: string | null) => (s && '=+-@\t\r'.includes(s[0]) ? `'${s}` : (s ?? ''))
const quote = (cell: string) => (/[",\r\n]/.test(cell) ? `"${cell.replaceAll('"', '""')}"` : cell)

export function receiptsCsv(receipts: Receipt[]): string {
  const rows = receipts.map((r) => [
    r.date?.replaceAll('-', '') ?? '',
    text(r.merchant),
    text(r.category),
    money(r.total),
    money(r.vatAmount),
    money(r.vatableSales),
    money(r.vatExemptSales),
    money(r.discount),
    text(r.name),
  ])
  return [HEADER, ...rows].map((row) => row.map(quote).join(',') + '\r\n').join('')
}

/** A link that downloads the CSV, like GET /api/receipts/export.csv. */
export const csvDataUrl = (csv: string) => 'data:text/csv;charset=utf-8,' + encodeURIComponent('﻿' + csv)
