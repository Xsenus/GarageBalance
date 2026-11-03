export function AnnualAccrualHoldNotice({ years }: { years: number[] }) {
  if (!years.length) return null
  return (
    <p role="status" className="form-hint">
      Годовые начисления за {years.join(', ')} год приостановлены по решению владельца.
      Месячные начисления продолжаются с установленного начала учёта гаража.
    </p>
  )
}
