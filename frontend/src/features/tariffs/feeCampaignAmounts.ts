const centsPerRuble = 100

function toCents(value: number) {
  return Math.round((value + Number.EPSILON) * centsPerRuble)
}

export function calculateFeeCampaignTargetAmount(contributionAmount: number | null, participantCount: number) {
  if (contributionAmount === null || contributionAmount <= 0 || participantCount <= 0) {
    return 0
  }

  return (toCents(contributionAmount) * participantCount) / centsPerRuble
}

export function calculateFeeCampaignContributionAmount(targetAmount: number | null, participantCount: number) {
  if (targetAmount === null || targetAmount <= 0 || participantCount <= 0) {
    return 0
  }

  return Math.ceil(toCents(targetAmount) / participantCount) / centsPerRuble
}

export function calculateFeeCampaignLastContribution(targetAmount: number, contributionAmount: number, participantCount: number) {
  if (targetAmount <= 0 || contributionAmount <= 0 || participantCount <= 0) {
    return 0
  }

  const remainingCents = toCents(targetAmount) - toCents(contributionAmount) * (participantCount - 1)
  return Math.max(remainingCents, 0) / centsPerRuble
}

export function areFeeCampaignAmountsEqual(firstAmount: number | null, secondAmount: number | null): boolean {
  if (firstAmount === null || secondAmount === null) {
    return false
  }

  return toCents(firstAmount) === toCents(secondAmount)
}
