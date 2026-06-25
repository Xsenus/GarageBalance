import { describe, expect, it } from 'vitest'
import { createEmptyPage, createFallbackPage, pageSizeOptions } from './pagination'

describe('pagination helpers', () => {
  it('keeps shared page size options stable', () => {
    expect(pageSizeOptions).toEqual([10, 25, 50, 100])
  })

  it('creates an empty page with default and custom limits', () => {
    expect(createEmptyPage<string>()).toEqual({ items: [], totalCount: 0, offset: 0, limit: 25 })
    expect(createEmptyPage<string>(50)).toEqual({ items: [], totalCount: 0, offset: 0, limit: 50 })
  })

  it('creates fallback slices without losing the total item count', () => {
    const items = ['one', 'two', 'three', 'four']

    expect(createFallbackPage(items, 1, 2)).toEqual({
      items: ['two', 'three'],
      totalCount: 4,
      offset: 1,
      limit: 2,
    })

    expect(createFallbackPage(items, 3, 2)).toEqual({
      items: ['four'],
      totalCount: 4,
      offset: 3,
      limit: 2,
    })
  })
})
