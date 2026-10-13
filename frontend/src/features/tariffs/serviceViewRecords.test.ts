// @vitest-environment node
import { describe, expect, it } from 'vitest'
import { selectServiceViewRecords } from './serviceViewRecords'

describe('service records in active and deleted views', () => {
  const records = [
    { id: 'active', isArchived: false, isActive: true },
    { id: 'disabled', isArchived: false, isActive: false },
    { id: 'deleted', isArchived: true, isActive: false },
  ]

  it('keeps disabled records in the active view and excludes deleted records', () => {
    expect(selectServiceViewRecords(records, 'active', (row) => row.isArchived).map((row) => row.id))
      .toEqual(['active', 'disabled'])
    expect(records).toHaveLength(3)
  })

  it('shows only deleted records independently of enabled status', () => {
    expect(selectServiceViewRecords(records, 'deleted', (row) => row.isArchived).map((row) => row.id))
      .toEqual(['deleted'])
  })

  it('handles empty views and the irregular payment archive property', () => {
    expect(selectServiceViewRecords([], 'deleted', () => false)).toEqual([])
    expect(selectServiceViewRecords([{ isDeleted: true }], 'active', (row) => row.isDeleted)).toEqual([])
    expect(selectServiceViewRecords([{ isDeleted: true }], 'deleted', (row) => row.isDeleted)).toEqual([{ isDeleted: true }])
  })
})
