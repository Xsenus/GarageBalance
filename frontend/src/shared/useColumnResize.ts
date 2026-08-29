import { useEffect, useRef } from 'react'
import type { Dispatch, KeyboardEvent, PointerEvent, SetStateAction } from 'react'

type ResizableColumnDefinition<TKey extends string> = { key: TKey; minWidth: number }
type ColumnWidths<TKey extends string> = Record<TKey, number>
type ActiveResize<TKey extends string> = {
  columnKey: TKey
  minWidth: number
  pointerId: number
  startWidth: number
  startX: number
  latestX: number
}

export function useColumnResize<TKey extends string>(
  definitions: ReadonlyArray<ResizableColumnDefinition<TKey>>,
  widths: ColumnWidths<TKey>,
  setWidths: Dispatch<SetStateAction<ColumnWidths<TKey>>>,
) {
  const activeResizeRef = useRef<ActiveResize<TKey> | null>(null)
  const animationFrameRef = useRef<number | null>(null)

  function cancelScheduledFrame() {
    if (animationFrameRef.current !== null) {
      cancelAnimationFrame(animationFrameRef.current)
      animationFrameRef.current = null
    }
  }

  function applyLatestWidth() {
    animationFrameRef.current = null
    const activeResize = activeResizeRef.current
    if (!activeResize) return
    const nextWidth = Math.max(activeResize.minWidth, activeResize.startWidth + activeResize.latestX - activeResize.startX)
    setWidths((current) => current[activeResize.columnKey] === nextWidth
      ? current
      : { ...current, [activeResize.columnKey]: nextWidth })
  }

  useEffect(() => () => {
    cancelScheduledFrame()
    activeResizeRef.current = null
  }, [])

  function startResize(columnKey: TKey, event: PointerEvent<HTMLButtonElement>) {
    if (event.button > 0) return
    const column = definitions.find((item) => item.key === columnKey)
    if (!column) return
    event.preventDefault()
    event.stopPropagation()
    cancelScheduledFrame()
    activeResizeRef.current = {
      columnKey,
      minWidth: column.minWidth,
      pointerId: event.pointerId,
      startWidth: widths[columnKey],
      startX: event.clientX,
      latestX: event.clientX,
    }
    event.currentTarget.setPointerCapture?.(event.pointerId)
  }

  function continueResize(event: PointerEvent<HTMLButtonElement>) {
    const activeResize = activeResizeRef.current
    if (!activeResize || event.pointerId !== activeResize.pointerId) return
    activeResize.latestX = event.clientX
    if (animationFrameRef.current === null) {
      animationFrameRef.current = requestAnimationFrame(applyLatestWidth)
    }
  }

  function finishResize(event: PointerEvent<HTMLButtonElement>) {
    const activeResize = activeResizeRef.current
    if (!activeResize || event.pointerId !== activeResize.pointerId) return
    activeResize.latestX = event.clientX
    cancelScheduledFrame()
    applyLatestWidth()
    activeResizeRef.current = null
    event.currentTarget.releasePointerCapture?.(event.pointerId)
  }

  function cancelResize(event: PointerEvent<HTMLButtonElement>) {
    const activeResize = activeResizeRef.current
    if (!activeResize || event.pointerId !== activeResize.pointerId) return
    cancelScheduledFrame()
    applyLatestWidth()
    activeResizeRef.current = null
  }

  function resizeWithKeyboard(columnKey: TKey, event: KeyboardEvent<HTMLButtonElement>) {
    const direction = event.key === 'ArrowRight' ? 1 : event.key === 'ArrowLeft' ? -1 : 0
    if (!direction) return
    const column = definitions.find((item) => item.key === columnKey)
    if (!column) return
    event.preventDefault()
    event.stopPropagation()
    const step = event.shiftKey ? 32 : 16
    setWidths((current) => {
      const nextWidth = Math.max(column.minWidth, current[columnKey] + direction * step)
      return current[columnKey] === nextWidth ? current : { ...current, [columnKey]: nextWidth }
    })
  }

  return { startResize, continueResize, finishResize, cancelResize, resizeWithKeyboard }
}
