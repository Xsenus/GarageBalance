import { expect, it } from 'vitest'
import { describeMeterReplacement } from '../../shared/meterReplacementDescription'

it('explains a 1540 reading after replacement from 1160 rather than subtracting the old 1420 reading', () => {
  const text = describeMeterReplacement({ deviceId: 'new', date: '2026-10-01', oldSerial: 'OLD', oldFinalValue: 1420,
    newSerial: 'NEW', newInitialValue: 1160, currentValue: 1540, removedDevicesConsumption: 0, totalConsumption: 380 })
  expect(text).toContain('Замена 01.10.2026')
  expect(text).toContain('старый № OLD — конечное 1 420')
  expect(text).toContain('начальное 1 160, текущее 1 540')
  expect(text).toContain('Расход 380 = снятые приборы 0 + новый 380')
})
