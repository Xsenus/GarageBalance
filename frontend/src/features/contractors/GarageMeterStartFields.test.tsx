import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { GarageMeterStartFields } from './GarageMeterStartFields'
import type { GarageMeterStartValueDto } from '../../services/dictionariesApi'
import { toGarageMeterStartRows } from './garageMeterStartValues'

const values: GarageMeterStartValueDto[] = [{ meterKind: 'water', serviceName: 'Вода', unitName: 'м³', value: 10, hasReadings: true }]
const makeProps = () => ({ loadKey: 'garage-1', load: vi.fn().mockResolvedValue(values), rows: toGarageMeterStartRows(values),
  errors: {}, disabled: false, onLoaded: vi.fn(), onChange: vi.fn(), onFieldEdited: vi.fn() })

it('loads labelled fields, preserves validation and disables edits during saving', async () => {
  const props = makeProps()
  const { rerender } = render(<GarageMeterStartFields {...props} errors={{ 'meter:water': 'Ошибка' }} />)
  expect(screen.getByRole('status', { name: 'Загружаем стартовые значения счётчиков' })).toHaveAttribute('aria-live', 'polite')
  const input = await screen.findByLabelText('Стартовое значение счётчика: Вода')
  expect(input).toHaveAttribute('aria-invalid', 'true')
  fireEvent.change(input, { target: { value: '15' } })
  expect(props.onFieldEdited).toHaveBeenCalledWith('meter:water')
  expect(props.onChange).toHaveBeenCalledWith('water', '15')
  rerender(<GarageMeterStartFields {...props} disabled />)
  expect(input).toBeDisabled()
  expect(props.load).toHaveBeenCalledOnce()
})

it('retries a failed load and accepts an empty service list', async () => {
  const props = makeProps()
  props.load.mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce([])
  render(<GarageMeterStartFields {...props} rows={[]} />)
  expect(await screen.findByText('Не удалось загрузить стартовые значения счётчиков.')).toBeInTheDocument()
  fireEvent.click(screen.getByRole('button', { name: /Повторить/ }))
  await waitFor(() => expect(props.onLoaded).toHaveBeenCalledWith([]))
  expect(screen.getByRole('group', { name: 'Стартовые значения счётчиков' })).toBeEmptyDOMElement()
})

it('aborts old requests and ignores stale success and failure after the garage changes or closes', async () => {
  const props = makeProps()
  let resolveOld!: (value: GarageMeterStartValueDto[]) => void
  let rejectCurrent!: (error: Error) => void
  props.load.mockImplementationOnce(() => new Promise((resolve) => { resolveOld = resolve }))
    .mockImplementationOnce(() => new Promise((_, reject) => { rejectCurrent = reject }))
  const { rerender, unmount } = render(<GarageMeterStartFields {...props} />)
  const oldSignal = props.load.mock.calls[0][0] as AbortSignal
  rerender(<GarageMeterStartFields {...props} loadKey="garage-2" />)
  expect(oldSignal.aborted).toBe(true)
  await act(async () => resolveOld(values))
  expect(props.onLoaded).not.toHaveBeenCalled()
  const currentSignal = props.load.mock.calls[1][0] as AbortSignal
  unmount()
  expect(currentSignal.aborted).toBe(true)
  await act(async () => rejectCurrent(new Error('late failure')))
  expect(props.onLoaded).not.toHaveBeenCalled()
})
