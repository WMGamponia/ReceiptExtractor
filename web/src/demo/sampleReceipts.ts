// Made-up confirmed receipts for the demo site's Spending page: every category, spread over the 12 months before
// today, so the demo never looks stale. Same stores and amounts as Seeding/SampleReceipts.cs (the Docker seed).
import type { Category, ReceiptUpdate } from '../api'
import { addDays } from './periods'

interface Store {
  merchant: string
  category: Category
  min: number
  max: number
  vat?: boolean
}

const STORES: Store[] = [
  { merchant: 'Jollibee', category: 'Food', min: 150, max: 900 },
  { merchant: 'Mang Inasal', category: 'Food', min: 180, max: 750 },
  { merchant: 'Chowking', category: 'Food', min: 150, max: 800 },
  { merchant: 'Starbucks', category: 'Food', min: 180, max: 650 },
  { merchant: 'SM Supermarket', category: 'Groceries', min: 800, max: 4500 },
  { merchant: 'Puregold', category: 'Groceries', min: 500, max: 3500 },
  { merchant: 'Aling Nena Sari-Sari Store', category: 'Groceries', min: 60, max: 400, vat: false },
  { merchant: 'Grab', category: 'Transport', min: 150, max: 650 },
  { merchant: 'Victory Liner', category: 'Transport', min: 450, max: 900 },
  { merchant: 'Petron', category: 'Fuel', min: 1000, max: 3000 },
  { merchant: 'Shell', category: 'Fuel', min: 800, max: 2500 },
  { merchant: 'Mercury Drug', category: 'Health', min: 200, max: 2500 },
  { merchant: 'Watsons', category: 'Health', min: 150, max: 1200 },
  { merchant: 'Meralco', category: 'Utilities', min: 1800, max: 5500 },
  { merchant: 'Maynilad', category: 'Utilities', min: 400, max: 1200 },
  { merchant: 'PLDT Home', category: 'Utilities', min: 1699, max: 1699 },
  { merchant: 'Lazada', category: 'Shopping', min: 300, max: 4000 },
  { merchant: 'Shopee', category: 'Shopping', min: 200, max: 2500 },
  { merchant: 'Uniqlo', category: 'Shopping', min: 790, max: 2990 },
  { merchant: 'National Book Store', category: 'Other', min: 150, max: 1200 },
  { merchant: 'LBC Express', category: 'Other', min: 180, max: 650 },
]

const CATEGORY_ORDER: Category[] = ['Food', 'Groceries', 'Transport', 'Fuel', 'Health', 'Utilities', 'Shopping', 'Other']
const COUNT = 40
const round2 = (n: number) => Math.round(n * 100) / 100

/** Small seeded random number generator (mulberry32), so every visitor gets the same receipts for a given day. */
function seeded(seed: number) {
  return () => {
    seed = (seed + 0x6d2b79f5) | 0
    let t = Math.imul(seed ^ (seed >>> 15), 1 | seed)
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296
  }
}

export function sampleReceipts(today: string): ReceiptUpdate[] {
  const random = seeded(20260928)
  const int = (below: number) => Math.floor(random() * below)

  return Array.from({ length: COUNT }, (_, i) => {
    // One store per category first, so every category shows up; then any store.
    const store = i < CATEGORY_ORDER.length ? STORES.find((s) => s.category === CATEGORY_ORDER[i])! : STORES[int(STORES.length)]
    const date = addDays(today, -int(365))
    const price = Math.round(store.min + random() * (store.max - store.min)) + int(4) * 0.25
    const base = { merchant: store.merchant, date, category: store.category, vatExemptSales: null, discount: null, serviceCharge: null }

    if (store.category === 'Health' && int(3) === 0) {
      // Senior citizen / PWD: VAT-exempt, then 20% off (exempt − discount = total).
      const discount = round2(price * 0.2)
      return { ...base, total: round2(price - discount), vatAmount: null, vatableSales: null, vatExemptSales: price, discount }
    }
    if (store.vat === false) return { ...base, total: price, vatAmount: null, vatableSales: null }

    const vat = round2((price * 12) / 112) // 12% VAT is included in PH totals
    return { ...base, total: price, vatAmount: vat, vatableSales: round2(price - vat) }
  })
}
