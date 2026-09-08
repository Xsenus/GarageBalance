// @vitest-environment jsdom
import { act, renderHook, waitFor } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import type { FundOptionDto } from '../../services/fundsApi'
import { useExpenseFundOptions } from './useExpenseFundOptions'

describe('useExpenseFundOptions', () => {
  it('loads only for a cash payout and excludes unavailable funds without inventing balances', async () => {
    const client = { getFundOptions: vi.fn().mockResolvedValue([{ id: 'b', name: 'Б', allowOperations: true }, { id: 'closed', name: 'Закрытый', allowOperations: false }, { id: 'a', name: 'А', allowOperations: true }]) }
    const { result, rerender } = renderHook(({ enabled }) => useExpenseFundOptions(client, 'token', enabled), { initialProps: { enabled: false } })
    expect(client.getFundOptions).not.toHaveBeenCalled()
    rerender({ enabled: true })
    await waitFor(() => expect(result.current.loading).toBe(false))
    expect(result.current.options.map((fund) => fund.id)).toEqual(['a', 'b'])
    expect(result.current.options[0]).not.toHaveProperty('balance')
  })

  it('exposes a failed load and retries it without requiring a new dialog', async () => {
    const client = { getFundOptions: vi.fn().mockRejectedValueOnce(new Error('Список фондов недоступен')).mockResolvedValue([{ id: 'fund', name: 'Фонд', allowOperations: true }]) }
    const { result } = renderHook(() => useExpenseFundOptions(client, 'token', true))
    await waitFor(() => expect(result.current.error).toBe('Список фондов недоступен'))
    act(() => result.current.reload())
    await waitFor(() => expect(result.current.options).toHaveLength(1))
    expect(result.current.error).toBeNull()
    expect(client.getFundOptions).toHaveBeenCalledTimes(2)
  })

  it('aborts and ignores an older session response and cancels on unmount', async () => {
    let resolveOld!: (options: FundOptionDto[]) => void
    const client = { getFundOptions: vi.fn().mockImplementationOnce(() => new Promise<FundOptionDto[]>((resolve) => { resolveOld = resolve })).mockResolvedValue([{ id: 'new', name: 'Новый', allowOperations: true }]) }
    const { result, rerender, unmount } = renderHook(({ token }) => useExpenseFundOptions(client, token, true), { initialProps: { token: 'old' } })
    await waitFor(() => expect(client.getFundOptions).toHaveBeenCalledTimes(1))
    const oldSignal = client.getFundOptions.mock.calls[0][1] as AbortSignal
    rerender({ token: 'new' })
    await waitFor(() => expect(result.current.options[0]?.id).toBe('new'))
    await act(async () => resolveOld([{ id: 'old', name: 'Старый', allowOperations: true }]))
    expect(oldSignal.aborted).toBe(true)
    expect(result.current.options[0].id).toBe('new')
    const currentSignal = client.getFundOptions.mock.calls[1][1] as AbortSignal
    unmount()
    expect(currentSignal.aborted).toBe(true)
  })
})
