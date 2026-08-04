import type { KeyboardEvent } from 'react'

const inputsCommittedOnEnter = new WeakSet<EventTarget>()

export function handleEditableInputKeyDown(
  event: KeyboardEvent<HTMLInputElement | HTMLSelectElement>,
  onCommit: () => void | Promise<void>,
) {
  if (event.key === 'Enter') {
    event.preventDefault()
    inputsCommittedOnEnter.add(event.currentTarget)
    void onCommit()
  }
}

export function shouldCommitEditableInputOnBlur(target: EventTarget) {
  if (inputsCommittedOnEnter.has(target)) {
    inputsCommittedOnEnter.delete(target)
    return false
  }
  return true
}

export function wasEditableInputCommittedOnEnter(target: EventTarget) {
  return inputsCommittedOnEnter.has(target)
}

export function formatPrototypeChangeValue(value: string) {
  return value.trim() || 'Пусто'
}
