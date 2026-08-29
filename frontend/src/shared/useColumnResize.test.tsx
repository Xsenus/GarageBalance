import { act, fireEvent, render, screen } from '@testing-library/react'
import { useState } from 'react'
import { afterEach, vi } from 'vitest'
import { useColumnResize } from './useColumnResize'

const definitions = [{ key: 'name' as const, minWidth: 80 }]

function ColumnResizeProbe() {
  const [widths, setWidths] = useState({ name: 100 })
  const resize = useColumnResize(definitions, widths, setWidths)

  return (
    <>
      <button
        type="button"
        onPointerDown={(event) => resize.startResize('name', event)}
        onPointerMove={resize.continueResize}
        onPointerUp={resize.finishResize}
        onPointerCancel={resize.cancelResize}
        onKeyDown={(event) => resize.resizeWithKeyboard('name', event)}
      >
        Resize
      </button>
      <output aria-label="Width">{widths.name}</output>
    </>
  )
}

afterEach(() => {
  vi.unstubAllGlobals()
  vi.restoreAllMocks()
})

describe('useColumnResize', () => {
  it('batches pointer movements into one animation frame and flushes the final width', () => {
    let frameCallback: FrameRequestCallback | null = null
    const requestFrame = vi.fn((callback: FrameRequestCallback) => {
      frameCallback = callback
      return 7
    })
    const cancelFrame = vi.fn()
    vi.stubGlobal('requestAnimationFrame', requestFrame)
    vi.stubGlobal('cancelAnimationFrame', cancelFrame)
    render(<ColumnResizeProbe />)

    const handle = screen.getByRole('button', { name: 'Resize' })
    fireEvent.pointerDown(handle, { button: 0, pointerId: 4, clientX: 100 })
    fireEvent.pointerMove(handle, { pointerId: 4, clientX: 120 })
    fireEvent.pointerMove(handle, { pointerId: 4, clientX: 140 })

    expect(requestFrame).toHaveBeenCalledTimes(1)
    expect(screen.getByRole('status', { name: 'Width' })).toHaveTextContent('100')
    act(() => frameCallback?.(0))
    expect(screen.getByRole('status', { name: 'Width' })).toHaveTextContent('140')

    fireEvent.pointerMove(handle, { pointerId: 4, clientX: 150 })
    fireEvent.pointerUp(handle, { pointerId: 4, clientX: 160 })

    expect(cancelFrame).toHaveBeenCalledWith(7)
    expect(screen.getByRole('status', { name: 'Width' })).toHaveTextContent('160')

    fireEvent.pointerDown(handle, { button: 0, pointerId: 8, clientX: 100 })
    fireEvent.pointerMove(handle, { pointerId: 8, clientX: 110 })
    fireEvent.pointerCancel(handle, { pointerId: 8, clientX: 0 })
    expect(screen.getByRole('status', { name: 'Width' })).toHaveTextContent('170')
  })

  it('ignores another pointer and cancels a pending frame on unmount', () => {
    const requestFrame = vi.fn(() => 11)
    const cancelFrame = vi.fn()
    vi.stubGlobal('requestAnimationFrame', requestFrame)
    vi.stubGlobal('cancelAnimationFrame', cancelFrame)
    const { unmount } = render(<ColumnResizeProbe />)

    const handle = screen.getByRole('button', { name: 'Resize' })
    fireEvent.pointerDown(handle, { button: 2, pointerId: 7, clientX: 100 })
    fireEvent.pointerMove(handle, { pointerId: 7, clientX: 180 })
    expect(requestFrame).not.toHaveBeenCalled()

    fireEvent.pointerDown(handle, { button: 0, pointerId: 5, clientX: 100 })
    fireEvent.pointerMove(handle, { pointerId: 6, clientX: 180 })
    expect(requestFrame).not.toHaveBeenCalled()
    fireEvent.pointerMove(handle, { pointerId: 5, clientX: 130 })
    unmount()

    expect(cancelFrame).toHaveBeenCalledWith(11)
  })

  it('supports keyboard resizing and preserves the minimum width', () => {
    render(<ColumnResizeProbe />)
    const handle = screen.getByRole('button', { name: 'Resize' })

    fireEvent.keyDown(handle, { key: 'ArrowRight' })
    expect(screen.getByRole('status', { name: 'Width' })).toHaveTextContent('116')
    fireEvent.keyDown(handle, { key: 'ArrowLeft', shiftKey: true })
    expect(screen.getByRole('status', { name: 'Width' })).toHaveTextContent('84')
    fireEvent.keyDown(handle, { key: 'ArrowLeft' })
    expect(screen.getByRole('status', { name: 'Width' })).toHaveTextContent('80')
    fireEvent.keyDown(handle, { key: 'Enter' })
    expect(screen.getByRole('status', { name: 'Width' })).toHaveTextContent('80')
  })
})
