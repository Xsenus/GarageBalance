import type { KeyboardEvent } from 'react'

export function handleEditableInputKeyDown(
  event: KeyboardEvent<HTMLInputElement | HTMLSelectElement>,
  onCommit: () => void | Promise<void>,
) {
  if (event.key === 'Enter') {
    event.preventDefault()
    void onCommit()
  }
}

export function formatPrototypeChangeValue(value: string) {
  return value.trim() || 'Пусто'
}
