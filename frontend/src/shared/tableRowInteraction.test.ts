import { describe, expect, it } from 'vitest'
import { isInteractiveTableRowTarget } from './tableRowInteraction'

describe('isInteractiveTableRowTarget', () => {
  it('distinguishes row content from controls and nested accessible actions', () => {
    const row = document.createElement('div')
    const text = document.createElement('span')
    const button = document.createElement('button')
    const combobox = document.createElement('div')
    const comboboxText = document.createElement('span')
    combobox.setAttribute('role', 'combobox')
    combobox.append(comboboxText)
    row.append(text, button, combobox)

    expect(isInteractiveTableRowTarget(text)).toBe(false)
    expect(isInteractiveTableRowTarget(button)).toBe(true)
    expect(isInteractiveTableRowTarget(comboboxText)).toBe(true)
    expect(isInteractiveTableRowTarget(null)).toBe(false)
  })
})
