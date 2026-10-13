import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import type { FinanceClient, MeterConnectionPreviewDto } from '../../services/financeApi'
import { GarageMeterConnectionPreview } from './GarageMeterConnectionPreview'

const result: MeterConnectionPreviewDto = { garageId: 'g1', garageNumber: '7', changes: [], accrualChanges: [] }
const props = () => ({ accessToken: 'token', financeClient: { previewMeterConnections: vi.fn().mockResolvedValue(result) } as unknown as FinanceClient, garageId: 'g1', garageVersion: 'v1', values: [{ meterKind: 'water', value: 250, connectedOn: '2026-09-10' }], onReady: vi.fn() })

it('keeps confirmation unavailable until the financial preview loads and sends the exact version', async () => {
  const p = props()
  let resolve!: (value: MeterConnectionPreviewDto) => void
  vi.mocked(p.financeClient.previewMeterConnections!).mockImplementation(() => new Promise((done) => { resolve = done }))
  render(<GarageMeterConnectionPreview {...p} />)
  expect(screen.getByRole('status', { name: 'Проверяем изменение учёта и начислений' })).toBeInTheDocument()
  expect(p.onReady).toHaveBeenLastCalledWith(false)
  expect(p.financeClient.previewMeterConnections).toHaveBeenCalledWith('token', { garageId: 'g1', garageVersion: 'v1', values: p.values }, expect.any(AbortSignal))
  await act(async () => resolve(result))
  expect(screen.getByText('Начисления не изменятся.')).toBeInTheDocument()
  expect(p.onReady).toHaveBeenLastCalledWith(true)
})

it('reports protected-history errors, blocks readiness, and allows retry', async () => {
  const p = props()
  vi.mocked(p.financeClient.previewMeterConnections!).mockRejectedValueOnce(new Error('Начисление уже оплачено'))
  render(<GarageMeterConnectionPreview {...p} />)
  expect(await screen.findByRole('alert')).toHaveTextContent('Начисление уже оплачено')
  expect(p.onReady).not.toHaveBeenCalledWith(true)
  fireEvent.click(screen.getByRole('button', { name: 'Повторить загрузку' }))
  expect(await screen.findByText('Начисления не изменятся.')).toBeInTheDocument()
})

it('shows amount and waiting-status differences with shared pagination', async () => {
  const p = props()
  vi.mocked(p.financeClient.previewMeterConnections!).mockResolvedValue({ ...result, accrualChanges: Array.from({ length: 12 }, (_, index) => ({ accrualId: String(index), accountingMonth: `2026-${String(index + 1).padStart(2, '0')}-01`, meterKind: 'water', oldAmount: 100, newAmount: 150, oldRequiresMeterReading: index === 0, newRequiresMeterReading: index === 1 })) })
  render(<GarageMeterConnectionPreview {...p} />)
  const table = await screen.findByRole('table', { name: 'Изменения начислений услуги' })
  expect(table).toHaveTextContent('Ожидается показание → 150')
  expect(table).toHaveTextContent('100.00 → Ожидается показание')
  fireEvent.click(screen.getByRole('button', { name: 'Страница 2', exact: true }))
  expect(screen.getByText('Показано 11-12 из 12')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: '25', exact: true }))
  expect(screen.getByText('Показано 1-12 из 12')).toBeInTheDocument()
})

it('aborts previews and ignores old responses after values change or the dialog closes', async () => {
  const p = props()
  let resolveOld!: (value: MeterConnectionPreviewDto) => void
  let rejectNext!: (value: Error) => void
  vi.mocked(p.financeClient.previewMeterConnections!).mockImplementationOnce(() => new Promise((done) => { resolveOld = done }))
    .mockImplementationOnce(() => new Promise((_, reject) => { rejectNext = reject }))
  const { rerender, unmount } = render(<GarageMeterConnectionPreview {...p} />)
  const signal = vi.mocked(p.financeClient.previewMeterConnections!).mock.calls[0][2]!
  rerender(<GarageMeterConnectionPreview {...p} garageVersion="v2" />)
  expect(signal.aborted).toBe(true)
  await act(async () => resolveOld(result))
  expect(p.onReady).not.toHaveBeenCalledWith(true)
  const nextSignal = vi.mocked(p.financeClient.previewMeterConnections!).mock.calls[1][2]!
  unmount()
  expect(nextSignal.aborted).toBe(true)
  await act(async () => rejectNext(new Error('late failure')))
  await waitFor(() => expect(p.onReady).not.toHaveBeenCalledWith(true))
})
