// @vitest-environment node
import { describe, expect, it } from 'vitest'
import type { ManagedRoleDto, ManagedUserDto } from '../services/usersApi'
import { getInitialRoleCodes, getRoleLabel, getRoleLabels, getUserEditorChanges, getUserEditorValidationErrors } from './userManagement'

const roles: ManagedRoleDto[] = [
  { code: 'administrator', name: 'Администратор', permissions: ['users.manage'] },
  { code: 'operator', name: 'Оператор', permissions: ['payments.read'] },
]

describe('user management helpers', () => {
  it('keeps all known user roles or defaults a new user to operator', () => {
    expect(getInitialRoleCodes(createUser(['operator', 'administrator']), roles)).toEqual(['operator', 'administrator'])
    expect(getInitialRoleCodes(createUser(['missing']), roles)).toEqual([])
    expect(getInitialRoleCodes(undefined, roles)).toEqual(['operator'])
    expect(getInitialRoleCodes(undefined, [])).toEqual([])
  })

  it('returns a role label or the code when role metadata is missing', () => {
    expect(getRoleLabel('operator', roles)).toBe('Оператор')
    expect(getRoleLabel('accountant', roles)).toBe('accountant')
    expect(getRoleLabels(['operator', 'administrator'], roles)).toBe('Администратор, Оператор')
  })

  it('returns no edit changes when the user form keeps the same values', () => {
    const user = createUser(['operator'])
    expect(getUserEditorChanges({
      email: user.email,
      displayName: `  ${user.displayName}  `,
      password: '',
      passwordConfirmation: '',
      roleCodes: ['operator'],
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
      passwordConfirmation: 'StrongPass123',
      roleCodes: ['operator', 'administrator'],
      isActive: false,
      deactivationReason: 'Доступ больше не нужен',
    }, user, roles)).toEqual([
      { field: 'Имя', before: 'Оператор', after: 'Старший оператор' },
      { field: 'Роли', before: 'Оператор', after: 'Администратор, Оператор' },
      { field: 'Статус', before: 'Активен', after: 'Отключен' },
      { field: 'Пароль', before: 'Без изменения', after: 'изменено' },
    ])
  })

  it('validates user creation through the shared validation rules', () => {
    expect(getUserEditorValidationErrors({
      email: '',
      displayName: '',
      password: 'weak',
      passwordConfirmation: 'weak',
      roleCodes: [],
      isActive: true,
      deactivationReason: '',
    }, 'create')).toEqual([
      'Укажите email пользователя.',
      'Укажите имя пользователя.',
      'Пароль должен быть не короче 8 символов.',
      'Выберите хотя бы одну роль пользователя.',
    ])
  })

  it('validates user editing without requiring email or password by default', () => {
    expect(getUserEditorValidationErrors({
      email: '',
      displayName: 'Оператор',
      password: '',
      passwordConfirmation: '',
      roleCodes: ['operator'],
      isActive: true,
      deactivationReason: '',
    }, 'edit')).toEqual([])

    expect(getUserEditorValidationErrors({
      email: '',
      displayName: '',
      password: 'weak',
      passwordConfirmation: 'weak',
      roleCodes: [],
      isActive: true,
      deactivationReason: '',
    }, 'edit')).toEqual([
      'Укажите имя пользователя.',
      'Выберите хотя бы одну роль пользователя.',
      'Пароль должен быть не короче 8 символов.',
    ])
  })

  it('requires a reason when an active user is disabled from the edit form', () => {
    const activeUser = createUser(['operator'])
    expect(getUserEditorValidationErrors({
      email: activeUser.email,
      displayName: activeUser.displayName,
      password: '',
      passwordConfirmation: '',
      roleCodes: ['operator'],
      isActive: false,
      deactivationReason: '',
    }, 'edit', activeUser)).toContain('Укажите причину отключения пользователя.')

    expect(getUserEditorValidationErrors({
      email: activeUser.email,
      displayName: activeUser.displayName,
      password: '',
      passwordConfirmation: '',
      roleCodes: ['operator'],
      isActive: false,
      deactivationReason: 'Уволился',
    }, 'edit', activeUser)).toEqual([])
  })

  it('allows disabling a user without a reason when action comments are optional', () => {
    const activeUser = createUser(['operator'])

    expect(getUserEditorValidationErrors({
      email: activeUser.email,
      displayName: activeUser.displayName,
      password: '',
      passwordConfirmation: '',
      roleCodes: ['operator'],
      isActive: false,
      deactivationReason: '',
    }, 'edit', activeUser, false)).toEqual([])
  })

  it('requires matching password fields when a password is entered', () => {
    const user = createUser(['operator'])
    expect(getUserEditorValidationErrors({
      email: user.email,
      displayName: user.displayName,
      password: 'StrongPass123',
      passwordConfirmation: 'StrongPass124',
      roleCodes: ['operator'],
      isActive: true,
      deactivationReason: '',
    }, 'edit', user)).toContain('Пароль и подтверждение пароля не совпадают.')
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
