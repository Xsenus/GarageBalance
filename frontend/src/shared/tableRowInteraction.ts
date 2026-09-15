export function isInteractiveTableRowTarget(target: EventTarget | null) {
  return target instanceof Element && target.closest(
    'button, a, input, textarea, select, [role="button"], [role="link"], [role="menuitem"], [role="combobox"], [contenteditable="true"]',
  ) !== null
}
