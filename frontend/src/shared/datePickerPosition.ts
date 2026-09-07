export function datePickerPosition(
  anchor: Pick<DOMRect, 'top' | 'bottom' | 'right'>,
  height: number,
  viewport: { width: number; height: number; left?: number; top?: number },
  preference: 'above' | 'below',
) {
  const leftEdge = (viewport.left ?? 0) + 8
  const topEdge = (viewport.top ?? 0) + 8
  const bottomEdge = topEdge + Math.max(0, viewport.height - 16)
  const width = Math.max(0, Math.min(292, viewport.width - 16))
  const above = Math.max(0, Math.min(anchor.top - 6, bottomEdge) - topEdge)
  const below = Math.max(0, bottomEdge - Math.max(anchor.bottom + 6, topEdge))
  const openAbove = preference === 'above'
    ? above >= height || above >= below
    : below < height && above > below
  const maxHeight = openAbove ? above : below
  return {
    left: Math.max(leftEdge, Math.min(anchor.right - width, leftEdge + viewport.width - 16 - width)),
    top: openAbove ? Math.max(topEdge, Math.min(anchor.top - 6, bottomEdge) - Math.min(height, maxHeight)) : Math.min(bottomEdge, Math.max(topEdge, anchor.bottom + 6)),
    width,
    maxHeight,
  }
}
