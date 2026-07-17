export function chooseTestParallelism(availableWorkers) {
  const normalizedWorkers = Number.isInteger(availableWorkers) && availableWorkers > 0
    ? availableWorkers
    : 1

  return Math.min(4, normalizedWorkers, Math.floor(normalizedWorkers / 2) + 1)
}
