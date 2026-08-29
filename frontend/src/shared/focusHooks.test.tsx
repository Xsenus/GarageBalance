import { fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { useState } from 'react'
import { vi } from 'vitest'
import { focusAfterDomUpdate, restoreFocusAfterClose, useCloseOnOutsidePointer, useDismissOnWindowClick, useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from './focusHooks'

function OutsidePointerProbe() {
  const [open, setOpen] = useState(true)
  const ref = useCloseOnOutsidePointer<HTMLDivElement>(open, setOpen)
  return (
    <>
      <div ref={ref}>
        <button type="button">Внутри</button>
        <span>{open ? 'Открыто' : 'Закрыто'}</span>
      </div>
      <button type="button">Снаружи</button>
    </>
  )
}

function EscapeProbe({ enabled, onEscape }: { enabled: boolean; onEscape: () => void }) {
  useEscapeKey(enabled, onEscape)
  return <button type="button">Probe</button>
}

function WindowClickDismissProbe() {
  const [menu, setMenu] = useState<{ id: string } | null>({ id: 'menu' })
  useDismissOnWindowClick(Boolean(menu), setMenu)
  return (
    <>
      <div onClick={(event) => event.stopPropagation()}>
        <button type="button">Внутри меню</button>
      </div>
      <button type="button">Вне меню</button>
      <span>{menu ? 'Меню открыто' : 'Меню закрыто'}</span>
    </>
  )
}

function FocusOnOpenProbe({ enabled }: { enabled: boolean }) {
  const ref = useFocusOnOpen<HTMLButtonElement>(enabled)
  return <button ref={ref} type="button">Target</button>
}

function FocusTrapProbe({ enabled }: { enabled: boolean }) {
  const ref = useFocusTrap<HTMLDivElement>(enabled)
  return (
    <div ref={ref}>
      <button type="button">First</button>
      <button type="button">Last</button>
    </div>
  )
}

function RestoreFocusProbe({ open }: { open: boolean }) {
  useRestoreFocusOnClose(open)
  return open ? <button type="button">Dialog action</button> : null
}

describe('focus shared hooks', () => {
  it('keeps an active surface open for inside clicks and closes it on an outside pointer', async () => {
    const user = userEvent.setup()
    render(<OutsidePointerProbe />)

    await user.click(screen.getByRole('button', { name: 'Внутри' }))
    expect(screen.getByText('Открыто')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Снаружи' }))
    expect(screen.getByText('Закрыто')).toBeInTheDocument()
  })

  it('closes on pointer input before a compatibility mouse event is emitted', () => {
    render(<OutsidePointerProbe />)

    fireEvent.pointerDown(screen.getByRole('button', { name: 'Снаружи' }))

    expect(screen.getByText('Закрыто')).toBeInTheDocument()
  })

  it('listens for window clicks only while a dismissible surface is open', async () => {
    const user = userEvent.setup()
    const addListener = vi.spyOn(window, 'addEventListener')
    const removeListener = vi.spyOn(window, 'removeEventListener')
    render(<WindowClickDismissProbe />)

    expect(addListener.mock.calls.filter(([type]) => type === 'click')).toHaveLength(1)
    await user.click(screen.getByRole('button', { name: 'Внутри меню' }))
    expect(screen.getByText('Меню открыто')).toBeInTheDocument()

    await user.click(screen.getByRole('button', { name: 'Вне меню' }))
    expect(screen.getByText('Меню закрыто')).toBeInTheDocument()
    expect(removeListener.mock.calls.filter(([type]) => type === 'click')).toHaveLength(1)

    addListener.mockRestore()
    removeListener.mockRestore()
  })

  it('keeps one escape listener while using the latest handler', () => {
    const calls: string[] = []
    const addListener = vi.spyOn(window, 'addEventListener')
    const removeListener = vi.spyOn(window, 'removeEventListener')
    const { rerender } = render(<EscapeProbe enabled={false} onEscape={() => calls.push('disabled')} />)

    fireEvent.keyDown(window, { key: 'Escape' })
    expect(calls).toEqual([])
    expect(addListener.mock.calls.filter(([type]) => type === 'keydown')).toHaveLength(0)

    rerender(<EscapeProbe enabled={true} onEscape={() => calls.push('old')} />)
    rerender(<EscapeProbe enabled={true} onEscape={() => calls.push('latest')} />)
    expect(addListener.mock.calls.filter(([type]) => type === 'keydown')).toHaveLength(1)
    fireEvent.keyDown(window, { key: 'Escape' })

    expect(calls).toEqual(['latest'])
    rerender(<EscapeProbe enabled={false} onEscape={() => calls.push('disabled')} />)
    expect(removeListener.mock.calls.filter(([type]) => type === 'keydown')).toHaveLength(1)

    addListener.mockRestore()
    removeListener.mockRestore()
  })

  it('focuses target when opened', () => {
    render(<FocusOnOpenProbe enabled={true} />)

    expect(screen.getByRole('button', { name: 'Target' })).toHaveFocus()
  })

  it('restores an explicit trigger after close and clears its reference', () => {
    vi.useFakeTimers()
    const trigger = document.createElement('button')
    const current = document.createElement('button')
    document.body.append(trigger, current)
    current.focus()
    const triggerRef: { current: HTMLButtonElement | null } = { current: trigger }

    restoreFocusAfterClose(triggerRef)
    expect(triggerRef.current).toBeNull()
    expect(current).toHaveFocus()
    vi.runAllTimers()

    expect(trigger).toHaveFocus()
    expect(triggerRef.current).toBeNull()
    trigger.remove()
    current.remove()
    vi.useRealTimers()
  })

  it('does not clear a trigger captured by a reopened dialog', () => {
    vi.useFakeTimers()
    const previousTrigger = document.createElement('button')
    const nextTrigger = document.createElement('button')
    document.body.append(previousTrigger, nextTrigger)
    const triggerRef: { current: HTMLButtonElement | null } = { current: previousTrigger }

    restoreFocusAfterClose(triggerRef)
    triggerRef.current = nextTrigger
    vi.runAllTimers()

    expect(previousTrigger).toHaveFocus()
    expect(triggerRef.current).toBe(nextTrigger)
    previousTrigger.remove()
    nextTrigger.remove()
    vi.useRealTimers()
  })

  it('focuses a connected element after the current DOM update', () => {
    vi.useFakeTimers()
    const trigger = document.createElement('button')
    document.body.append(trigger)

    focusAfterDomUpdate(trigger)
    expect(trigger).not.toHaveFocus()
    vi.runAllTimers()

    expect(trigger).toHaveFocus()
    trigger.remove()
    vi.useRealTimers()
  })

  it('clears a disconnected trigger without moving focus', () => {
    vi.useFakeTimers()
    const current = document.createElement('button')
    document.body.append(current)
    current.focus()
    const triggerRef: { current: HTMLButtonElement | null } = { current: document.createElement('button') }

    restoreFocusAfterClose(triggerRef)
    vi.runAllTimers()

    expect(current).toHaveFocus()
    expect(triggerRef.current).toBeNull()
    current.remove()
    vi.useRealTimers()
  })

  it('traps tab navigation inside the active container', async () => {
    const user = userEvent.setup()
    render(<FocusTrapProbe enabled={true} />)

    screen.getByRole('button', { name: 'Last' }).focus()
    await user.keyboard('{Tab}')
    expect(screen.getByRole('button', { name: 'First' })).toHaveFocus()

    await user.keyboard('{Shift>}{Tab}{/Shift}')
    expect(screen.getByRole('button', { name: 'Last' })).toHaveFocus()
  })

  it('restores focus to opener when the active surface closes', () => {
    const { rerender } = render(
      <>
        <button type="button">Open dialog</button>
        <RestoreFocusProbe open={false} />
      </>,
    )

    screen.getByRole('button', { name: 'Open dialog' }).focus()

    rerender(
      <>
        <button type="button">Open dialog</button>
        <RestoreFocusProbe open={true} />
      </>,
    )

    screen.getByRole('button', { name: 'Dialog action' }).focus()

    rerender(
      <>
        <button type="button">Open dialog</button>
        <RestoreFocusProbe open={false} />
      </>,
    )

    expect(screen.getByRole('button', { name: 'Open dialog' })).toHaveFocus()
  })
})
