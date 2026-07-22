import { useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { RotateCcw, Save, Search, ShieldCheck, Trash2, UserPlus, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { CreateManagedUserRequest, ManagedRoleDto, ManagedUserDto, PagedManagedUsersDto, UpdateManagedUserRequest, UserManagementClient } from '../../services/usersApi'
import { permissions, rolePermissionGroups } from '../../shared/accessControl'
import { TableLoadingState } from '../../shared/AsyncState'
import type { ChangePreview } from '../../shared/changePreview'
import { FormError, FormValidationSummary } from '../../shared/formFeedback'
import { FormField } from '../../shared/FormField'
import { formatDateTime } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { createEmptyPage } from '../../shared/pagination'
import { TablePagination } from '../../shared/TablePagination'
import type { UserEditChange, UserFormState } from '../../shared/userManagement'
import { getPrimaryRoleCode, getRoleLabel, getUserEditorChanges, getUserEditorValidationErrors } from '../../shared/userManagement'

type UserEditorState = { mode: 'create' | 'edit'; user?: ManagedUserDto }
type UserSaveConfirmationState = { user: ManagedUserDto; changes: UserEditChange[]; request: UpdateManagedUserRequest }
type RolePermissionEditorState = { role: ManagedRoleDto; permissions: string[] }
type RolePermissionConfirmationState = { role: ManagedRoleDto; changes: ChangePreview[]; permissions: string[] }

export function UserManagementPanel({ auth, userClient }: { auth: AuthResponse; userClient: UserManagementClient }) {
  const [roles, setRoles] = useState<ManagedRoleDto[]>([])
  const [page, setPage] = useState<PagedManagedUsersDto>(() => createEmptyPage<ManagedUserDto>())
  const [searchDraft, setSearchDraft] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [offset, setOffset] = useState(0)
  const [pageSize, setPageSize] = useState(25)
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  const [toast, setToast] = useState<{ id: number; text: string; kind: 'success' | 'error' } | null>(null)
  const [contextMenu, setContextMenu] = useState<{ user: ManagedUserDto; x: number; y: number } | null>(null)
  const [editor, setEditor] = useState<UserEditorState | null>(null)
  const [saveConfirmation, setSaveConfirmation] = useState<UserSaveConfirmationState | null>(null)
  const [roleEditor, setRoleEditor] = useState<RolePermissionEditorState | null>(null)
  const [roleConfirmation, setRoleConfirmation] = useState<RolePermissionConfirmationState | null>(null)
  const [rolePermissionError, setRolePermissionError] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ManagedUserDto | null>(null)
  const [restoreTarget, setRestoreTarget] = useState<ManagedUserDto | null>(null)
  const [deleteReason, setDeleteReason] = useState('')
  const [deleteReasonError, setDeleteReasonError] = useState<string | null>(null)
  const [form, setForm] = useState<UserFormState>({ email: '', displayName: '', password: '', passwordConfirmation: '', roleCode: 'operator', isActive: true, deactivationReason: '' })
  const rolesRequestRef = useRef<{ accessToken: string; client: UserManagementClient; promise: Promise<ManagedRoleDto[]> } | null>(null)
  useRestoreFocusOnClose(Boolean(editor))
  const editorCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(editor))
  const editorDialogRef = useFocusTrap<HTMLElement>(Boolean(editor) && !saveConfirmation)
  useRestoreFocusOnClose(Boolean(saveConfirmation))
  const saveConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(saveConfirmation))
  const saveConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(saveConfirmation))
  useRestoreFocusOnClose(Boolean(roleEditor))
  const roleEditorCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(roleEditor))
  const roleEditorDialogRef = useFocusTrap<HTMLElement>(Boolean(roleEditor) && !roleConfirmation)
  useRestoreFocusOnClose(Boolean(roleConfirmation))
  const roleConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(roleConfirmation))
  const roleConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(roleConfirmation))
  useRestoreFocusOnClose(Boolean(deleteTarget))
  const deleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(deleteTarget))
  const deleteDialogRef = useFocusTrap<HTMLElement>(Boolean(deleteTarget))
  useRestoreFocusOnClose(Boolean(restoreTarget))
  const restoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(restoreTarget))
  const restoreDialogRef = useFocusTrap<HTMLElement>(Boolean(restoreTarget))

  useEscapeKey(Boolean(contextMenu), () => setContextMenu(null))
  useEscapeKey(Boolean(editor) && !saveConfirmation, () => closeEditor())
  useEscapeKey(Boolean(saveConfirmation), () => setSaveConfirmation(null))
  useEscapeKey(Boolean(roleEditor) && !roleConfirmation, () => closeRoleEditor())
  useEscapeKey(Boolean(roleConfirmation), () => setRoleConfirmation(null))
  useEscapeKey(Boolean(deleteTarget), () => closeDeleteDialog())
  useEscapeKey(Boolean(restoreTarget), () => setRestoreTarget(null))

  useEffect(() => {
    if (!toast) {
      return undefined
    }

    const timeoutId = window.setTimeout(() => setToast(null), 3200)
    return () => window.clearTimeout(timeoutId)
  }, [toast])

  function showToast(text: string, kind: 'success' | 'error' = 'success') {
    setToast({ id: Date.now(), text, kind })
  }

  const getRolesOnce = useCallback(() => {
    const cached = rolesRequestRef.current
    if (cached?.accessToken === auth.accessToken && cached.client === userClient) {
      return cached.promise
    }

    const request = userClient.getRoles(auth.accessToken)
    rolesRequestRef.current = { accessToken: auth.accessToken, client: userClient, promise: request }
    void request.catch(() => {
      if (rolesRequestRef.current?.promise === request) {
        rolesRequestRef.current = null
      }
    })
    return request
  }, [auth.accessToken, userClient])

  async function refreshUsers() {
    setLoading(true)
    setError(null)
    try {
      const loadedPage = await userClient.getUsersPage(auth.accessToken, appliedSearch, offset, pageSize)
      setPage(loadedPage)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось загрузить пользователей.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    let ignore = false

    async function loadUsers() {
      setLoading(true)
      setError(null)
      let pageFailed = false
      try {
        const loadedPage = await userClient.getUsersPage(auth.accessToken, appliedSearch, offset, pageSize)
        if (!ignore) {
          setPage(loadedPage)
        }
      } catch (caught) {
        if (!ignore) {
          pageFailed = true
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить пользователей.')
        }
      } finally {
        if (!ignore) {
          setLoading(false)
        }
      }

      if (ignore) {
        return
      }

      try {
        const loadedRoles = await getRolesOnce()
        if (!ignore) {
          setRoles(loadedRoles)
          setForm((value) => ({ ...value, roleCode: loadedRoles.find((role) => role.code === value.roleCode)?.code ?? loadedRoles[0]?.code ?? '' }))
        }
      } catch (caught) {
        if (!ignore && !pageFailed) {
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить роли пользователей.')
        }
      }
    }

    void loadUsers()
    return () => {
      ignore = true
    }
  }, [appliedSearch, auth.accessToken, getRolesOnce, offset, pageSize, userClient])

  function openEditor(mode: 'create' | 'edit', user?: ManagedUserDto) {
    setContextMenu(null)
    setValidationErrors([])
    setError(null)
    setEditor({ mode, user })
    setForm({
      email: user?.email ?? '',
      displayName: user?.displayName ?? '',
      password: '',
      passwordConfirmation: '',
      roleCode: getPrimaryRoleCode(user, roles),
      isActive: user?.isActive ?? true,
      deactivationReason: '',
    })
  }

  function closeEditor() {
    setEditor(null)
    setSaveConfirmation(null)
    setValidationErrors([])
  }

  async function saveUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!editor) {
      return
    }

    const errors = getUserEditorValidationErrors(form, editor.mode, editor.user)
    if (errors.length > 0) {
      setValidationErrors(errors)
      return
    }

    setValidationErrors([])

    if (editor.mode === 'edit' && editor.user) {
      const request: UpdateManagedUserRequest = {
        displayName: form.displayName.trim(),
        roleCodes: [form.roleCode],
        isActive: form.isActive,
        newPassword: form.password.length > 0 ? form.password : null,
        deactivationReason: editor.user.isActive && !form.isActive ? form.deactivationReason.trim() : null,
      }
      const changes = getUserEditorChanges(form, editor.user, roles)
      if (changes.length === 0) {
        closeEditor()
        return
      }

      setSaveConfirmation({ user: editor.user, changes, request })
      return
    }

    setSaving(editor.mode)
    setError(null)
    try {
      if (editor.mode === 'create') {
        const request: CreateManagedUserRequest = {
          email: form.email,
          displayName: form.displayName,
          password: form.password,
          roleCodes: [form.roleCode],
          isActive: form.isActive,
        }
        await userClient.createUser(auth.accessToken, request)
        setOffset(0)
      }

      closeEditor()
      await refreshUsers()
      showToast('Пользователь добавлен.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить пользователя.'
      setError(message)
      showToast(message, 'error')
    } finally {
      setSaving(null)
    }
  }

  async function confirmSaveUser() {
    if (!saveConfirmation) {
      return
    }

    setSaving('edit')
    setError(null)
    try {
      await userClient.updateUser(auth.accessToken, saveConfirmation.user.id, saveConfirmation.request)
      setSaveConfirmation(null)
      closeEditor()
      await refreshUsers()
      showToast('Пользователь изменен.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить пользователя.'
      setError(message)
      showToast(message, 'error')
    } finally {
      setSaving(null)
    }
  }

  async function deleteUser() {
    if (!deleteTarget) {
      return
    }

    const reason = deleteReason.trim()
    if (!reason) {
      setDeleteReasonError('Укажите причину отключения пользователя.')
      return
    }

    setSaving('delete')
    setError(null)
    setDeleteReasonError(null)
    try {
      await userClient.updateUser(auth.accessToken, deleteTarget.id, {
        displayName: deleteTarget.displayName,
        roleCodes: [getPrimaryRoleCode(deleteTarget, roles)],
        isActive: false,
        newPassword: null,
        deactivationReason: reason,
      })
      closeDeleteDialog()
      await refreshUsers()
      showToast('Пользователь отключен.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось отключить пользователя.'
      setError(message)
      showToast(message, 'error')
    } finally {
      setSaving(null)
    }
  }

  async function restoreUser() {
    if (!restoreTarget) {
      return
    }

    setSaving('restore')
    setError(null)
    try {
      await userClient.restoreUser(auth.accessToken, restoreTarget.id)
      setRestoreTarget(null)
      await refreshUsers()
      showToast('Пользователь восстановлен.')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось восстановить пользователя.')
    } finally {
      setSaving(null)
    }
  }

  function openRoleEditor(role: ManagedRoleDto) {
    setRolePermissionError(null)
    setRoleEditor({ role, permissions: [...role.permissions] })
  }

  function closeRoleEditor() {
    setRoleEditor(null)
    setRoleConfirmation(null)
    setRolePermissionError(null)
  }

  function toggleRolePermission(permission: string, checked: boolean) {
    setRolePermissionError(null)
    setRoleEditor((current) => {
      if (!current) {
        return current
      }

      const permissionsSet = new Set(current.permissions)
      if (checked) {
        permissionsSet.add(permission)
      } else {
        permissionsSet.delete(permission)
      }

      return { ...current, permissions: [...permissionsSet].sort() }
    })
  }

  function saveRolePermissions(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!roleEditor) {
      return
    }

    if (roleEditor.permissions.length === 0) {
      setRolePermissionError('Выберите хотя бы одно право для роли.')
      return
    }

    const before = formatRolePermissionLabels(roleEditor.role.permissions)
    const after = formatRolePermissionLabels(roleEditor.permissions)
    if (before === after) {
      closeRoleEditor()
      return
    }

    setRoleConfirmation({
      role: roleEditor.role,
      permissions: roleEditor.permissions,
      changes: [{ field: 'Права', before, after }],
    })
  }

  async function confirmRolePermissions() {
    if (!roleConfirmation) {
      return
    }

    setSaving('role')
    setError(null)
    try {
      const updatedRole = await userClient.updateRolePermissions(auth.accessToken, roleConfirmation.role.code, { permissions: roleConfirmation.permissions })
      setRoles((current) => current.map((role) => (role.code === updatedRole.code ? updatedRole : role)))
      setRoleConfirmation(null)
      setRoleEditor(null)
      await refreshUsers()
      showToast('Права роли изменены.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить права роли.'
      setError(message)
      showToast(message, 'error')
    } finally {
      setSaving(null)
    }
  }

  function openDeleteDialog(user: ManagedUserDto) {
    setDeleteReason('')
    setDeleteReasonError(null)
    setDeleteTarget(user)
  }

  function closeDeleteDialog() {
    setDeleteTarget(null)
    setDeleteReason('')
    setDeleteReasonError(null)
  }

  function submitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setAppliedSearch(searchDraft.trim())
    setOffset(0)
  }


  return (
    <section className="dictionary-panel-v2 users-panel-v2" aria-label="Пользователи" onClick={() => setContextMenu(null)}>
      <div className="section-heading">
        <div>
          <p className="eyebrow">Пользователи</p>
          <h2>Доступ в систему и роли сотрудников</h2>
        </div>
        {!loading ? <span>{page.totalCount} пользователей</span> : null}
      </div>

      {error ? <FormError>{error}</FormError> : null}

      <div className="users-workbench">
        <div className="dictionary-table-shell">
          <form className="dictionary-toolbar" onSubmit={submitSearch}>
            <input aria-label="Поиск пользователей" placeholder="Email, имя или роль" value={searchDraft} onChange={(event) => setSearchDraft(event.target.value)} />
            <button className="ghost-button" type="submit" disabled={loading}>
              <Search size={16} />
              <span>Найти</span>
            </button>
          </form>

          <div className="dictionary-toolbar users-toolbar-actions">
            <span className="form-hint">Действия по строкам доступны через ПКМ.</span>
            <button className="secondary-button create-action-button" type="button" onClick={() => openEditor('create')} disabled={roles.length === 0}>
              <UserPlus size={16} aria-hidden="true" />
              <span>Добавить</span>
            </button>
          </div>

          <div className="dictionary-table-scroll">
            <table className="dictionary-data-table users-data-table" aria-label="Список пользователей" onContextMenu={(event) => event.preventDefault()}>
              <thead>
                <tr>
                  <th>Сотрудник</th>
                  <th>Email</th>
                  <th>Роль</th>
                  <th>Статус</th>
                  <th>Последний вход</th>
                </tr>
              </thead>
              <tbody>
                {!loading ? page.items.map((managedUser) => (
                  <tr
                    key={managedUser.id}
                    tabIndex={0}
                    onContextMenu={(event) => {
                      event.preventDefault()
                      event.stopPropagation()
                      setContextMenu({ user: managedUser, x: event.clientX, y: event.clientY })
                    }}
                  >
                    <td><strong>{managedUser.displayName}</strong></td>
                    <td>{managedUser.email}</td>
                    <td>{managedUser.roles.map((role) => getRoleLabel(role, roles)).join(', ')}</td>
                    <td><span className={managedUser.isActive ? 'status-active' : 'status-disabled'}>{managedUser.isActive ? 'Активен' : 'Отключен'}</span></td>
                    <td>{managedUser.lastLoginAtUtc ? formatDateTime(managedUser.lastLoginAtUtc) : 'Не входил'}</td>
                  </tr>
                )) : null}
                {!loading && page.items.length === 0 ? (
                  <tr>
                    <td colSpan={5}>
                      <p className="empty-state" role="status" aria-live="polite">Пользователей пока нет</p>
                    </td>
                  </tr>
                ) : null}
              </tbody>
            </table>
            {loading ? <TableLoadingState label="Загружаем пользователей" /> : null}
          </div>

          <TablePagination
            ariaLabel="Пагинация пользователей"
            totalCount={page.totalCount}
            offset={offset}
            limit={pageSize}
            visibleCount={page.items.length}
            disabled={loading}
            pageSizeLabel="Количество строк пользователей"
            onPageChange={(pageNumber) => setOffset((pageNumber - 1) * pageSize)}
            onPageSizeChange={(limit) => { setPageSize(limit); setOffset(0) }}
          />
        </div>
      </div>

      <RolePermissionMatrix roles={roles} onEditRole={openRoleEditor} />

      {contextMenu ? (
        <div className="context-menu" role="menu" style={{ left: contextMenu.x, top: contextMenu.y }} onClick={(event) => event.stopPropagation()}>
          <div className="context-menu-group" role="group">
            <button type="button" role="menuitem" onClick={() => openEditor('create')}>
              <UserPlus size={15} aria-hidden="true" />
              <span>Добавить</span>
            </button>
          </div>
          <div className="context-menu-separator" role="separator" />
          <div className="context-menu-group" role="group">
            <button type="button" role="menuitem" onClick={() => openEditor('edit', contextMenu.user)}>
              <Save size={15} />
              <span>Изменить</span>
            </button>
            <button className="context-menu-danger" type="button" role="menuitem" onClick={() => { openDeleteDialog(contextMenu.user); setContextMenu(null) }} disabled={!contextMenu.user.isActive}>
              <Trash2 size={15} />
              <span>Удалить</span>
            </button>
            <button type="button" role="menuitem" onClick={() => { setRestoreTarget(contextMenu.user); setContextMenu(null) }} disabled={contextMenu.user.isActive}>
              <RotateCcw size={15} />
              <span>Вернуть</span>
            </button>
          </div>
        </div>
      ) : null}

      {editor ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeEditor}>
          <section ref={editorDialogRef} className="detail-dialog dictionary-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="user-editor-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <h3 id="user-editor-title">{editor.mode === 'create' ? 'Новый пользователь' : 'Изменить пользователя'}</h3>
                <p>{editor.mode === 'create' ? 'Создайте сотрудника и назначьте роль.' : 'Измените имя, роль, статус или задайте новый пароль.'}</p>
              </div>
              <button ref={editorCloseRef} className="icon-button" type="button" onClick={closeEditor} aria-label="Закрыть окно пользователя">
                <X size={18} />
              </button>
            </div>
            <form className="dictionary-modal-form" autoComplete="off" onSubmit={saveUser}>
              {editor.mode === 'create' ? (
                <FormField label="Email">
                  <input aria-label="Email пользователя" autoComplete="off" data-1p-ignore data-lpignore="true" name="managed-user-email" placeholder="email@example.com" value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} type="email" required />
                </FormField>
              ) : (
                <FormField label="Email">
                  <input aria-label="Email пользователя" autoComplete="off" name="managed-user-email-readonly" value={form.email} disabled />
                </FormField>
              )}
              <FormField label="Имя сотрудника">
                <input aria-label="Имя пользователя" autoComplete="off" name="managed-user-display-name" placeholder="ФИО или рабочее имя" value={form.displayName} onChange={(event) => setForm({ ...form, displayName: event.target.value })} required />
              </FormField>
              <FormField label="Роль">
                <select aria-label="Роль пользователя" value={form.roleCode} onChange={(event) => setForm({ ...form, roleCode: event.target.value })} required>
                  {roles.map((role) => (
                    <option value={role.code} key={role.code}>{role.name}</option>
                  ))}
                </select>
              </FormField>
              <FormField label="Статус">
                <select aria-label="Статус пользователя" value={form.isActive ? 'active' : 'inactive'} onChange={(event) => setForm({ ...form, isActive: event.target.value === 'active' })}>
                  <option value="active">Активен</option>
                  <option value="inactive">Отключен</option>
                </select>
              </FormField>
              {editor.user?.isActive && !form.isActive ? (
                <FormField label="Причина отключения">
                  <textarea
                    aria-label="Причина отключения пользователя"
                    placeholder="Например: сотрудник больше не работает или доступ выдан ошибочно"
                    maxLength={1000}
                    value={form.deactivationReason}
                    onChange={(event) => setForm({ ...form, deactivationReason: event.target.value })}
                    required
                  />
                </FormField>
              ) : null}
              <FormField label={editor.mode === 'create' ? 'Пароль' : 'Новый пароль'}>
                <input
                  aria-label="Пароль пользователя"
                  aria-describedby="new-user-password-policy-hint"
                  autoComplete="new-password"
                  data-1p-ignore
                  data-lpignore="true"
                  name="managed-user-new-password"
                  placeholder={editor.mode === 'create' ? 'Пароль' : 'Оставьте пустым, если менять не нужно'}
                  value={form.password}
                  onChange={(event) => setForm({ ...form, password: event.target.value })}
                  type="password"
                  minLength={editor.mode === 'create' ? 8 : undefined}
                  required={editor.mode === 'create'}
                />
              </FormField>
              <FormField label={editor.mode === 'create' ? 'Повторите пароль' : 'Повторите новый пароль'}>
                <input
                  aria-label="Подтверждение пароля пользователя"
                  aria-describedby="new-user-password-policy-hint"
                  autoComplete="new-password"
                  data-1p-ignore
                  data-lpignore="true"
                  name="managed-user-new-password-confirmation"
                  placeholder={editor.mode === 'create' ? 'Повторите пароль' : 'Повторите новый пароль'}
                  value={form.passwordConfirmation}
                  onChange={(event) => setForm({ ...form, passwordConfirmation: event.target.value })}
                  type="password"
                  minLength={editor.mode === 'create' ? 8 : undefined}
                  required={editor.mode === 'create'}
                />
              </FormField>
              <p className="form-hint" id="new-user-password-policy-hint">Минимум 8 символов.</p>
              <FormValidationSummary title={editor.mode === 'create' ? 'Проверьте нового пользователя' : 'Проверьте пользователя'} items={validationErrors} />
              <div className="detail-dialog-actions">
                <button className="secondary-button" type="submit" disabled={saving !== null || roles.length === 0}>
                  <Save size={16} />
                  <span>Сохранить</span>
                </button>
                <button className="ghost-button" type="button" onClick={closeEditor}>Отмена</button>
              </div>
            </form>
          </section>
        </div>
      ) : null}

      {saveConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setSaveConfirmation(null)}>
          <section ref={saveConfirmationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="user-save-confirmation-title" aria-describedby="user-save-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Изменение</p>
                <h3 id="user-save-confirmation-title">Подтвердите изменения пользователя</h3>
                <p>{saveConfirmation.user.displayName}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить подтверждение изменений пользователя" onClick={() => setSaveConfirmation(null)} disabled={saving === 'edit'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="user-save-confirmation-description">Проверьте, что именно изменится. После подтверждения действие будет записано в историю изменений.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля пользователя">
              {saveConfirmation.changes.map((change) => (
                <li key={`${change.field}-${change.before}-${change.after}`}>
                  <span className="dictionary-change-field">{change.field}</span>
                  <span className="dictionary-change-values">
                    <span className="dictionary-change-value">{change.before}</span>
                    <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                    <span className="dictionary-change-value dictionary-change-value-after">{change.after}</span>
                  </span>
                </li>
              ))}
            </ul>
            <div className="detail-dialog-actions">
              <button ref={saveConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setSaveConfirmation(null)} disabled={saving === 'edit'}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmSaveUser()} disabled={saving === 'edit'}>
                <Save size={16} />
                <span>{saving === 'edit' ? 'Сохраняем...' : 'Сохранить изменения'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {roleEditor ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={closeRoleEditor}>
          <section ref={roleEditorDialogRef} className="detail-dialog dictionary-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="role-permissions-title" aria-describedby="role-permissions-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Роль</p>
                <h3 id="role-permissions-title">Изменить права роли</h3>
                <p id="role-permissions-description">{roleEditor.role.name}</p>
              </div>
              <button ref={roleEditorCloseRef} className="icon-button" type="button" onClick={closeRoleEditor} aria-label="Закрыть изменение прав роли" disabled={saving === 'role'}>
                <X size={18} />
              </button>
            </div>
            <form className="dictionary-modal-form" onSubmit={saveRolePermissions}>
              <div className="role-permission-editor" role="group" aria-label={`Права роли ${roleEditor.role.name}`}>
                {rolePermissionGroups.map((group) => {
                  const administratorUsersManage = roleEditor.role.code === 'administrator' && group.permission === permissions.usersManage
                  return (
                    <label className="contractors-check-row" key={group.permission}>
                      <input
                        type="checkbox"
                        aria-label={`${roleEditor.role.name}: ${group.label}`}
                        checked={roleEditor.permissions.includes(group.permission)}
                        disabled={saving === 'role' || administratorUsersManage}
                        onChange={(event) => toggleRolePermission(group.permission, event.target.checked)}
                      />
                      <span>{group.label}</span>
                    </label>
                  )
                })}
              </div>
              {rolePermissionError ? <p className="form-error" role="alert">{rolePermissionError}</p> : null}
              <p className="form-hint">Права применяются к пользователям с этой ролью после обновления их сессии. Изменение будет записано в историю.</p>
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={closeRoleEditor} disabled={saving === 'role'}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={saving === 'role'}>
                  <Save size={16} />
                  <span>Сохранить</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}

      {roleConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setRoleConfirmation(null)}>
          <section ref={roleConfirmationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="role-permissions-confirmation-title" aria-describedby="role-permissions-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Права</p>
                <h3 id="role-permissions-confirmation-title">Подтвердите изменение прав роли</h3>
                <p>{roleConfirmation.role.name}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить подтверждение изменения прав роли" onClick={() => setRoleConfirmation(null)} disabled={saving === 'role'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="role-permissions-confirmation-description">Проверьте набор доступов. После подтверждения изменение будет записано в историю изменений.</p>
            <ul className="dictionary-change-list" aria-label="Изменяемые поля роли">
              {roleConfirmation.changes.map((change) => (
                <li key={`${change.field}-${change.before}-${change.after}`}>
                  <span className="dictionary-change-field">{change.field}</span>
                  <span className="dictionary-change-values">
                    <span className="dictionary-change-value">{change.before}</span>
                    <span className="dictionary-change-arrow" aria-hidden="true">-&gt;</span>
                    <span className="dictionary-change-value dictionary-change-value-after">{change.after}</span>
                  </span>
                </li>
              ))}
            </ul>
            <div className="detail-dialog-actions">
              <button ref={roleConfirmationCancelRef} className="ghost-button" type="button" onClick={() => setRoleConfirmation(null)} disabled={saving === 'role'}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void confirmRolePermissions()} disabled={saving === 'role'}>
                <ShieldCheck size={16} />
                <span>{saving === 'role' ? 'Сохраняем...' : 'Сохранить права'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {deleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => closeDeleteDialog()}>
          <section ref={deleteDialogRef} className="detail-dialog dictionary-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="user-delete-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <h3 id="user-delete-title">Удалить пользователя</h3>
                <p>{deleteTarget.displayName} будет отключен и не сможет входить в систему. История изменений сохранится.</p>
              </div>
              <button className="icon-button" type="button" onClick={() => closeDeleteDialog()} aria-label="Закрыть подтверждение удаления">
                <X size={18} />
              </button>
            </div>
            <label className="field-label" htmlFor="user-delete-reason">Причина отключения</label>
            <textarea
              id="user-delete-reason"
              aria-label="Причина отключения пользователя"
              aria-invalid={Boolean(deleteReasonError)}
              aria-describedby={deleteReasonError ? 'user-delete-reason-error' : undefined}
              maxLength={1000}
              value={deleteReason}
              onChange={(event) => {
                setDeleteReason(event.target.value)
                if (deleteReasonError && event.target.value.trim()) {
                  setDeleteReasonError(null)
                }
              }}
              placeholder="Например: сотрудник больше не работает или доступ выдан ошибочно"
              disabled={saving === 'delete'}
              required
            />
            {deleteReasonError ? <p className="form-error" id="user-delete-reason-error">{deleteReasonError}</p> : null}
            <div className="detail-dialog-actions">
              <button ref={deleteCancelRef} className="ghost-button" type="button" onClick={() => closeDeleteDialog()}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={deleteUser} disabled={saving === 'delete' || !deleteReason.trim()}>
                <Trash2 size={16} />
                <span>Удалить</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {restoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={() => setRestoreTarget(null)}>
          <section ref={restoreDialogRef} className="detail-dialog dictionary-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="user-restore-title" aria-describedby="user-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <h3 id="user-restore-title">Вернуть пользователя?</h3>
                <p>{restoreTarget.displayName} снова сможет входить в систему с прежними ролями.</p>
              </div>
              <button className="icon-button" type="button" onClick={() => setRestoreTarget(null)} aria-label="Отменить восстановление пользователя" disabled={saving === 'restore'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="user-restore-description">Действие будет записано в историю изменений.</p>
            <div className="detail-dialog-actions">
              <button ref={restoreCancelRef} className="ghost-button" type="button" onClick={() => setRestoreTarget(null)} disabled={saving === 'restore'}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void restoreUser()} disabled={saving === 'restore'}>
                <RotateCcw size={16} />
                <span>{saving === 'restore' ? 'Возвращаем...' : 'Вернуть'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {toast ? <div className={`toast-message ${toast.kind === 'error' ? 'toast-message--error' : ''}`} role="status" aria-live="polite">{toast.text}</div> : null}
    </section>
  )
}

function getRolePermissionLabel(permission: string) {
  return rolePermissionGroups.find((group) => group.permission === permission)?.label ?? permission
}

function formatRolePermissionLabels(permissionsList: readonly string[]) {
  const knownLabels = rolePermissionGroups
    .filter((group) => permissionsList.includes(group.permission))
    .map((group) => group.label)
  const customLabels = permissionsList
    .filter((permission) => !rolePermissionGroups.some((group) => group.permission === permission))
    .sort()
    .map(getRolePermissionLabel)
  const labels = [...knownLabels, ...customLabels]
  return labels.length > 0 ? labels.join(', ') : 'Нет прав'
}

function RolePermissionMatrix({ roles, onEditRole }: { roles: ManagedRoleDto[]; onEditRole(role: ManagedRoleDto): void }) {
  return (
    <section className="role-matrix" aria-label="Матрица ролей">
      <div className="section-heading compact-heading">
        <div>
          <p className="eyebrow">Роли и права</p>
          <h3>Матрица доступов</h3>
        </div>
        <span>{roles.length} ролей</span>
      </div>

      <div className="role-matrix-table" role="table" aria-label="Матрица ролей и прав">
        <div className="role-matrix-row header" role="row">
          <span role="columnheader">Роль</span>
          {rolePermissionGroups.map((group) => (
            <span role="columnheader" key={group.permission}>{group.label}</span>
          ))}
          <span role="columnheader">Действия</span>
        </div>
        {roles.length === 0 ? <p className="empty-state" role="status" aria-live="polite">Роли пока не загружены</p> : null}
        {roles.map((role) => (
          <div className="role-matrix-row" role="row" key={role.code}>
            <span role="cell">
              <strong>{role.name}</strong>
              <small>{role.code}</small>
            </span>
            {rolePermissionGroups.map((group) => {
              const allowed = role.permissions.includes(group.permission)
              return (
                <span role="cell" aria-label={`${role.name}: ${group.label} - ${allowed ? 'разрешено' : 'нет доступа'}`} className={allowed ? 'status-active' : 'status-disabled'} key={group.permission}>
                  {allowed ? 'Да' : 'Нет'}
                </span>
              )
            })}
            <span role="cell">
              <button className="icon-button" type="button" aria-label={`Изменить права роли ${role.name}`} title={`Изменить права роли ${role.name}`} onClick={() => onEditRole(role)}>
                <ShieldCheck size={16} />
              </button>
            </span>
          </div>
        ))}
      </div>
    </section>
  )
}
