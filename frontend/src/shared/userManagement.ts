import type { ManagedRoleDto, ManagedUserDto } from '../services/usersApi'
import type { ChangePreview } from './changePreview'
import { appendChangePreview, formatChangeText, formatSensitiveChange } from './changePreview'
import { getManagedUserValidationErrors, getPasswordPolicyErrors } from './validation'

export type UserFormState = {
  email: string
  displayName: string
  password: string
  passwordConfirmation: string
  roleCode: string
  isActive: boolean
  deactivationReason: string
}

export type UserEditChange = ChangePreview

export function getPrimaryRoleCode(user: ManagedUserDto | undefined, roles: ManagedRoleDto[]) {
  return user?.roles[0] ?? roles[0]?.code ?? ''
}

export function getRoleLabel(roleCode: string, roles: ManagedRoleDto[]) {
  return roles.find((role) => role.code === roleCode)?.name ?? roleCode
}

export function getUserStatusLabel(isActive: boolean) {
  return isActive ? 'Активен' : 'Отключен'
}

export function getUserEditorChanges(form: UserFormState, user: ManagedUserDto, roles: ManagedRoleDto[]): UserEditChange[] {
  const changes: UserEditChange[] = []
  const nextDisplayName = form.displayName.trim()
  const currentRoleCode = getPrimaryRoleCode(user, roles)

  appendChangePreview(changes, 'Имя', formatChangeText(user.displayName), formatChangeText(nextDisplayName))

  if (form.roleCode !== currentRoleCode) {
    changes.push({
      field: 'Роль',
      before: getRoleLabel(currentRoleCode, roles),
      after: getRoleLabel(form.roleCode, roles),
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
    const errors = getManagedUserValidationErrors(form.email, form.displayName, form.password, form.roleCode)
    if (passwordConfirmationError) {
      errors.push(passwordConfirmationError)
    }

    return errors
  }

  const errors: string[] = []
  if (!form.displayName.trim()) {
    errors.push('Укажите имя пользователя.')
  }

  if (!form.roleCode) {
    errors.push('Выберите роль пользователя.')
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
