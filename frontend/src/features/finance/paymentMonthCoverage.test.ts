// @vitest-environment node

import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

describe('expense worksheet month coverage', () => {
  const source = readFileSync(resolve(process.cwd(), 'src', 'features', 'finance', 'FinancePanel.tsx'), 'utf8')

  it('uses the current month, a selectable range and shared quick periods without fixed calendar values', () => {
    expect(source).toContain('useState(() => getCurrentMonthInputValue())')
    expect(source).toMatch(/<LocalizedDatePicker\s+ariaLabel="Месяц выплат с"\s+mode="month"/u)
    expect(source).toMatch(/<LocalizedDatePicker\s+ariaLabel="Месяц выплат по"\s+mode="month"/u)
    expect(source).toMatch(/<ReportPeriodQuickSelect[\s\S]*?valueFrom=\{expenseWorksheetMonthFrom\}[\s\S]*?valueTo=\{expenseWorksheetMonthTo\}[\s\S]*?onSelect=/u)
    expect(source).not.toMatch(/2026-0[456]|апрель 2026|май 2026|июнь 2026/u)
  })
})
