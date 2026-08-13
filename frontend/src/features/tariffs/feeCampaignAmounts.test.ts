import { describe, expect, it } from 'vitest'
import { areFeeCampaignAmountsEqual, calculateFeeCampaignContributionAmount, calculateFeeCampaignLastContribution, calculateFeeCampaignTargetAmount } from './feeCampaignAmounts'

describe('fee campaign amount calculations', () => {
  it('calculates the exact total from the contribution and participant count', () => {
    expect(calculateFeeCampaignTargetAmount(123, 22)).toBe(2706)
    expect(calculateFeeCampaignTargetAmount(100.005, 3)).toBe(300.03)
  })

  it('calculates a contribution that can reach the entered total', () => {
    expect(calculateFeeCampaignContributionAmount(2706, 22)).toBe(123)
    expect(calculateFeeCampaignContributionAmount(1000, 3)).toBe(333.34)
    expect(calculateFeeCampaignLastContribution(1000, 333.34, 3)).toBe(333.32)
  })

  it('returns zero until a positive amount and at least one participant are available', () => {
    expect(calculateFeeCampaignTargetAmount(null, 22)).toBe(0)
    expect(calculateFeeCampaignTargetAmount(100, 0)).toBe(0)
    expect(calculateFeeCampaignContributionAmount(null, 22)).toBe(0)
    expect(calculateFeeCampaignContributionAmount(1000, 0)).toBe(0)
    expect(calculateFeeCampaignLastContribution(0, 100, 3)).toBe(0)
  })

  it('compares amounts by accounting cents', () => {
    expect(areFeeCampaignAmountsEqual(333.339, 333.34)).toBe(true)
    expect(areFeeCampaignAmountsEqual(333.34, 333.35)).toBe(false)
    expect(areFeeCampaignAmountsEqual(null, 0)).toBe(false)
  })
})
