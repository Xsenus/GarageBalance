export function NegativeFundBalanceConfirmation({ visible, checked, disabled, onChange }: {
  visible: boolean
  checked: boolean
  disabled: boolean
  onChange: (checked: boolean) => void
}) {
  if (!visible) return null
  return (
    <label className="payments-negative-fund-confirmation">
      <input type="checkbox" aria-label="Подтвердить отрицательный остаток фонда" checked={checked} disabled={disabled} onChange={(event) => onChange(event.target.checked)} />
      <span>
        <strong>После выплаты фонд станет отрицательным.</strong>
        <small>Банк будет проверен отдельно. Подтверждение сохранится в истории изменений.</small>
      </span>
    </label>
  )
}
