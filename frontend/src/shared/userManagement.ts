import type { ManagedRoleDto, ManagedUserDto } from '../services/usersApi'
import type { ChangePreview } from './changePreview'
import { appendChangePreview, formatChangeText, formatSensitiveChange } from './changePreview'
import { getManagedUserValidationErrors, getPasswordPolicyErrors } from './validation'

export type UserFormState = {
  email: string
  displayName: string
  password: string
  passwordConfirmation: string
  roleCodes: string[]
  isActive: boolean
  deactivationReason: string
}

export type UserEditChange = ChangePreview

export function getInitialRoleCodes(user: ManagedUserDto | undefined, roles: ManagedRoleDto[]) {
  if (user) {
    return user.roles.filter((roleCode) => roles.some((role) => role.code === roleCode))
  }

  const preferredRole = roles.find((role) => role.code === 'operator') ?? roles[0]
  return preferredRole ? [preferredRole.code] : []
}

export function getRoleLabel(roleCode: string, roles: ManagedRoleDto[]) {
  return roles.find((role) => role.code === roleCode)?.name ?? roleCode
}

export function getRoleLabels(roleCodes: readonly string[], roles: ManagedRoleDto[]) {
  return roleCodes.map((roleCode) => getRoleLabel(roleCode, roles)).sort((left, right) => left.localeCompare(right, 'ru')).join(', ')
}

export function getUserStatusLabel(isActive: boolean) {
  return isActive ? 'Активен' : 'Отключен'
}

export function getUserEditorChanges(form: UserFormState, user: ManagedUserDto, roles: ManagedRoleDto[]): UserEditChange[] {
  const changes: UserEditChange[] = []
  const nextDisplayName = form.displayName.trim()
  const currentRoleCodes = [...user.roles].sort()
  const nextRoleCodes = [...form.roleCodes].sort()

  appendChangePreview(changes, 'Имя', formatChangeText(user.displayName), formatChangeText(nextDisplayName))

  if (currentRoleCodes.join('\n') !== nextRoleCodes.join('\n')) {
    changes.push({
      field: 'Роли',
      before: getRoleLabels(currentRoleCodes, roles),
      after: getRoleLabels(nextRoleCodes, roles),
    })
  }

  appendChangePreview(changes, 'Статус', getUserStatusLabel(user.isActive), getUserStatusLabel(form.isActive))

  if (form.password.trim()) {
    appendChangePreview(changes, 'Пароль', 'Без изменения', formatSensitiveChange(form.password))
  }

  return changes
}

export function getUserEditorValidationErrors(form: UserFormState, mode: 'create' | 'edit', user?: ManagedUserDto) {
  const passwordWasEntered = form.password.length > 0 || form.passwordConfirmation.length > 0
  const passwordConfirmationError = passwordWasEntered && form.password !== form.passwordConfirmation
    ? 'Пароль и подтверждение пароля не совпадают.'
    : null

  if (mode === 'create') {
    const errors = getManagedUserValidationErrors(form.email, form.displayName, form.password, form.roleCodes)
    if (passwordConfirmationError) {
      errors.push(passwordConfirmationError)
    }

    return errors
  }

  const errors: string[] = []
  if (!form.displayName.trim()) {
    errors.push('Укажите имя пользователя.')
  }

  if (form.roleCodes.length === 0) {
    errors.push('Выберите хотя бы одну роль пользователя.')
  }

  if (passwordWasEntered) {
    errors.push(...getPasswordPolicyErrors(form.password, 'Укажите новый пароль или оставьте поле пустым.'))
  }

  if (passwordConfirmationError) {
    errors.push(passwordConfirmationError)
  }

  if (user?.isActive && !form.isActive && !form.deactivationReason.trim()) {
    errors.push('Укажите причину отключения пользователя.')
  }

  return errors
}
