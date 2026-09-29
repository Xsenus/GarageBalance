import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import type { GarageDto } from '../../services/dictionariesApi'
import type { DailyServicePaymentReportDto } from '../../services/reportsApi'
import { DailyServicePaymentPanel } from './DailyServicePaymentPanel'

vi.mock('../../shared/fileExports', () => ({ downloadBlob: vi.fn() }))
import { downloadBlob } from '../../shared/fileExports'

const amounts = { electricity: 10, water: 20, trash: 30, outdoorLighting: 40, membership: 50, target: 60, other: 7, total: 217 }
const report: DailyServicePaymentReportDto = { dateFrom: '2026-09-01', throughDate: '2026-09-18', data: {
  rows: [{ date: '2026-09-18', garageId: 'garage-85', garageNumber: '85', amounts }],
  days: [{ date: '2026-09-18', amounts: { ...amounts, total: 1000 } }], monthTotal: { ...amounts, total: 2000 },
  rowCount: 30, offset: 0, limit: 25, hasOther: true,
} }
const garage = { id: 'garage-85', number: '85', ownerName: 'Тестовый владелец' } as GarageDto
function props() {
  return { accessToken: 'test', canRead: true, dictionaryClient: { getGarages: vi.fn().mockResolvedValue([garage]) }, reportClient: {
    getDailyServicePayments: vi.fn().mockResolvedValue(report), exportDailyServicePayments: vi.fn().mockResolvedValue(new Blob(['fixture'])),
  } }
}

