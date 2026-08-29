import { useEffect, useRef } from 'react'
import type { Dispatch, KeyboardEvent, PointerEvent, SetStateAction } from 'react'

export function usePointerResize<TElement extends HTMLElement>(onResize: (clientX: number) => void, onEnd?: (cancelled: boolean) => void) {
  const pointerIdRef = useRef<number | null>(null)
  const latestXRef = useRef(0)
  const animationFrameRef = useRef<number | null>(null)

  function cancelScheduledFrame() {
    if (animationFrameRef.current !== null) {
      cancelAnimationFrame(animationFrameRef.current)
      animationFrameRef.current = null
    }
  }

  function applyLatestPosition() {
    animationFrameRef.current = null
    onResize(latestXRef.current)
  }

  useEffect(() => () => {
    cancelScheduledFrame()
    pointerIdRef.current = null
  }, [])

  function startPointerResize(event: PointerEvent<TElement>) {
    if (event.button > 0) return
    event.preventDefault()
    event.stopPropagation()
    cancelScheduledFrame()
    pointerIdRef.current = event.pointerId
    latestXRef.current = event.clientX
    event.currentTarget.setPointerCapture?.(event.pointerId)
    applyLatestPosition()
  }

  function continuePointerResize(event: PointerEvent<TElement>) {
    if (event.pointerId !== pointerIdRef.current) return
    latestXRef.current = event.clientX
    if (animationFrameRef.current === null) {
      animationFrameRef.current = requestAnimationFrame(applyLatestPosition)
    }
  }

  function finishPointerResize(event: PointerEvent<TElement>) {
    if (event.pointerId !== pointerIdRef.current) return
    latestXRef.current = event.clientX
    cancelScheduledFrame()
    applyLatestPosition()
    pointerIdRef.current = null
    event.currentTarget.releasePointerCapture?.(event.pointerId)
    onEnd?.(false)
  }

  function cancelPointerResize(event: PointerEvent<TElement>) {
    if (event.pointerId !== pointerIdRef.current) return
    cancelScheduledFrame()
    applyLatestPosition()
    pointerIdRef.current = null
    onEnd?.(true)
  }

  return { startPointerResize, continuePointerResize, finishPointerResize, cancelPointerResize }
}

type ResizableColumnDefinition<TKey extends string> = { key: TKey; minWidth: number }
type ColumnWidths<TKey extends string> = Record<TKey, number>
type ActiveResize<TKey extends string> = {
  columnKey: TKey
  minWidth: number
  startWidth: number
  startX: number
}

export function useColumnResize<TKey extends string>(
  definitions: ReadonlyArray<ResizableColumnDefinition<TKey>>,
  widths: ColumnWidths<TKey>,
  setWidths: Dispatch<SetStateAction<ColumnWidths<TKey>>>,
) {
  const activeResizeRef = useRef<ActiveResize<TKey> | null>(null)
  const pointerResize = usePointerResize<HTMLButtonElement>((clientX) => {
    const activeResize = activeResizeRef.current
    if (!activeResize) return
    const nextWidth = Math.max(activeResize.minWidth, activeResize.startWidth + clientX - activeResize.startX)
    setWidths((current) => current[activeResize.columnKey] === nextWidth
      ? current
      : { ...current, [activeResize.columnKey]: nextWidth })
  }, () => {
    activeResizeRef.current = null
  })

  function startResize(columnKey: TKey, event: PointerEvent<HTMLButtonElement>) {
    if (event.button > 0) return
    const column = definitions.find((item) => item.key === columnKey)
    if (!column) return
    activeResizeRef.current = {
      columnKey,
      minWidth: column.minWidth,
      startWidth: widths[columnKey],
      startX: event.clientX,
    }
    pointerResize.startPointerResize(event)
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

  return {
    startResize,
    continueResize: pointerResize.continuePointerResize,
    finishResize: pointerResize.finishPointerResize,
    cancelResize: pointerResize.cancelPointerResize,
    resizeWithKeyboard,
  }
}
