import { describe, expect, it } from 'vitest'
import { rankGarageSearchResults } from './garageSearchRanking'

describe('rankGarageSearchResults', () => {
  it('puts an exact number before prefixes, contained matches and owner matches', () => {
    const garages = [
      { id: 'owner', number: '5', ownerName: 'Иванов 7' },
      { id: 'contained-2', number: '117', ownerName: 'Петров' },
      { id: 'prefix-2', number: '71', ownerName: 'Сидоров' },
      { id: 'exact', number: '7', ownerName: 'Орлов' },
      { id: 'contained-1', number: '107', ownerName: 'Федоров' },
      { id: 'prefix-1', number: '70', ownerName: 'Смирнов' },
    ]

    expect(rankGarageSearchResults(garages, ' 7 ').map((garage) => garage.number)).toEqual([
      '7',
      '70',
      '71',
      '107',
      '117',
      '5',
    ])
  })

  it('uses natural number order inside the same relevance group', () => {
    const garages = [
      { id: '100', number: '710', ownerName: 'Иванов' },
      { id: '20', number: '72', ownerName: 'Петров' },
      { id: '3', number: '71', ownerName: 'Сидоров' },
    ]

    expect(rankGarageSearchResults(garages, '7').map((garage) => garage.number)).toEqual(['71', '72', '710'])
  })
})
