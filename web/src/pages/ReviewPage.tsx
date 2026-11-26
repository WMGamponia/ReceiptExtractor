import { useState, type FormEvent, type ReactNode } from 'react'
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom'
import { api, ApiError, CATEGORIES, isBusy, type LockableField, type Receipt, type ReceiptUpdate } from '../api'
import { ImageViewer } from '../ImageViewer'
import { renderRotated, type Rotation } from '../imageTools'
import { ErrorBox, Flags, StatusBadge } from '../components'
import {
  formatMoneyInput,
  parseMoney,
  shiftDecimal,
  todayIso,
  vatComplement,
  vatFromTotal,
  vatWarnings,
  type Amounts,
} from '../format'
import { useReceipt } from '../useReceipt'
import { formatConfidence, unsureFields } from '../confidence'

/** Where "back to the list" goes: the list tab the user came from (see ReceiptsPage), else the default tab. */
function useBackToList(): string {
  const back = (useLocation().state as { back?: string } | null)?.back
  return back?.startsWith('/receipts') ? back : '/receipts'
}

export function ReviewPage() {
  const { id = '' } = useParams()
  const { receipt, setReceipt, error } = useReceipt(id)
  const [imageVersion, setImageVersion] = useState(0) // bumped after saving a rotation to reload the image
  const [rereadWith, setRereadWith] = useState<LockableField[]>() // fields kept in the last re-extract

  if (error && !receipt) return <ErrorBox message={error} />
  if (!receipt) return <p className="muted">Loading…</p>

  const imageSrc = imageVersion && !receipt.imageUrl.startsWith('blob:') ? `${receipt.imageUrl}?v=${imageVersion}` : receipt.imageUrl

  async function saveRotation(rotation: Rotation) {
    const { blob, fileName } = await renderRotated(imageSrc, rotation)
    setReceipt(await api.replaceImage(receipt!.id, blob, fileName))
    setImageVersion(Date.now())
  }

  return (
    <section>
      <div className="page-header">
        <h1>{isBusy(receipt) ? receipt.originalFileName : receipt.name}</h1>
        <span className="header-badges">
          <StatusBadge status={receipt.status} />
          {receipt.confidence != null && (
            <span className="badge badge-confidence" title="How sure the model was of the dates and amounts it read (its least sure one). You can't change it.">
              Confidence {formatConfidence(receipt.confidence)}
            </span>
          )}
        </span>
      </div>

      <div className="review">
        <div className="review-image">
          <ImageViewer
            src={imageSrc}
            alt={`Receipt ${receipt.originalFileName}`}
            onSaveRotation={saveRotation}
            saveDisabledReason={isBusy(receipt) ? 'You can save once the model has finished reading it.' : undefined}
          />
        </div>

        <div className="review-panel">
          {isBusy(receipt) && (
            <p className="muted">
              {rereadWith
                ? `The model is re-reading this receipt, keeping your ${listFields(rereadWith)}…`
                : 'The model is reading this receipt…'}
            </p>
          )}
          {rereadWith && !isBusy(receipt) && receipt.status !== 'Failed' && (
            <div className="notice notice-ok" role="status">
              Re-read using your {listFields(rereadWith)}. The other fields are the model's new answer — check them,
              then confirm.
            </div>
          )}
          {receipt.status === 'Failed' && <FailedPanel receipt={receipt} onRetried={setReceipt} />}
          {!isBusy(receipt) && receipt.status !== 'Failed' && (
            // key: re-initialise the form if the receipt is re-extracted
            <ReviewForm
              key={`${receipt.id}-${receipt.status}`}
              receipt={receipt}
              onSaved={setReceipt}
              onReextract={(r, locked) => {
                setRereadWith(locked)
                setReceipt(r)
              }}
            />
          )}
          <DeleteReceipt receipt={receipt} />
        </div>
      </div>
    </section>
  )
}

