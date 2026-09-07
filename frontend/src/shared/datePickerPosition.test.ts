// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { datePickerPosition } from './datePickerPosition'

describe('date picker viewport placement', () => {
  it('uses the requested side when it fits and flips at viewport edges', () => {
    const viewport = { width: 1024, height: 800 }
    expect(datePickerPosition({ top: 400, bottom: 440, right: 700 }, 300, viewport, 'above')).toEqual({ left: 408, top: 94, width: 292, maxHeight: 386 })
    expect(datePickerPosition({ top: 400, bottom: 440, right: 700 }, 300, viewport, 'below').top).toBe(446)
    expect(datePickerPosition({ top: 20, bottom: 60, right: 700 }, 300, viewport, 'above').top).toBe(66)
    expect(datePickerPosition({ top: 740, bottom: 780, right: 700 }, 300, viewport, 'below').top).toBe(434)
  })

  it('clamps both horizontal edges and narrows the popup on a small screen', () => {
    expect(datePickerPosition({ top: 20, bottom: 60, right: 30 }, 300, { width: 320, height: 600 }, 'below').left).toBe(8)
    expect(datePickerPosition({ top: 20, bottom: 60, right: 500 }, 300, { width: 320, height: 600 }, 'below').left).toBe(20)
    expect(datePickerPosition({ top: 20, bottom: 60, right: 200 }, 300, { width: 240, height: 600 }, 'below')).toMatchObject({ left: 8, width: 224 })
  })

  it('limits height to the larger free side without leaving the viewport', () => {
    expect(datePickerPosition({ top: 200, bottom: 240, right: 300 }, 500, { width: 400, height: 400 }, 'below')).toEqual({ left: 8, top: 8, width: 292, maxHeight: 186 })
    expect(datePickerPosition({ top: 100, bottom: 140, right: 300 }, 500, { width: 400, height: 400 }, 'above')).toMatchObject({ top: 146, maxHeight: 246 })
    expect(datePickerPosition({ top: -100, bottom: -60, right: 300 }, 300, { width: 400, height: 400 }, 'below').top).toBe(8)
    expect(datePickerPosition({ top: 600, bottom: 640, right: 300 }, 300, { width: 400, height: 400 }, 'above').top).toBe(92)
  })

  it('accounts for a shifted visual viewport and zero available space', () => {
    expect(datePickerPosition({ top: 120, bottom: 160, right: 300 }, 300, { width: 320, height: 400, left: 40, top: 100 }, 'above')).toMatchObject({ left: 48, top: 166, maxHeight: 326 })
    expect(datePickerPosition({ top: 0, bottom: 0, right: 0 }, 300, { width: 0, height: 0 }, 'below')).toEqual({ left: 8, top: 8, width: 0, maxHeight: 0 })
  })
})
