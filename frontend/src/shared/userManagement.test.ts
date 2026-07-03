import { describe, expect, it } from 'vitest'
import type { ManagedRoleDto, ManagedUserDto } from '../services/usersApi'
import { getPrimaryRoleCode, getRoleLabel, getUserEditorChanges, getUserEditorValidationErrors } from './userManagement'

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

  it('returns no edit changes when the user form keeps the same values', () => {
    const user = createUser(['operator'])
    expect(getUserEditorChanges({
      email: user.email,
      displayName: `  ${user.displayName}  `,
      password: '',
      roleCode: 'operator',
      isActive: true,
      deactivationReason: '',
    }, user, roles)).toEqual([])
  })

  it('describes editable user changes with human-readable labels', () => {
    const user = createUser(['operator'])
    expect(getUserEditorChanges({
      email: user.email,
      displayName: 'Старший оператор',
      password: 'StrongPass123',
      roleCode: 'administrator',
      isActive: false,
      deactivationReason: 'Доступ больше не нужен',
    }, user, roles)).toEqual([
      { field: 'Имя', before: 'Оператор', after: 'Старший оператор' },
      { field: 'Роль', before: 'Оператор', after: 'Администратор' },
      { field: 'Статус', before: 'Активен', after: 'Отключен' },
      { field: 'Пароль', before: 'Без изменения', after: 'изменено' },
    ])
  })

  it('validates user creation through the shared validation rules', () => {
    expect(getUserEditorValidationErrors({
      email: '',
      displayName: '',
      password: 'weak',
      roleCode: '',
      isActive: true,
      deactivationReason: '',
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
      deactivationReason: '',
    }, 'edit')).toEqual([])

    expect(getUserEditorValidationErrors({
      email: '',
      displayName: '',
      password: 'weak',
      roleCode: '',
      isActive: true,
      deactivationReason: '',
    }, 'edit')).toEqual([
      'Укажите имя пользователя.',
      'Выберите роль пользователя.',
      'Пароль должен быть не короче 8 символов.',
      'Добавьте заглавную букву в пароль.',
      'Добавьте хотя бы одну цифру в пароль.',
    ])
  })

  it('requires a reason when an active user is disabled from the edit form', () => {
    const activeUser = createUser(['operator'])
    expect(getUserEditorValidationErrors({
      email: activeUser.email,
      displayName: activeUser.displayName,
      password: '',
      roleCode: 'operator',
      isActive: false,
      deactivationReason: '',
    }, 'edit', activeUser)).toContain('Укажите причину отключения пользователя.')

    expect(getUserEditorValidationErrors({
      email: activeUser.email,
      displayName: activeUser.displayName,
      password: '',
      roleCode: 'operator',
      isActive: false,
      deactivationReason: 'Уволился',
    }, 'edit', activeUser)).toEqual([])
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