function FailedPanel({ receipt, onRetried }: { receipt: Receipt; onRetried: (r: Receipt) => void }) {
  const [error, setError] = useState<string>()
  return (
    <>
      <ErrorBox message={`Extraction failed: ${receipt.error ?? 'unknown error'}`} />
      <ErrorBox message={error} />
      <button
        className="primary"
        onClick={() => api.retryReceipt(receipt.id).then(onRetried, (e: Error) => setError(e.message))}
      >
        Try again
      </button>
    </>
  )
}

function DeleteReceipt({ receipt }: { receipt: Receipt }) {
  const navigate = useNavigate()
  const back = useBackToList()
  const [asking, setAsking] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [error, setError] = useState<string>()
  const name = isBusy(receipt) ? receipt.originalFileName : receipt.name
  const processing = receipt.status === 'Processing'

  async function remove() {
    setDeleting(true)
    setError(undefined)
    try {
      await api.deleteReceipt(receipt.id)
      navigate(back, { state: { deleted: name } })
    } catch (e) {
      setError((e as Error).message)
      setDeleting(false)
    }
  }

  return (
    <div className="danger-zone">
      <ErrorBox message={error} />
      {!asking ? (
        <button
          className="small danger"
          onClick={() => setAsking(true)}
          disabled={processing}
          title={processing ? 'You can delete it once the model has finished reading it.' : undefined}
        >
          Delete receipt
        </button>
      ) : (
        <div className="notice notice-error" role="alert">
          <p>
            Delete “{name}” and its photo? This can't be undone.
          </p>
          <div className="button-row">
            <button className="small danger-solid" onClick={remove} disabled={deleting}>
              {deleting ? 'Deleting…' : 'Delete'}
            </button>
            <button className="small" onClick={() => setAsking(false)} disabled={deleting}>
              Keep it
            </button>
          </div>
        </div>
      )}
    </div>
  )
}

interface FormState {
  merchant: string
  date: string
  total: string
  vatAmount: string
  vatableSales: string
  vatExemptSales: string
  discount: string
  serviceCharge: string
  category: string
}

const money = formatMoneyInput

type VatField = 'vatAmount' | 'vatableSales'
const otherVatField = (f: VatField): VatField => (f === 'vatAmount' ? 'vatableSales' : 'vatAmount')

const amountsOf = (f: FormState): Amounts => ({
  total: parseMoney(f.total),
  vat: parseMoney(f.vatAmount),
  vatable: parseMoney(f.vatableSales),
  exempt: parseMoney(f.vatExemptSales),
  discount: parseMoney(f.discount),
  serviceCharge: parseMoney(f.serviceCharge),
})

/** Works out `field` from the other VAT field (plus total, exempt sales, discount and service charge). */
const complementOf = (f: FormState, field: VatField) =>
  vatComplement(amountsOf(f), field === 'vatAmount' ? 'vatable' : 'vat')

/** Form field -> the backend's field name for re-extract, and how to describe it. */
const LOCKABLE: Record<keyof FormState, { name: LockableField; label: string; money: boolean }> = {
  merchant: { name: 'merchant', label: 'merchant', money: false },
  date: { name: 'date', label: 'date', money: false },
  total: { name: 'total', label: 'total', money: true },
  vatAmount: { name: 'vat_amount', label: 'VAT', money: true },
  vatableSales: { name: 'vatable_sales', label: 'VATable sales', money: true },
  vatExemptSales: { name: 'vat_exempt_sales', label: 'VAT-exempt sales', money: true },
  discount: { name: 'discount', label: 'discount', money: true },
  serviceCharge: { name: 'service_charge', label: 'service charge', money: true },
  category: { name: 'category', label: 'category', money: false },
}

const LABEL_OF = Object.fromEntries(Object.values(LOCKABLE).map((f) => [f.name, f.label])) as Record<LockableField, string>

