import { Trash2 } from 'lucide-react'
import { PhoneInput } from './PhoneInput'

type PhoneListInputProps = {
  values: string[]
  onChange: (values: string[]) => void
  label: string
  firstPhoneLabel: string
  required?: boolean
}

export function PhoneListInput({ values, onChange, label, firstPhoneLabel, required }: PhoneListInputProps) {
  function updatePhone(index: number, phone: string) {
    onChange(values.map((current, currentIndex) => currentIndex === index ? phone : current))
  }

  return (
    <div className="form-field phone-list" role="group" aria-label={label}>
      <div className="form-field">
        <span className="form-field-label">{label}</span>
        <div className="suggest">
          <PhoneInput
            aria-label={firstPhoneLabel}
            required={required}
            value={values[0]}
            onValueChange={(phone) => updatePhone(0, phone)}
          />
          <button
            className="field-btn"
            type="button"
            aria-label="Добавить телефон"
            disabled={values.length === 10}
            onClick={() => onChange([...values, ''])}
          >+</button>
        </div>
      </div>
      {values.slice(1).map((phone, index) => (
        <div className="suggest" key={index}>
          <PhoneInput
            aria-label={`${firstPhoneLabel}: телефон ${index + 2}`}
            value={phone}
            onValueChange={(nextPhone) => updatePhone(index + 1, nextPhone)}
          />
          <button
            className="field-btn danger-icon-button"
            type="button"
            aria-label={`Удалить телефон ${index + 2}`}
            onClick={() => onChange(values.filter((_, currentIndex) => currentIndex !== index + 1))}
          >
            <Trash2 size={15} aria-hidden="true" />
          </button>
        </div>
      ))}
    </div>
  )
}
