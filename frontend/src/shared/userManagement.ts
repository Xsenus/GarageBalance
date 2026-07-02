import type { ManagedRoleDto, ManagedUserDto } from '../services/usersApi'
import { getManagedUserValidationErrors, getPasswordPolicyErrors } from './validation'

export type UserFormState = {
  email: string
  displayName: string
  password: string
  roleCode: string
  isActive: boolean
  deactivationReason: string
}

export type UserEditChange = {
  field: string
  before: string
  after: string
}

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

  if (nextDisplayName !== user.displayName) {
    changes.push({
      field: 'Имя',
      before: user.displayName,
      after: nextDisplayName,
    })
  }

  if (form.roleCode !== currentRoleCode) {
    changes.push({
      field: 'Роль',
      before: getRoleLabel(currentRoleCode, roles),
      after: getRoleLabel(form.roleCode, roles),
    })
  }

  if (form.isActive !== user.isActive) {
    changes.push({
      field: 'Статус',
      before: getUserStatusLabel(user.isActive),
      after: getUserStatusLabel(form.isActive),
    })
  }

  if (form.password.trim()) {
    changes.push({
      field: 'Пароль',
      before: 'Без изменения',
      after: 'Будет задан новый пароль',
    })
  }

  return changes
}

export function getUserEditorValidationErrors(form: UserFormState, mode: 'create' | 'edit', user?: ManagedUserDto) {
  if (mode === 'create') {
    return getManagedUserValidationErrors(form.email, form.displayName, form.password, form.roleCode)
  }

  const errors: string[] = []
  if (!form.displayName.trim()) {
    errors.push('Укажите имя пользователя.')
  }

  if (!form.roleCode) {
    errors.push('Выберите роль пользователя.')
  }

  if (form.password.trim()) {
    errors.push(...getPasswordPolicyErrors(form.password, 'Укажите новый пароль или оставьте поле пустым.'))
  }

  if (user?.isActive && !form.isActive && !form.deactivationReason.trim()) {
    errors.push('Укажите причину отключения пользователя.')
  }

  return errors
}
