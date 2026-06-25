import { describe, expect, it } from 'vitest'
import type { ManagedRoleDto, ManagedUserDto } from '../services/usersApi'
import { getPrimaryRoleCode, getRoleLabel, getUserEditorValidationErrors } from './userManagement'

const roles: ManagedRoleDto[] = [
  { code: 'administrator', name: 'Администратор', permissions: ['users.manage'] },
  { code: 'operator', name: 'Оператор', permissions: ['payments.read'] },
]

describe('user management helpers', () => {
  it('chooses the user primary role or falls back to the first available role', () => {
    expect(getPrimaryRoleCode(createUser(['operator', 'administrator']), roles)).toBe('operator')
    expect(getPrimaryRoleCode(undefined, roles)).toBe('administrator')
    expect(getPrimaryRoleCode(undefined, [])).toBe('')
  })

  it('returns a role label or the code when role metadata is missing', () => {
    expect(getRoleLabel('operator', roles)).toBe('Оператор')
    expect(getRoleLabel('accountant', roles)).toBe('accountant')
  })

  it('validates user creation through the shared validation rules', () => {
    expect(getUserEditorValidationErrors({
      email: '',
      displayName: '',
      password: 'weak',
      roleCode: '',
      isActive: true,
    }, 'create')).toEqual([
      'Укажите email пользователя.',
      'Укажите имя пользователя.',
      'Пароль должен быть не короче 8 символов.',
      'Добавьте заглавную букву в пароль.',
      'Добавьте хотя бы одну цифру в пароль.',
      'Выберите роль пользователя.',
    ])
  })

  it('validates user editing without requiring email or password by default', () => {
    expect(getUserEditorValidationErrors({
      email: '',
      displayName: 'Оператор',
      password: '',
      roleCode: 'operator',
      isActive: true,
    }, 'edit')).toEqual([])

    expect(getUserEditorValidationErrors({
      email: '',
      displayName: '',
      password: 'weak',
      roleCode: '',
      isActive: true,
    }, 'edit')).toEqual([
      'Укажите имя пользователя.',
      'Выберите роль пользователя.',
      'Пароль должен быть не короче 8 символов.',
      'Добавьте заглавную букву в пароль.',
      'Добавьте хотя бы одну цифру в пароль.',
    ])
  })
})

function createUser(roles: string[]): ManagedUserDto {
  return {
    id: 'user-1',
    email: 'operator@example.com',
    displayName: 'Оператор',
    isActive: true,
    createdAtUtc: '2026-06-25T00:00:00Z',
    lastLoginAtUtc: null,
    roles,
    permissions: [],
  }
}
