import { Trash2, UserPlus } from 'lucide-react'
import { PhoneInput } from './PhoneInput'

type PhoneListInputProps = {
  values: string[]
  onChange: (values: string[]) => void
  label: string
  firstPhoneLabel: string
  required?: boolean
}

export function PhoneListInput({ values, onChange, label, firstPhoneLabel, required }: PhoneListInputProps) {
  const phones = values

  function updatePhone(index: number, phone: string) {
    onChange(phones.map((current, currentIndex) => currentIndex === index ? phone : current))
  }

  function removePhone(index: number) {
    onChange(phones.filter((_, currentIndex) => currentIndex !== index))
  }

  return (
    <div className="form-field" role="group" aria-label={label}>
      <span className="form-field-label">{label}</span>
      {phones.map((phone, index) => (
        <div className="dictionary-row-actions" key={index}>
          <PhoneInput
            aria-label={index === 0 ? firstPhoneLabel : `${firstPhoneLabel}: телефон ${index + 1}`}
            required={required && index === 0}
            value={phone}
            onValueChange={(nextPhone) => updatePhone(index, nextPhone)}
          />
          {phones.length > 1 ? (
            <button
              className="icon-button"
              type="button"
              aria-label={`Удалить телефон ${index + 1}`}
              onClick={() => removePhone(index)}
            >
              <Trash2 size={16} />
            </button>
          ) : null}
        </div>
      ))}
      <button
        className="secondary-button"
        type="button"
        disabled={phones.length >= 10}
        onClick={() => onChange([...phones, ''])}
      >
        <UserPlus size={16} />
        Добавить телефон
      </button>
    </div>
  )
}
