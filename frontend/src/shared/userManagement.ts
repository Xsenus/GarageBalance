import type { ManagedRoleDto, ManagedUserDto } from '../services/usersApi'
import { getManagedUserValidationErrors, getPasswordPolicyErrors } from './validation'

export type UserFormState = {
  email: string
  displayName: string
  password: string
  roleCode: string
  isActive: boolean
}

export function getPrimaryRoleCode(user: ManagedUserDto | undefined, roles: ManagedRoleDto[]) {
  return user?.roles[0] ?? roles[0]?.code ?? ''
}

export function getRoleLabel(roleCode: string, roles: ManagedRoleDto[]) {
  return roles.find((role) => role.code === roleCode)?.name ?? roleCode
}

export function getUserEditorValidationErrors(form: UserFormState, mode: 'create' | 'edit') {
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

  return errors
}
