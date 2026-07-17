// @vitest-environment node

import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

describe('expense worksheet month coverage', () => {
  const source = readFileSync(resolve(process.cwd(), 'src', 'features', 'finance', 'FinancePanel.tsx'), 'utf8')

  it('uses the current month and the shared localized month picker without fixed calendar periods', () => {
    expect(source).toContain('useState(() => getCurrentMonthInputValue())')
    expect(source).toMatch(/<LocalizedDatePicker\s+ariaLabel="Месяц выплат"\s+mode="month"/u)
    expect(source).not.toMatch(/2026-0[456]|апрель 2026|май 2026|июнь 2026/u)
  })
})