describe('daily service payment report', () => {
  it('restores the selected garage outside the first search page and clears it explicitly', async () => {
    const user = userEvent.setup()
    const supplied = props()
    supplied.dictionaryClient.getGarages.mockResolvedValue([])
    const onSelectedGarageChange = vi.fn()
    const onFiltersChange = vi.fn()
    render(<DailyServicePaymentPanel {...supplied} initialFilters={{ throughDate: '2026-09-18', garageId: garage.id, offset: 0, limit: 25 }} initialSelectedGarage={garage} onSelectedGarageChange={onSelectedGarageChange} onFiltersChange={onFiltersChange} />)
    const selector = screen.getByRole('combobox', { name: 'Гараж ежедневного отчёта' })
    await waitFor(() => expect(selector).not.toBeDisabled())
    expect(selector).toHaveTextContent('Гараж 85')
    expect(supplied.reportClient.getDailyServicePayments).toHaveBeenCalledWith('test', expect.objectContaining({ garageId: garage.id }), expect.any(AbortSignal))
    await user.click(selector)
    await user.click(screen.getByRole('option', { name: 'Все гаражи' }))
    expect(onSelectedGarageChange).toHaveBeenLastCalledWith(null)
    expect(onFiltersChange).toHaveBeenLastCalledWith(expect.objectContaining({ garageId: undefined, offset: 0 }))
    await waitFor(() => expect(supplied.reportClient.getDailyServicePayments).toHaveBeenLastCalledWith('test', expect.objectContaining({ garageId: undefined }), expect.any(AbortSignal)))
  })

  it('shows service payments and full totals with shared pagination and filters', async () => {
    const user = userEvent.setup()
    const supplied = props()
    render(<DailyServicePaymentPanel {...supplied} />)
    const table = await screen.findByRole('table', { name: 'Оплаты гаражей по услугам' })
    expect(within(table).getByRole('columnheader', { name: 'Прочее' })).toBeInTheDocument()
    expect(within(table).getByRole('rowheader', { name: '85' })).toBeInTheDocument()
    expect(within(table).getByText('1 000.00')).toBeInTheDocument()
    expect(within(table).getByText('2 000.00')).toBeInTheDocument()
    expect(screen.getByRole('group', { name: 'Количество строк' })).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Страница 2' }))
    await waitFor(() => expect(supplied.reportClient.getDailyServicePayments).toHaveBeenLastCalledWith('test', expect.objectContaining({ offset: 25, limit: 25 }), expect.any(AbortSignal)))
    await user.click(await screen.findByRole('button', { name: '50', exact: true }))
    await waitFor(() => expect(supplied.reportClient.getDailyServicePayments).toHaveBeenLastCalledWith('test', expect.objectContaining({ offset: 0, limit: 50 }), expect.any(AbortSignal)))
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Гараж ежедневного отчёта' })).not.toBeDisabled())
    await user.click(screen.getByRole('combobox', { name: 'Гараж ежедневного отчёта' }))
    await user.click(screen.getByRole('option', { name: /Гараж 85/ }))
    await waitFor(() => expect(supplied.reportClient.getDailyServicePayments).toHaveBeenLastCalledWith('test', expect.objectContaining({ garageId: garage.id, offset: 0 }), expect.any(AbortSignal)))
    await user.type(screen.getByLabelText('Поиск гаража ежедневного отчёта'), '85')
    await waitFor(() => expect(supplied.dictionaryClient.getGarages).toHaveBeenLastCalledWith('test', '85', 20, true, expect.any(AbortSignal)))
    fireEvent.change(screen.getByLabelText('Дата ежедневного отчёта'), { target: { value: '19.09.2026' } })
    await waitFor(() => expect(supplied.reportClient.getDailyServicePayments).toHaveBeenLastCalledWith('test', expect.objectContaining({ throughDate: '2026-09-19', offset: 0, garageId: garage.id }), expect.any(AbortSignal)))
  })

  it('handles loading error retry and empty result without an unnecessary Other column', async () => {
    const user = userEvent.setup()
    const supplied = props()
    let reject: (error: Error) => void = () => {}
    supplied.reportClient.getDailyServicePayments.mockReturnValueOnce(new Promise((_resolve, fail) => { reject = fail }))
      .mockResolvedValueOnce({ ...report, data: { ...report.data, rows: [], days: [], rowCount: 0, hasOther: false } })
    render(<DailyServicePaymentPanel {...supplied} />)
    expect(screen.getByRole('status', { name: 'Получаем ежедневный отчёт' })).toBeInTheDocument()
    expect(screen.queryByText('Оплат за выбранный период нет')).not.toBeInTheDocument()
    await act(async () => reject(new Error('Ошибка отчёта')))
    expect(await screen.findByText('Ошибка отчёта')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
    expect(await screen.findByText('Оплат за выбранный период нет')).toBeInTheDocument()
    expect(screen.queryByRole('columnheader', { name: 'Прочее' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Открыть календарь: Дата ежедневного отчёта' }))
    await user.click(screen.getByRole('button', { name: 'Очистить' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Укажите дату отчёта.')
    expect(supplied.reportClient.getDailyServicePayments).toHaveBeenCalledTimes(2)
    expect(screen.getByRole('button', { name: 'Скачать ежедневный отчёт XLSX' })).toBeDisabled()
  })

  it.each(['xlsx', 'pdf'] as const)('exports %s with current filters and reports export failures', async (format) => {
    const user = userEvent.setup()
    const supplied = props()
    supplied.reportClient.exportDailyServicePayments.mockRejectedValueOnce(new Error('Экспорт недоступен'))
    render(<DailyServicePaymentPanel {...supplied} />)
    await screen.findByRole('table', { name: 'Оплаты гаражей по услугам' })
    const button = screen.getByRole('button', { name: `Скачать ежедневный отчёт ${format.toUpperCase()}` })
    await user.click(button)
    expect(await screen.findByRole('alert')).toHaveTextContent('Экспорт недоступен')
    await user.click(button)
    expect(await screen.findByText(`Отчёт ${format.toUpperCase()} готов.`)).toBeInTheDocument()
    expect(supplied.reportClient.exportDailyServicePayments).toHaveBeenLastCalledWith('test', expect.objectContaining({ throughDate: expect.any(String) }), format, expect.any(AbortSignal))
    expect(downloadBlob).toHaveBeenCalledWith(expect.any(Blob), expect.stringMatching(new RegExp(`^garagebalance-daily-services-\\d{8}\\.${format}$`)))
  })

  it('rejects stale results and aborts in-flight report and export on unmount', async () => {
    const user = userEvent.setup()
    const supplied = props()
    let resolve: (result: DailyServicePaymentReportDto) => void = () => {}
    supplied.reportClient.getDailyServicePayments.mockReturnValueOnce(new Promise((done) => { resolve = done }))
    const { unmount } = render(<DailyServicePaymentPanel {...supplied} />)
    const initialSignal = supplied.reportClient.getDailyServicePayments.mock.calls[0][2] as AbortSignal
    fireEvent.change(screen.getByLabelText('Дата ежедневного отчёта'), { target: { value: '19.09.2026' } })
    await screen.findByRole('table', { name: 'Оплаты гаражей по услугам' })
    expect(initialSignal.aborted).toBe(true)
    await act(async () => resolve({ ...report, data: { ...report.data, rows: [{ ...report.data.rows[0], garageNumber: '999' }] } }))
    expect(screen.queryByRole('rowheader', { name: '999' })).not.toBeInTheDocument()
    supplied.reportClient.exportDailyServicePayments.mockReturnValueOnce(new Promise(() => {}))
    await user.click(screen.getByRole('button', { name: 'Скачать ежедневный отчёт XLSX' }))
    const exportSignal = supplied.reportClient.exportDailyServicePayments.mock.calls[0][3] as AbortSignal
    expect(screen.getByRole('button', { name: 'Скачать ежедневный отчёт PDF' })).toBeDisabled()
    unmount()
    expect(exportSignal.aborted).toBe(true)
    expect(supplied.reportClient.getDailyServicePayments.mock.lastCall![2].aborted).toBe(true)
  })

  it('search error can be retried and permission-denied state sends no requests', async () => {
    const user = userEvent.setup()
    const supplied = props()
    supplied.dictionaryClient.getGarages.mockRejectedValueOnce(new Error('Поиск недоступен'))
    const { unmount } = render(<DailyServicePaymentPanel {...supplied} />)
    expect(await screen.findByText('Поиск недоступен')).toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
    await waitFor(() => expect(screen.getByRole('combobox', { name: 'Гараж ежедневного отчёта' })).not.toBeDisabled())
    unmount()
    const denied = props()
    render(<DailyServicePaymentPanel {...denied} canRead={false} />)
    expect(screen.getByText(/необходимо право чтения отчётов/)).toBeInTheDocument()
    expect(denied.reportClient.getDailyServicePayments).not.toHaveBeenCalled()
    expect(denied.dictionaryClient.getGarages).not.toHaveBeenCalled()
  })
})
