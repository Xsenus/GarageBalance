import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { AnnualAccrualHoldNotice } from './AnnualAccrualHoldNotice'

describe('AnnualAccrualHoldNotice', () => {
  it('does not show a hold without an explicit year', () => {
    render(<AnnualAccrualHoldNotice years={[]} />)
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it.each([[2026], [2026, 2028]])('shows only held years and explains monthly billing: %j', (...years) => {
    render(<AnnualAccrualHoldNotice years={years} />)
    expect(screen.getByRole('status')).toHaveTextContent(`Годовые начисления за ${years.join(', ')} год приостановлены`)
    expect(screen.getByRole('status')).toHaveTextContent('Месячные начисления продолжаются с установленного начала учёта гаража.')
    expect(screen.getByRole('status')).not.toHaveTextContent('2027')
  })
})
