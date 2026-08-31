// @vitest-environment jsdom

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { AsyncErrorState } from './AsyncState'
import { FormError, FormValidationSummary } from './formFeedback'

function Dialog({ label, children }: { label: string; children?: React.ReactNode }) {
  return (
    <section className="detail-dialog" role="dialog" aria-modal="true" aria-label={label}>
      <header className="detail-dialog-header"><h2>{label}</h2></header>
      <div data-testid={`${label}-content`}>{children}</div>
    </section>
  )
}

function AlertDialog({ label, children }: { label: string; children?: React.ReactNode }) {
  return (
    <section className="detail-dialog" role="alertdialog" aria-modal="true" aria-label={label}>
      <header className="detail-dialog-header"><h2>{label}</h2></header>
      {children}
    </section>
  )
}

describe('foreground dialog errors', () => {
  it('keeps an error in its natural place when no modal dialog is open', () => {
    const { container } = render(<FormError>Не удалось загрузить данные.</FormError>)

    expect(screen.getByRole('alert')).toHaveTextContent('Не удалось загрузить данные.')
    expect(container.querySelector('.foreground-dialog-error-host')).not.toBeInTheDocument()
  })

  it('moves a background error into the foreground dialog immediately after its header', () => {
    render(
      <>
        <main data-testid="background"><FormError>Не удалось сохранить запись.</FormError></main>
        <Dialog label="Редактирование" />
      </>,
    )

    const dialog = screen.getByRole('dialog', { name: 'Редактирование' })
    const alert = screen.getByRole('alert')
    const host = dialog.querySelector<HTMLElement>('.foreground-dialog-error-host')

    expect(host).toContainElement(alert)
    expect(dialog.firstElementChild?.nextElementSibling).toBe(host)
    expect(screen.getByTestId('background')).not.toContainElement(alert)
  })

  it('keeps an error that already belongs to the foreground dialog in its form position', () => {
    render(<Dialog label="Удаление"><FormError>Удаление не выполнено.</FormError></Dialog>)

    const dialog = screen.getByRole('dialog', { name: 'Удаление' })
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Удаление не выполнено.')
    expect(dialog.querySelector('.foreground-dialog-error-host')).not.toBeInTheDocument()
    expect(screen.getByTestId('Удаление-content')).toContainElement(within(dialog).getByRole('alert'))
  })

  it('moves an existing error to a newly opened upper dialog and returns it after dialogs close', async () => {
    const { rerender } = render(
      <>
        <main data-testid="background"><FormError>Конфликт изменений.</FormError></main>
        <Dialog label="Основное окно" />
      </>,
    )

    rerender(
      <>
        <main data-testid="background"><FormError>Конфликт изменений.</FormError></main>
        <Dialog label="Основное окно" />
        <Dialog label="Подтверждение" />
      </>,
    )

    await waitFor(() => {
      expect(within(screen.getByRole('dialog', { name: 'Подтверждение' })).getByRole('alert')).toHaveTextContent('Конфликт изменений.')
    })

    rerender(<main data-testid="background"><FormError>Конфликт изменений.</FormError></main>)

    await waitFor(() => {
      expect(screen.getByTestId('background')).toContainElement(screen.getByRole('alert'))
    })
    expect(document.querySelector('.foreground-dialog-error-host')).not.toBeInTheDocument()
  })

  it('moves validation summaries and retry actions together with their errors', () => {
    const retry = vi.fn()
    render(
      <>
        <main>
          <FormValidationSummary title="Проверьте данные" items={['Укажите дату.']} />
          <AsyncErrorState message="Список не загружен." onRetry={retry} />
        </main>
        <Dialog label="Настройки" />
      </>,
    )

    const dialog = screen.getByRole('dialog', { name: 'Настройки' })
    expect(within(dialog).getByRole('alert', { name: 'Проверьте данные' })).toHaveTextContent('Укажите дату.')
    expect(within(dialog).getByRole('alert', { name: '' })).toHaveTextContent('Список не загружен.')

    fireEvent.click(within(dialog).getByRole('button', { name: 'Повторить загрузку' }))
    expect(retry).toHaveBeenCalledTimes(1)
  })

  it('supports confirmation alert dialogs without duplicating an error already shown there', () => {
    render(
      <>
        <main><AsyncErrorState message="Удаление отклонено." onRetry={() => undefined} /></main>
        <AlertDialog label="Подтверждение удаления"><FormError>Удаление отклонено.</FormError></AlertDialog>
      </>,
    )

    const confirmation = screen.getByRole('alertdialog', { name: 'Подтверждение удаления' })
    expect(within(confirmation).getByRole('alert')).toHaveTextContent('Удаление отклонено.')
    expect(within(confirmation).queryAllByText('Удаление отклонено.')).toHaveLength(1)
    expect(confirmation.querySelector('.foreground-dialog-error-host')).not.toBeInTheDocument()
  })
})
