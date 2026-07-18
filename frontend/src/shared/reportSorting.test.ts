import { describe, expect, it } from 'vitest'
import { advanceReportSort } from './reportSorting'

describe('report sorting', () => {
  it('cycles a column through ascending, descending and default order', () => {
    const ascending = advanceReportSort(null, 'date')
    const descending = advanceReportSort(ascending, 'date')

    expect(ascending).toEqual({ field: 'date', direction: 'asc' })
    expect(descending).toEqual({ field: 'date', direction: 'desc' })
    expect(advanceReportSort(descending, 'date')).toBeNull()
  })

  it('starts a newly selected column with ascending direction', () => {
    expect(advanceReportSort({ field: 'date', direction: 'desc' }, 'amount')).toEqual({ field: 'amount', direction: 'asc' })
  })
})
