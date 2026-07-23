type GarageSearchCandidate = {
  id: string
  number: string
  ownerName: string
}

function getGarageSearchRank(garage: GarageSearchCandidate, normalizedSearch: string) {
  const normalizedNumber = garage.number.toLocaleLowerCase('ru')
  if (normalizedNumber === normalizedSearch) {
    return 0
  }
  if (normalizedNumber.startsWith(normalizedSearch)) {
    return 1
  }
  if (normalizedNumber.includes(normalizedSearch)) {
    return 2
  }
  return 3
}

export function rankGarageSearchResults<T extends GarageSearchCandidate>(garages: T[], search: string): T[] {
  const normalizedSearch = search.trim().toLocaleLowerCase('ru')
  if (!normalizedSearch) {
    return garages
  }

  return garages
    .filter((garage) => garage.number.toLocaleLowerCase('ru').includes(normalizedSearch)
      || garage.ownerName.toLocaleLowerCase('ru').includes(normalizedSearch))
    .toSorted((left, right) => {
      const rankDifference = getGarageSearchRank(left, normalizedSearch) - getGarageSearchRank(right, normalizedSearch)
      if (rankDifference !== 0) {
        return rankDifference
      }

      const numberDifference = left.number.localeCompare(right.number, 'ru', { numeric: true, sensitivity: 'base' })
      return numberDifference !== 0 ? numberDifference : left.id.localeCompare(right.id)
    })
}
