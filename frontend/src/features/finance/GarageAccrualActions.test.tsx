import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { AccrualDto, FinanceClient, FinancePagedResult } from '../../services/financeApi'
import { GarageAccrualActions } from './GarageAccrualActions'
import type { GarageIncomePrototypeRow } from './garageIncomeWorksheetRows'

const row = { month: '2026-09', incomeTypeId: 'income', irregularPaymentId: 'irregular', service: 'Подключение канализации' } as GarageIncomePrototypeRow
const record = { id: 'accrual', garageId: 'garage-85', incomeTypeId: 'income', irregularPaymentId: 'irregular', amount: 20000, basis: row.service, feeCampaignId: null } as AccrualDto
const page: FinancePagedResult<AccrualDto> = { items: [record], totalCount: 1, offset: 0, limit: 10 }
function setup() {
  const getAccrualsPage = vi.fn().mockResolvedValue(page)
  const props = { target: { row, garageId: 'garage-85', x: 100, y: 100 }, accessToken: 'test', financeClient: { getAccrualsPage } as unknown as FinanceClient, canWrite: true, onClose: vi.fn(), onEdit: vi.fn(), onCancel: vi.fn() }
  return { props, getAccrualsPage }
}

describe('GarageAccrualActions', () => {
  it.each(['edit', 'cancel'] as const)('selects the original accrual for %s, not the worksheet group amount', async (action) => {
    const user = userEvent.setup()
    const { props, getAccrualsPage } = setup()
    render(<GarageAccrualActions {...props} />)
    const label = action === 'edit' ? 'Редактировать' : 'Удалить'
    await user.click(screen.getByRole('menuitem', { name: label }))
    const button = await screen.findByRole('button', { name: label })
    expect(screen.getByRole('button', { name: 'Закрыть выбор начисления' })).toHaveFocus()
    expect(getAccrualsPage).toHaveBeenCalledWith('test', expect.objectContaining({ garageId: 'garage-85', incomeTypeId: 'income', irregularPaymentId: 'irregular', monthFrom: '2026-09-01', monthTo: '2026-09-01', includeCanceled: false }), expect.any(AbortSignal))
    await user.click(button)
    expect(props.onClose).toHaveBeenCalledOnce()
    expect(action === 'edit' ? props.onEdit : props.onCancel).toHaveBeenCalledWith(record)
  })

  it('shows a skeleton, reports failure and retries without showing empty data while loading', async () => {
    const { props, getAccrualsPage } = setup()
    let reject!: (error: Error) => void
    getAccrualsPage.mockImplementationOnce(() => new Promise((_resolve, rejectRequest) => { reject = rejectRequest }))
    render(<GarageAccrualActions {...props} />)
    fireEvent.click(screen.getByRole('menuitem', { name: 'Редактировать' }))
    expect(screen.getByRole('status', { name: 'Получаем начисления гаража' })).toBeInTheDocument()
    expect(screen.queryByText('На этой странице нет начислений выбранной услуги')).not.toBeInTheDocument()
    await act(async () => { reject(new Error('Проверка ошибки')) })
    expect(await screen.findByText('Проверка ошибки')).toBeInTheDocument()
    fireEvent.click(screen.getByRole('button', { name: /Повторить/ }))
    expect(await screen.findByRole('button', { name: 'Редактировать' })).toBeInTheDocument()
  })

  it('protects fee campaigns and never offers another garage as an editable record', async () => {
    const { props, getAccrualsPage } = setup()
    props.target.row = { ...row, feeCampaignId: 'fee' }
    getAccrualsPage.mockResolvedValue({ ...page, items: [{ ...record, feeCampaignId: 'fee' }, { ...record, id: 'foreign', garageId: 'garage-86', feeCampaignId: 'fee' }] })
    render(<GarageAccrualActions {...props} />)
    fireEvent.click(screen.getByRole('menuitem', { name: 'Удалить' }))
    expect(await screen.findByText('Изменяется в карточке сбора')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Удалить' })).not.toBeInTheDocument()
    expect(screen.getAllByRole('row')).toHaveLength(2)
  })

  it('uses shared pagination, handles empty results and cancels obsolete loads', async () => {
    const { props, getAccrualsPage } = setup()
    getAccrualsPage.mockResolvedValueOnce({ ...page, totalCount: 12 }).mockResolvedValueOnce({ ...page, items: [], totalCount: 12 })
    const view = render(<GarageAccrualActions {...props} />)
    fireEvent.click(screen.getByRole('menuitem', { name: 'Редактировать' }))
    await screen.findByRole('button', { name: 'Редактировать' })
    fireEvent.click(screen.getByRole('button', { name: 'Страница 2' }))
    expect(await screen.findByText('На этой странице нет начислений выбранной услуги')).toBeInTheDocument()
    expect(getAccrualsPage.mock.calls[0][2].aborted).toBe(true)
    expect(getAccrualsPage.mock.calls[1][1].offset).toBe(10)
    view.unmount()
    expect(getAccrualsPage.mock.calls[1][2].aborted).toBe(true)
  })

  it('does not expose actions or load financial records without write permission', () => {
    const { props, getAccrualsPage } = setup()
    render(<GarageAccrualActions {...props} canWrite={false} />)
    expect(screen.queryByRole('menu')).not.toBeInTheDocument()
    expect(getAccrualsPage).not.toHaveBeenCalled()
  })

  it('ignores a late response from the previous page even when transport ignores cancellation', async () => {
    const { props, getAccrualsPage } = setup()
    let resolveOld!: (value: FinancePagedResult<AccrualDto>) => void
    getAccrualsPage.mockResolvedValueOnce({ ...page, totalCount: 21 })
      .mockImplementationOnce(() => new Promise((resolve) => { resolveOld = resolve }))
      .mockResolvedValueOnce({ ...page, items: [{ ...record, id: 'latest', basis: 'Последняя страница' }], totalCount: 21 })
    const view = render(<GarageAccrualActions {...props} />)
    fireEvent.click(screen.getByRole('menuitem', { name: 'Редактировать' }))
    await screen.findByRole('button', { name: 'Редактировать' })
    fireEvent.click(screen.getByRole('button', { name: 'Страница 2' }))
    await waitFor(() => expect(getAccrualsPage).toHaveBeenCalledTimes(2))
    // Parent refresh changes the target while the previous request is pending.
    const newTarget = { ...props.target, row: { ...row, month: '2026-08' } }
    view.rerender(<GarageAccrualActions {...props} target={newTarget} />)
    await screen.findByText('Последняя страница')
    expect(getAccrualsPage.mock.calls[1][2].aborted).toBe(true)
    await act(async () => { resolveOld({ ...page, items: [], totalCount: 21 }) })
    expect(screen.getByText('Последняя страница')).toBeInTheDocument()
    expect(screen.queryByText('На этой странице нет начислений выбранной услуги')).not.toBeInTheDocument()
  })

  it('aborts pending selection and prevents mutation when write permission is revoked', async () => {
    const { props, getAccrualsPage } = setup()
    let resolveRequest!: (value: FinancePagedResult<AccrualDto>) => void
    getAccrualsPage.mockImplementationOnce(() => new Promise((resolve) => { resolveRequest = resolve }))
    const view = render(<GarageAccrualActions {...props} />)
    fireEvent.click(screen.getByRole('menuitem', { name: 'Удалить' }))
    await waitFor(() => expect(getAccrualsPage).toHaveBeenCalledOnce())
    view.rerender(<GarageAccrualActions {...props} canWrite={false} />)
    expect(getAccrualsPage.mock.calls[0][2].aborted).toBe(true)
    await act(async () => { resolveRequest(page) })
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(props.onEdit).not.toHaveBeenCalled()
    expect(props.onCancel).not.toHaveBeenCalled()
  })

  it('supports keyboard navigation and Escape without changing data', async () => {
    const { props } = setup()
    render(<GarageAccrualActions {...props} />)
    await waitFor(() => expect(screen.getByRole('menuitem', { name: 'Редактировать' })).toHaveFocus())
    fireEvent.keyDown(screen.getByRole('menu'), { key: 'ArrowDown' })
    expect(screen.getByRole('menuitem', { name: 'Удалить' })).toHaveFocus()
    fireEvent.keyDown(window, { key: 'Escape' })
    expect(props.onClose).toHaveBeenCalled()
    expect(props.onCancel).not.toHaveBeenCalled()
  })
})