/** "total, VAT and discount" */
function listFields(fields: LockableField[]): string {
  const labels = fields.map((f) => LABEL_OF[f])
  return labels.length < 2 ? labels.join('') : `${labels.slice(0, -1).join(', ')} and ${labels[labels.length - 1]}`
}

interface ReviewFormProps {
  receipt: Receipt
  onSaved: (r: Receipt) => void
  onReextract: (r: Receipt, locked: LockableField[]) => void
}

function ReviewForm({ receipt, onSaved, onReextract }: ReviewFormProps) {
  const navigate = useNavigate()
  const back = useBackToList()
  const [initial] = useState<FormState>(() => ({
    merchant: receipt.merchant ?? '',
    date: receipt.date ?? '',
    total: money(receipt.total),
    vatAmount: money(receipt.vatAmount),
    vatableSales: money(receipt.vatableSales),
    vatExemptSales: money(receipt.vatExemptSales),
    discount: money(receipt.discount),
    serviceCharge: money(receipt.serviceCharge),
    category: receipt.category ?? '',
  }))
  const [form, setForm] = useState<FormState>(initial)
  const [rereading, setRereading] = useState(false)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string>()
  const [fieldErrors, setFieldErrors] = useState<Record<string, string[]>>({})

  // Which VAT field we filled in ourselves (so the breakdown adds up to the total). It keeps following
  // the other amounts until the user types in it.
  const [autoFilled, setAutoFilled] = useState<VatField | null>(null)

  const set = (field: keyof FormState) => (value: string) => setForm((f) => ({ ...f, [field]: value }))
  const unsure = new Set(unsureFields(receipt))
  const uploadDate = todayIso(new Date(receipt.uploadedAtUtc))

  function setVatField(field: VatField, value: string) {
    const counterpart = otherVatField(field)
    const counterpartValue = parseMoney(form[counterpart])
    const next = { ...form, [field]: value }
    let nextAuto = autoFilled === field ? null : autoFilled // typing in a filled-in field makes it the user's

    // Only fill a field that's empty/0 or that we filled before — never overwrite what the user typed.
    if (autoFilled === counterpart || counterpartValue == null || counterpartValue === 0) {
      const filled = complementOf(next, counterpart)
      if (filled != null) {
        next[counterpart] = money(filled)
        nextAuto = counterpart
      } else if (autoFilled === counterpart) {
        next[counterpart] = '' // e.g. the user cleared the field we were following
        nextAuto = null
      }
    }

    setForm(next)
    setAutoFilled(nextAuto)
  }

  /** Total, VAT-exempt sales, discount or service charge changed: keep a filled-in VAT field in step. */
  function setAmount(field: 'total' | 'vatExemptSales' | 'discount' | 'serviceCharge', value: string) {
    const next = { ...form, [field]: value }
    if (autoFilled) next[autoFilled] = money(complementOf(next, autoFilled))
    setForm(next)
  }

  const amounts = amountsOf(form)
  const { total, vat, vatable, exempt, discount, serviceCharge } = amounts
  // No VAT lines read (faded receipt, or the model missed them): offer to split the VAT-inclusive total.
  const splitFromTotal = !vat && !vatable ? vatFromTotal(amounts) : null

  function fillVatFromTotal() {
    if (!splitFromTotal) return
    setForm((f) => ({ ...f, vatAmount: money(splitFromTotal.vat), vatableSales: money(splitFromTotal.vatable) }))
    setAutoFilled(null) // the user asked for these, so they count as their values
  }
  const liveWarnings = vatWarnings(amounts)
  const futureDate = form.date !== '' && form.date > todayIso()

  const toUpdate = (): ReceiptUpdate => ({
    merchant: form.merchant.trim() || null,
    date: form.date || null,
    total,
    vatAmount: vat,
    vatableSales: vatable,
    vatExemptSales: exempt,
    discount,
    serviceCharge: serviceCharge ?? 0, // 0, not null: the API keeps the stored value when it's null
    category: form.category || null,
  })

  // Fields the user changed by hand. A VAT field we filled in ourselves isn't locked: it's only derived,
  // so the model gets to read it again.
  const changed = (Object.keys(LOCKABLE) as (keyof FormState)[])
    .filter((key) => key !== autoFilled)
    .filter((key) =>
      LOCKABLE[key].money
        ? (parseMoney(form[key]) ?? 0) !== (parseMoney(initial[key]) ?? 0)
        : form[key].trim() !== initial[key].trim(),
    )
    .map((key) => LOCKABLE[key].name)

  async function reextract() {
    setError(undefined)
    setFieldErrors({})
    if ([total, vat, vatable, exempt, discount, serviceCharge].some((n) => Number.isNaN(n))) {
      setError('Amounts must be numbers, e.g. 1,124.46')
      return
    }

    setRereading(true)
    try {
      onReextract(await api.reextractReceipt(receipt.id, toUpdate(), changed), changed)
    } catch (e) {
      if (e instanceof ApiError) setFieldErrors(e.fieldErrors)
      setError((e as Error).message)
      setRereading(false)
    }
  }

  async function submit(e: FormEvent) {
    e.preventDefault()
    setError(undefined)
    setFieldErrors({})

    if ([total, vat, vatable, exempt, discount, serviceCharge].some((n) => Number.isNaN(n))) {
      setError('Amounts must be numbers, e.g. 1,124.46')
      return
    }

    setSaving(true)
    try {
      const saved = await api.confirmReceipt(receipt.id, toUpdate())
      onSaved(saved)
      navigate(back, { state: { confirmed: saved.name } })
    } catch (e) {
      if (e instanceof ApiError) setFieldErrors(e.fieldErrors)
      setError((e as Error).message)
    } finally {
      setSaving(false)
    }
  }

  const fieldError = (name: string) => fieldErrors[name]?.join(' ')

  return (
    <form onSubmit={submit} className="form">
      {receipt.status === 'NeedsReview' && <Flags flags={receipt.flags} title="The model's answer failed these checks" />}
      {receipt.status === 'Confirmed' && receipt.corrections.length > 0 && (
        <Flags flags={receipt.corrections} title="You corrected" />
      )}

      <Field label="Merchant" hint="Store or company name">
        <input value={form.merchant} onChange={(e) => set('merchant')(e.target.value)} />
      </Field>

      <Field
        label="Date"
        required
        unsure={unsure.has('date')}
        error={fieldError('date') ?? (futureDate ? 'This date is in the future — day and month swapped?' : undefined)}
      >
        <input type="date" value={form.date} onChange={(e) => set('date')(e.target.value)} required />
      </Field>
      {unsure.has('date') && form.date !== uploadDate && (
        <p className="field-action">
          <button type="button" className="link" onClick={() => set('date')(uploadDate)}>
            Use upload date ({uploadDate})
          </button>
        </p>
      )}

      <Field label="Total (₱)" required unsure={unsure.has('total')} error={fieldError('total')}>
        <MoneyInput value={form.total} onChange={(v) => setAmount('total', v)} required />
      </Field>

      <div className="field-pair">
        <Field label="VAT (₱)" unsure={unsure.has('vat_amount')} hint={autoFilled === 'vatAmount' ? 'auto-filled' : '0 if non-VAT'}>
          <MoneyInput value={form.vatAmount} onChange={(v) => setVatField('vatAmount', v)} />
        </Field>
        <Field label="VATable sales (₱)" unsure={unsure.has('vatable_sales')} hint={autoFilled === 'vatableSales' ? 'auto-filled' : undefined}>
          <MoneyInput value={form.vatableSales} onChange={(v) => setVatField('vatableSales', v)} />
        </Field>
      </div>
      {splitFromTotal && (
        <p className="field-action">
          <button type="button" className="link" onClick={fillVatFromTotal}>
            Work out VAT from the total
          </button>
          <span className="muted">
            12% included: {formatMoneyInput(splitFromTotal.vatable)} + {formatMoneyInput(splitFromTotal.vat)} VAT
          </span>
        </p>
      )}

      <Field label="VAT-exempt sales (₱)" unsure={unsure.has('vat_exempt_sales')} hint="e.g. SC / PWD" error={fieldError('vatExemptSales')}>
        <MoneyInput value={form.vatExemptSales} onChange={(v) => setAmount('vatExemptSales', v)} />
      </Field>
      <div className="field-pair">
        <Field label="Discount (₱)" unsure={unsure.has('discount')} hint="all, added up" error={fieldError('discount')}>
          <MoneyInput value={form.discount} onChange={(v) => setAmount('discount', v)} />
        </Field>
        <Field label="Service charge (₱)" unsure={unsure.has('service_charge')} hint="e.g. 10% at restaurants" error={fieldError('serviceCharge')}>
          <MoneyInput value={form.serviceCharge} onChange={(v) => setAmount('serviceCharge', v)} />
        </Field>
      </div>

      {liveWarnings.length > 0 && <Flags flags={liveWarnings} title="VAT doesn't add up" />}

      <Field label="Category" required error={fieldError('category')}>
        <select value={form.category} onChange={(e) => set('category')(e.target.value)} required>
          <option value="" disabled>
            Pick one…
          </option>
          {CATEGORIES.map((c) => (
            <option key={c}>{c}</option>
          ))}
        </select>
      </Field>

      <ErrorBox message={error} />

      <div className="button-row">
        <button type="submit" className="primary" disabled={saving || rereading}>
          {saving ? 'Saving…' : receipt.status === 'Confirmed' ? 'Save changes' : 'Confirm'}
        </button>
        <button
          type="button"
          onClick={reextract}
          disabled={changed.length === 0 || saving || rereading}
          title={
            changed.length === 0
              ? 'Correct a field first; the model then re-reads the rest using it'
              : 'Keep your changes as correct and let the model re-read the other fields'
          }
        >
          {rereading ? 'Starting…' : 'Re-extract'}
        </button>
        <Link className="button" to={back}>
          Cancel
        </Link>
      </div>
      {changed.length > 0 && (
        <p className="muted small-print">
          Re-extract keeps your {listFields(changed)} and re-reads the other fields with them.
        </p>
      )}
    </form>
  )
}

