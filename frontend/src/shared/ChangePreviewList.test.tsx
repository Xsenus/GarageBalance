import { render, screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

import { ChangePreviewList } from './ChangePreviewList'

describe('ChangePreviewList', () => {
  it('renders every changed field in an accessible named list', () => {
    render(<ChangePreviewList ariaLabel="Изменяемые поля платежа" changes={[
      { field: 'Сумма', before: '100,00 руб.', after: '150,00 руб.' },
      { field: 'Комментарий', before: 'пусто', after: 'Оплачено' },
    ]} />)

    const list = screen.getByRole('list', { name: 'Изменяемые поля платежа' })
    const items = within(list).getAllByRole('listitem')

    expect(items).toHaveLength(2)
    expect(items[0]).toHaveTextContent('Сумма100,00 руб.->150,00 руб.')
    expect(items[1]).toHaveTextContent('Комментарийпусто->Оплачено')
  })
})
