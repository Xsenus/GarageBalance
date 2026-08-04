// @vitest-environment node
import { describe, expect, it, vi } from 'vitest'
import {
  formatPrototypeChangeValue,
  handleEditableInputKeyDown,
  shouldCommitEditableInputOnBlur,
  wasEditableInputCommittedOnEnter,
} from './prototypeEditing'

describe('prototype editing helpers', () => {
  it('commits an editable value and prevents form submit on Enter', () => {
    const preventDefault = vi.fn()
    const onCommit = vi.fn()
    const currentTarget = {}

    handleEditableInputKeyDown({ key: 'Enter', preventDefault, currentTarget } as never, onCommit)

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

  it('suppresses only the blur caused by an Enter commit', () => {
    const preventDefault = vi.fn()
    const onCommit = vi.fn()
    const target = {}

    handleEditableInputKeyDown({ key: 'Enter', preventDefault, currentTarget: target } as never, onCommit)

    expect(preventDefault).toHaveBeenCalledOnce()
    expect(onCommit).toHaveBeenCalledOnce()
    expect(wasEditableInputCommittedOnEnter(target)).toBe(true)
    expect(shouldCommitEditableInputOnBlur(target)).toBe(false)
    expect(wasEditableInputCommittedOnEnter(target)).toBe(false)
    expect(shouldCommitEditableInputOnBlur(target)).toBe(true)
  })

  it('commits a regular blur that was not caused by Enter', () => {
    expect(shouldCommitEditableInputOnBlur({})).toBe(true)
  })

  it('formats blank and nonblank change values', () => {
    expect(formatPrototypeChangeValue('   ')).toBe('Пусто')
    expect(formatPrototypeChangeValue('  42,5  ')).toBe('42,5')
  })
})
