// @vitest-environment node
import { describe, expect, it, vi } from 'vitest'
import { formatPrototypeChangeValue, handleEditableInputKeyDown } from './prototypeEditing'

describe('prototype editing helpers', () => {
  it('commits an editable value and prevents form submit on Enter', () => {
    const preventDefault = vi.fn()
    const onCommit = vi.fn()

    handleEditableInputKeyDown({ key: 'Enter', preventDefault } as never, onCommit)

    expect(preventDefault).toHaveBeenCalledOnce()
    expect(onCommit).toHaveBeenCalledOnce()
  })

  it('ignores keys other than Enter', () => {
    const preventDefault = vi.fn()
    const onCommit = vi.fn()

    handleEditableInputKeyDown({ key: 'Escape', preventDefault } as never, onCommit)

    expect(preventDefault).not.toHaveBeenCalled()
    expect(onCommit).not.toHaveBeenCalled()
  })

  it('formats blank and nonblank change values', () => {
    expect(formatPrototypeChangeValue('   ')).toBe('Пусто')
    expect(formatPrototypeChangeValue('  42,5  ')).toBe('42,5')
  })
})
