import { render, screen } from '@testing-library/react'
import { FormError, FormValidationSummary } from './formFeedback'

describe('form feedback shared UI', () => {
  it('renders form errors as alerts', () => {
    render(<FormError>Ошибка сохранения</FormError>)

    expect(screen.getByRole('alert')).toHaveTextContent('Ошибка сохранения')
  })

  it('hides empty validation summary', () => {
    const { container } = render(<FormValidationSummary title="Проверьте форму" items={[]} />)

    expect(container).toBeEmptyDOMElement()
  })

  it('renders validation summary as a named alert', () => {
    render(<FormValidationSummary title="Проверьте форму" items={['Укажите дату', 'Укажите сумму']} />)

    const alert = screen.getByRole('alert', { name: 'Проверьте форму' })
    expect(alert).toHaveTextContent('Укажите дату')
    expect(alert).toHaveTextContent('Укажите сумму')
  })
})