/** Money input with buttons to move the decimal point, for amounts read as 112446 instead of 1124.46. */
function MoneyInput(props: { value: string; onChange: (value: string) => void; required?: boolean }) {
  const amount = parseMoney(props.value)
  const canShift = amount != null && !Number.isNaN(amount) && amount !== 0
  const shift = (direction: 'left' | 'right') => props.onChange(shiftDecimal(props.value, direction))
  return (
    <span className="money-input">
      <input inputMode="decimal" value={props.value} onChange={(e) => props.onChange(e.target.value)} required={props.required} />
      {canShift && (
        <span className="shifts">
          <button
            type="button"
            className="shift"
            onClick={() => shift('left')}
            title="Move the decimal point left (÷10)"
            aria-label="Move the decimal point left"
          >
            ←.
          </button>
          <button
            type="button"
            className="shift"
            onClick={() => shift('right')}
            title="Move the decimal point right (×10)"
            aria-label="Move the decimal point right"
          >
            .→
          </button>
        </span>
      )}
    </span>
  )
}

function Field(props: { label: string; hint?: string; required?: boolean; error?: string; unsure?: boolean; children: ReactNode }) {
  return (
    <label className={`field ${props.error ? 'field-invalid' : ''}`}>
      <span className="field-label">
        {props.label}
        {props.required && <span className="required"> *</span>}
        {props.unsure && (
          <span className="unsure" title="Model unsure — check against the photo">
            {' '}● unsure
          </span>
        )}
        {props.hint && <span className="muted"> · {props.hint}</span>}
      </span>
      {props.children}
      {props.error && <span className="text-error">{props.error}</span>}
    </label>
  )
}
