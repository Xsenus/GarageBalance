import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { LocalizedDatePicker } from './LocalizedDatePicker'

afterEach(() => { vi.restoreAllMocks(); vi.unstubAllGlobals() })

describe('calendar layout lifecycle', () => {
  it('repositions on scroll, resize and content resize, then removes observers when disabled', async () => {
    const user = userEvent.setup()
    let resized: ResizeObserverCallback = () => undefined
    const disconnect = vi.fn()
    const observe = vi.fn()
    vi.stubGlobal('ResizeObserver', class {
      constructor(callback: ResizeObserverCallback) { resized = callback }
      observe = observe
      disconnect = disconnect
    })
    vi.stubGlobal('innerWidth', 1024)
    vi.stubGlobal('innerHeight', 800)
    vi.spyOn(HTMLElement.prototype, 'scrollHeight', 'get').mockReturnValue(300)
    const { container, rerender } = render(<LocalizedDatePicker ariaLabel="Дата" mode="date" value="2026-09-05" onChange={() => undefined} />)
    const root = container.querySelector('.localized-date-picker')!
    const bounds = vi.spyOn(root, 'getBoundingClientRect').mockReturnValue({ top: 340, bottom: 380, right: 700 } as DOMRect)
    await user.click(screen.getByRole('button', { name: 'Открыть календарь: Дата' }))
    const calendar = screen.getByRole('dialog')
    expect(calendar).toHaveStyle({ left: '408px', top: '386px', width: '292px', maxHeight: '406px' })
    expect(observe).toHaveBeenCalledWith(root)
    expect(observe).toHaveBeenCalledWith(calendar)
    vi.stubGlobal('innerHeight', 500)
    fireEvent(window, new Event('resize'))
    expect(calendar).toHaveStyle({ top: '32px', maxHeight: '326px' })
    bounds.mockReturnValue({ top: 30, bottom: 70, right: 200 } as DOMRect)
    fireEvent.scroll(root)
    expect(calendar).toHaveStyle({ top: '76px', left: '8px' })
    vi.stubGlobal('innerWidth', 240)
    resized([], {} as ResizeObserver)
    expect(calendar).toHaveStyle({ width: '224px' })
    rerender(<LocalizedDatePicker ariaLabel="Дата" mode="date" value="2026-09-05" disabled onChange={() => undefined} />)
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(disconnect).toHaveBeenCalledOnce()
    const calls = bounds.mock.calls.length
    fireEvent.scroll(root)
    fireEvent(window, new Event('resize'))
    expect(bounds).toHaveBeenCalledTimes(calls)
  })

  it('tracks the visual viewport and releases its listeners on unmount', async () => {
    const user = userEvent.setup()
    const viewport = Object.assign(new EventTarget(), { width: 320, height: 400, offsetLeft: 40, offsetTop: 100 })
    vi.stubGlobal('visualViewport', viewport)
    vi.spyOn(HTMLElement.prototype, 'scrollHeight', 'get').mockReturnValue(300)
    const remove = vi.spyOn(viewport, 'removeEventListener')
    const { container, unmount } = render(<LocalizedDatePicker ariaLabel="Дата" mode="date" value="2026-09-05" onChange={() => undefined} />)
    const root = container.querySelector('.localized-date-picker')!
    vi.spyOn(root, 'getBoundingClientRect').mockReturnValue({ top: 120, bottom: 160, right: 300 } as DOMRect)
    await user.click(screen.getByRole('button', { name: 'Открыть календарь: Дата' }))
    const calendar = screen.getByRole('dialog')
    expect(calendar).toHaveStyle({ left: '48px', top: '166px' })
    viewport.offsetLeft = 80
    viewport.dispatchEvent(new Event('scroll'))
    expect(calendar).toHaveStyle({ left: '88px' })
    viewport.width = 240
    viewport.dispatchEvent(new Event('resize'))
    expect(calendar).toHaveStyle({ width: '224px' })
    unmount()
    expect(remove).toHaveBeenCalledWith('resize', expect.any(Function))
    expect(remove).toHaveBeenCalledWith('scroll', expect.any(Function))
  })

  it.each([
    ['date', '2026-09-05', '6', '06.09.2026'],
    ['month', '2026-09', 'Окт', '10.2026'],
  ] as const)('returns focus after selecting and clearing a %s value', async (mode, initial, option, expected) => {
    const user = userEvent.setup()
    function Example() {
      const [value, setValue] = useState<string>(initial)
      return <LocalizedDatePicker ariaLabel="Период" mode={mode} value={value} onChange={setValue} />
    }
    render(<Example />)
    const trigger = screen.getByRole('button', { name: 'Открыть календарь: Период' })
    await user.click(trigger)
    const choice = screen.getByRole('button', { name: option, exact: true })
    choice.focus()
    await user.keyboard('{Enter}')
    expect(screen.getByLabelText('Период')).toHaveValue(expected)
    expect(trigger).toHaveFocus()
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    await user.keyboard('{Enter}')
    await user.click(screen.getByRole('button', { name: 'Очистить' }))
    expect(screen.getByLabelText('Период')).toHaveValue('')
    expect(trigger).toHaveFocus()
  })

  it('handles Escape inside the calendar without closing its parent form', async () => {
    const user = userEvent.setup()
    const parentKey = vi.fn()
    const change = vi.fn()
    render(<section onKeyDown={parentKey}><LocalizedDatePicker ariaLabel="Месяц" mode="month" value="2026-09" onChange={change} /></section>)
    const trigger = screen.getByRole('button', { name: 'Открыть календарь: Месяц' })
    await user.click(trigger)
    screen.getByRole('button', { name: 'Окт' }).focus()
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(trigger).toHaveFocus()
    expect(parentKey).not.toHaveBeenCalled()
    expect(change).not.toHaveBeenCalled()
    await user.keyboard('{Escape}')
    expect(parentKey).toHaveBeenCalledOnce()
  })
})
