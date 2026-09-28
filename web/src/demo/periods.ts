// Summary periods for the demo site, which has no API. Mirrors Summaries/PeriodRange.cs and SummaryEndpoints.cs:
// ISO weeks (Monday–Sunday), calendar quarters, `end` exclusive, confirmed receipts only.
import type { Period, Receipt, Summary } from '../api'

// Dates are 'YYYY-MM-DD' strings; the maths is done in UTC so time zones never shift a day.
const toDate = (iso: string) => new Date(`${iso}T00:00:00Z`)
const toIso = (d: Date) => d.toISOString().slice(0, 10)

export function addDays(iso: string, days: number): string {
  const d = toDate(iso)
  d.setUTCDate(d.getUTCDate() + days)
  return toIso(d)
}

function addMonths(iso: string, months: number): string {
  const d = toDate(iso)
  d.setUTCMonth(d.getUTCMonth() + months) // only used on the 1st of a month, so no end-of-month overflow
  return toIso(d)
}

/** ISO 8601 week-numbering year and week of the Monday `monday`. */
function isoWeek(monday: string): { year: number; week: number } {
  const thursday = toDate(addDays(monday, 3)) // the week belongs to the year its Thursday is in
  const year = thursday.getUTCFullYear()
  const week = Math.floor((thursday.getTime() - Date.UTC(year, 0, 1)) / 86_400_000 / 7) + 1
  return { year, week }
}

export interface PeriodRange {
  period: Period
  start: string
  end: string // exclusive
  label: string
}

export function periodFor(period: Period, day: string): PeriodRange {
  const [y, m] = day.split('-').map(Number)
  switch (period) {
    case 'Daily':
      return { period, start: day, end: addDays(day, 1), label: day }
    case 'Weekly': {
      const monday = addDays(day, -((toDate(day).getUTCDay() + 6) % 7)) // getUTCDay: Sunday = 0
      const { year, week } = isoWeek(monday)
      return { period, start: monday, end: addDays(monday, 7), label: `${year}-W${String(week).padStart(2, '0')}` }
    }
    case 'Monthly': {
      const start = day.slice(0, 7) + '-01'
      return { period, start, end: addMonths(start, 1), label: day.slice(0, 7) }
    }
    case 'Quarterly': {
      const q = Math.floor((m - 1) / 3)
      const start = `${y}-${String(q * 3 + 1).padStart(2, '0')}-01`
      return { period, start, end: addMonths(start, 3), label: `${y}-Q${q + 1}` }
    }
    case 'Yearly':
      return { period, start: `${y}-01-01`, end: `${y + 1}-01-01`, label: String(y) }
  }
}

const HISTORY_LENGTH = 6
const round2 = (n: number) => Math.round(n * 100) / 100

export function summarize(receipts: Receipt[], period: Period, day: string): Summary {
  const current = periodFor(period, day)
  const ranges = [current]
  while (ranges.length < HISTORY_LENGTH) ranges.unshift(periodFor(period, addDays(ranges[0].start, -1)))

  const confirmed = receipts.filter((r) => r.status === 'Confirmed' && r.date)
  const within = (range: PeriodRange) => confirmed.filter((r) => r.date! >= range.start && r.date! < range.end)
  const sum = (list: Receipt[], pick: (r: Receipt) => number | null) => round2(list.reduce((s, r) => s + (pick(r) ?? 0), 0))

  const inCurrent = within(current)
  const groups = new Map<string, Receipt[]>()
  for (const r of inCurrent) groups.set(r.category ?? 'Other', [...(groups.get(r.category ?? 'Other') ?? []), r])
  const byCategory = [...groups]
    .map(([category, list]) => ({ category, total: sum(list, (r) => r.total), count: list.length }))
    .sort((a, b) => b.total - a.total)

  return {
    period,
    label: current.label,
    start: current.start,
    end: current.end,
    total: sum(inCurrent, (r) => r.total),
    vatTotal: sum(inCurrent, (r) => r.vatAmount),
    count: inCurrent.length,
    byCategory,
    history: ranges.map((range) => {
      const list = within(range)
      return { label: range.label, start: range.start, total: sum(list, (r) => r.total), count: list.length }
    }),
    previous: periodFor(period, addDays(current.start, -1)).start,
    next: periodFor(period, current.end).start,
  }
}
