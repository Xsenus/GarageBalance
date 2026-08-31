import { useCallback, useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Pencil, RotateCcw, Save, Search, ShieldCheck, Trash2, UserPlus, X } from 'lucide-react'
import type { AuthResponse } from '../../services/authApi'
import type { CreateManagedUserRequest, ManagedRoleDto, ManagedUserDto, PagedManagedUsersDto, UpdateManagedUserRequest, UserManagementClient } from '../../services/usersApi'
import { expandPermissionDependencies, isPermissionRequiredBySelection, permissions, rolePermissionGroups } from '../../shared/accessControl'
import { AsyncErrorState, BackgroundRefreshStatus, EmptyState, StatusMessage, TableLoadingState } from '../../shared/AsyncState'
import { FormError, FormValidationSummary } from '../../shared/formFeedback'
import { FormField } from '../../shared/FormField'
import { formatDateTime } from '../../shared/formatters'
import { useEscapeKey, useFocusOnOpen, useFocusTrap, useRestoreFocusOnClose } from '../../shared/focusHooks'
import { createEmptyPage } from '../../shared/pagination'
import { SelectControl } from '../../shared/SelectControl'
import { TablePagination } from '../../shared/TablePagination'
import { ToastViewport } from '../../shared/Toast'
import { useToast } from '../../shared/useToast'
import type { UserFormState } from '../../shared/userManagement'
import { getInitialRoleCodes, getRoleLabel, getUserEditorChanges, getUserEditorValidationErrors } from '../../shared/userManagement'
import { useActionCommentSettings } from '../../shared/ActionCommentSettings'

type UserEditorState = { mode: 'create' | 'edit'; user?: ManagedUserDto }
type UserDeactivationConfirmationState = { user: ManagedUserDto; request: UpdateManagedUserRequest }
type RolePermissionEditorState = { role: ManagedRoleDto; permissions: string[] }

export function UserManagementPanel({ auth, userClient }: { auth: AuthResponse; userClient: UserManagementClient }) {
  const [actionCommentsRequired] = useActionCommentSettings()
  const [roles, setRoles] = useState<ManagedRoleDto[]>([])
  const [page, setPage] = useState<PagedManagedUsersDto>(() => createEmptyPage<ManagedUserDto>())
  const [searchDraft, setSearchDraft] = useState('')
  const [appliedSearch, setAppliedSearch] = useState('')
  const [offset, setOffset] = useState(0)
  const [pageSize, setPageSize] = useState(25)
  const [loading, setLoading] = useState(true)
  const [hasLoadedPage, setHasLoadedPage] = useState(false)
  const [saving, setSaving] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [validationErrors, setValidationErrors] = useState<string[]>([])
  const { toast, showToast, dismissToast } = useToast(3200)
  const [contextMenu, setContextMenu] = useState<{ user: ManagedUserDto; x: number; y: number } | null>(null)
  const [editor, setEditor] = useState<UserEditorState | null>(null)
  const [deactivationConfirmation, setDeactivationConfirmation] = useState<UserDeactivationConfirmationState | null>(null)
  const [roleEditor, setRoleEditor] = useState<RolePermissionEditorState | null>(null)
  const [rolePermissionError, setRolePermissionError] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<ManagedUserDto | null>(null)
  const [restoreTarget, setRestoreTarget] = useState<ManagedUserDto | null>(null)
  const [deleteReason, setDeleteReason] = useState('')
  const [deleteReasonError, setDeleteReasonError] = useState<string | null>(null)
  const [form, setForm] = useState<UserFormState>({ email: '', displayName: '', password: '', passwordConfirmation: '', roleCodes: ['operator'], isActive: true, deactivationReason: '' })
  const rolesRequestRef = useRef<{ accessToken: string; client: UserManagementClient; controller: AbortController; promise: Promise<ManagedRoleDto[]> } | null>(null)
  const usersPageControllerRef = useRef<AbortController | null>(null)
  const busy = saving !== null
  useRestoreFocusOnClose(Boolean(editor))
  const editorCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(editor))
  const editorDialogRef = useFocusTrap<HTMLElement>(Boolean(editor) && !deactivationConfirmation)
  useRestoreFocusOnClose(Boolean(deactivationConfirmation))
  const deactivationConfirmationCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(deactivationConfirmation))
  const deactivationConfirmationDialogRef = useFocusTrap<HTMLElement>(Boolean(deactivationConfirmation))
  useRestoreFocusOnClose(Boolean(roleEditor))
  const roleEditorCloseRef = useFocusOnOpen<HTMLButtonElement>(Boolean(roleEditor))
  const roleEditorDialogRef = useFocusTrap<HTMLElement>(Boolean(roleEditor))
  useRestoreFocusOnClose(Boolean(deleteTarget))
  const deleteCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(deleteTarget))
  const deleteDialogRef = useFocusTrap<HTMLElement>(Boolean(deleteTarget))
  useRestoreFocusOnClose(Boolean(restoreTarget))
  const restoreCancelRef = useFocusOnOpen<HTMLButtonElement>(Boolean(restoreTarget))
  const restoreDialogRef = useFocusTrap<HTMLElement>(Boolean(restoreTarget))

  useEscapeKey(Boolean(contextMenu), () => setContextMenu(null))
  useEscapeKey(Boolean(editor) && !deactivationConfirmation && !busy, () => closeEditor())
  useEscapeKey(Boolean(deactivationConfirmation) && saving !== 'edit', () => closeDeactivationConfirmation())
  useEscapeKey(Boolean(roleEditor) && saving !== 'role', () => closeRoleEditor())
  useEscapeKey(Boolean(deleteTarget) && saving !== 'delete', () => closeDeleteDialog())
  useEscapeKey(Boolean(restoreTarget) && saving !== 'restore', () => closeRestoreDialog())

  useEffect(() => () => {
    rolesRequestRef.current?.controller.abort()
    usersPageControllerRef.current?.abort()
    usersPageControllerRef.current = null
  }, [])

  const getRolesOnce = useCallback(() => {
    const cached = rolesRequestRef.current
    if (cached?.accessToken === auth.accessToken && cached.client === userClient) {
      return cached.promise
    }

    cached?.controller.abort()
    const controller = new AbortController()
    const request = userClient.getRoles(auth.accessToken, controller.signal)
    rolesRequestRef.current = { accessToken: auth.accessToken, client: userClient, controller, promise: request }
    void request.catch(() => {
      if (rolesRequestRef.current?.promise === request) {
        rolesRequestRef.current = null
      }
    })
    return request
  }, [auth.accessToken, userClient])

  const beginUsersPageRequest = useCallback((requestedOffset: number) => {
    usersPageControllerRef.current?.abort()
    const controller = new AbortController()
    usersPageControllerRef.current = controller
    return {
      controller,
      promise: userClient.getUsersPage(auth.accessToken, appliedSearch, requestedOffset, pageSize, controller.signal),
    }
  }, [appliedSearch, auth.accessToken, pageSize, userClient])

  async function refreshUsers(requestedOffset = offset, background = false) {
    const { controller, promise } = beginUsersPageRequest(requestedOffset)
    if (!background) {
      setLoading(true)
    }
    setError(null)
    try {
      const loadedPage = await promise
      if (usersPageControllerRef.current === controller && !controller.signal.aborted) {
        setPage(loadedPage)
        setHasLoadedPage(true)
      }
    } catch (caught) {
      if (usersPageControllerRef.current === controller && !controller.signal.aborted) {
        setError(caught instanceof Error ? caught.message : 'Не удалось загрузить пользователей.')
      }
    } finally {
      if (usersPageControllerRef.current === controller) {
        usersPageControllerRef.current = null
        if (!controller.signal.aborted && !background) {
          setLoading(false)
        }
      }
    }
  }

  function refreshUsersAfterMutation(requestedOffset = offset) {
    void refreshUsers(requestedOffset, true)
  }

  useEffect(() => {
    let ignore = false
    const { controller, promise } = beginUsersPageRequest(offset)

    async function loadUsers() {
      setLoading(true)
      setError(null)
      let pageFailed = false
      try {
        const loadedPage = await promise
        if (!ignore && usersPageControllerRef.current === controller) {
          setPage(loadedPage)
          setHasLoadedPage(true)
        }
      } catch (caught) {
        if (!ignore && usersPageControllerRef.current === controller && !controller.signal.aborted) {
          pageFailed = true
          setError(caught instanceof Error ? caught.message : 'Не удалось загрузить пользователей.')
        }
      } finally {
        if (!ignore && usersPageControllerRef.current === controller && !controller.signal.aborted) {
          setLoading(false)
        }
      }

      if (ignore || controller.signal.aborted || usersPageControllerRef.current !== controller) {
        return
      }

      try {
        const loadedRoles = await getRolesOnce()
        if (!ignore) {
          setRoles(loadedRoles)
          setForm((value) => {
            const availableRoleCodes = value.roleCodes.filter((roleCode) => loadedRoles.some((role) => role.code === roleCode))
            return { ...value, roleCodes: availableRoleCodes.length > 0 ? availableRoleCodes : getInitialRoleCodes(undefined, loadedRoles) }
          })
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
      if (usersPageControllerRef.current === controller) {
        controller.abort()
        usersPageControllerRef.current = null
      }
    }
  }, [beginUsersPageRequest, getRolesOnce, offset])

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
      roleCodes: getInitialRoleCodes(user, roles),
      isActive: user?.isActive ?? true,
      deactivationReason: '',
    })
  }

  function closeEditor() {
    setEditor(null)
    setDeactivationConfirmation(null)
    setValidationErrors([])
    setError(null)
  }

  async function saveUser(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!editor) {
      return
    }

    const errors = getUserEditorValidationErrors(form, editor.mode, editor.user, actionCommentsRequired)
    if (errors.length > 0) {
      setValidationErrors(errors)
      return
    }

    setValidationErrors([])

    if (editor.mode === 'edit' && editor.user) {
      const request: UpdateManagedUserRequest = {
        version: editor.user.version,
        displayName: form.displayName.trim(),
        roleCodes: form.roleCodes,
        isActive: form.isActive,
        newPassword: form.password.length > 0 ? form.password : null,
        deactivationReason: editor.user.isActive && !form.isActive ? form.deactivationReason.trim() : null,
      }
      const changes = getUserEditorChanges(form, editor.user, roles)
      if (changes.length === 0) {
        closeEditor()
        return
      }

      if (editor.user.isActive && !request.isActive) {
        setDeactivationConfirmation({ user: editor.user, request })
        return
      }

      await updateEditedUser(editor.user, request)
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
          roleCodes: form.roleCodes,
          isActive: form.isActive,
        }
        await userClient.createUser(auth.accessToken, request)
      }

      closeEditor()
      if (offset === 0) {
        refreshUsersAfterMutation(0)
      } else {
        setOffset(0)
      }
      showToast('Пользователь добавлен.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить пользователя.'
      setError(message)
    } finally {
      setSaving(null)
    }
  }

  async function updateEditedUser(user: ManagedUserDto, request: UpdateManagedUserRequest) {
    setSaving('edit')
    setError(null)
    try {
      await userClient.updateUser(auth.accessToken, user.id, request)
      closeEditor()
      refreshUsersAfterMutation()
      showToast(user.isActive && !request.isActive ? 'Пользователь отключен.' : 'Пользователь изменен.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить пользователя.'
      setError(message)
    } finally {
      setSaving(null)
    }
  }

  async function retryUsersLoad() {
    const { controller, promise } = beginUsersPageRequest(offset)
    setLoading(true)
    setError(null)
    try {
      const [loadedPage, loadedRoles] = await Promise.all([
        promise,
        getRolesOnce(),
      ])
      if (usersPageControllerRef.current === controller && !controller.signal.aborted) {
        setPage(loadedPage)
        setHasLoadedPage(true)
        setRoles(loadedRoles)
      }
    } catch (caught) {
      if (usersPageControllerRef.current === controller && !controller.signal.aborted) {
        setError(caught instanceof Error ? caught.message : 'Не удалось загрузить пользователей.')
      }
    } finally {
      if (usersPageControllerRef.current === controller) {
        usersPageControllerRef.current = null
        if (!controller.signal.aborted) {
          setLoading(false)
        }
      }
    }
  }

  async function confirmDeactivateUser() {
    if (!deactivationConfirmation || busy) {
      return
    }

    await updateEditedUser(deactivationConfirmation.user, deactivationConfirmation.request)
  }

  async function deleteUser() {
    if (!deleteTarget || busy) {
      return
    }

    const reason = deleteReason.trim()
    if (actionCommentsRequired && !reason) {
      setDeleteReasonError('Укажите причину отключения пользователя.')
      return
    }

    setSaving('delete')
    setError(null)
    setDeleteReasonError(null)
    try {
      await userClient.updateUser(auth.accessToken, deleteTarget.id, {
        version: deleteTarget.version,
        displayName: deleteTarget.displayName,
        roleCodes: deleteTarget.roles,
        isActive: false,
        newPassword: null,
        deactivationReason: reason,
      })
      closeDeleteDialog()
      refreshUsersAfterMutation()
      showToast('Пользователь отключен.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось отключить пользователя.'
      setError(message)
    } finally {
      setSaving(null)
    }
  }

  async function restoreUser() {
    if (!restoreTarget || busy) {
      return
    }

    setSaving('restore')
    setError(null)
    try {
      await userClient.restoreUser(auth.accessToken, restoreTarget.id)
      closeRestoreDialog()
      refreshUsersAfterMutation()
      showToast('Пользователь восстановлен.')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Не удалось восстановить пользователя.')
    } finally {
      setSaving(null)
    }
  }

  function openRoleEditor(role: ManagedRoleDto) {
    setRolePermissionError(null)
    setError(null)
    setRoleEditor({ role, permissions: expandPermissionDependencies(role.permissions) })
  }

  function closeRoleEditor() {
    setRoleEditor(null)
    setRolePermissionError(null)
    setError(null)
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

      return { ...current, permissions: expandPermissionDependencies([...permissionsSet]) }
    })
  }

  function toggleUserRole(roleCode: string, checked: boolean) {
    setForm((current) => {
      const roleCodes = new Set(current.roleCodes)
      if (checked) {
        roleCodes.add(roleCode)
      } else {
        roleCodes.delete(roleCode)
      }

      return { ...current, roleCodes: [...roleCodes] }
    })
  }

  async function saveRolePermissions(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!roleEditor) {
      return
    }

    if (roleEditor.permissions.length === 0) {
      setRolePermissionError('Выберите хотя бы одно право для роли.')
      return
    }

    if (haveSamePermissions(roleEditor.role.permissions, roleEditor.permissions)) {
      closeRoleEditor()
      return
    }

    setSaving('role')
    setError(null)
    try {
      const updatedRole = await userClient.updateRolePermissions(auth.accessToken, roleEditor.role.code, { permissions: roleEditor.permissions })
      setRoles((current) => current.map((role) => (role.code === updatedRole.code ? updatedRole : role)))
      closeRoleEditor()
      refreshUsersAfterMutation()
      showToast('Права роли изменены.')
    } catch (caught) {
      const message = caught instanceof Error ? caught.message : 'Не удалось сохранить права роли.'
      setError(message)
    } finally {
      setSaving(null)
    }
  }

  function openDeleteDialog(user: ManagedUserDto) {
    setDeleteReason('')
    setDeleteReasonError(null)
    setError(null)
    setDeleteTarget(user)
  }

  function closeDeleteDialog() {
    setDeleteTarget(null)
    setDeleteReason('')
    setDeleteReasonError(null)
    setError(null)
  }

  function closeDeactivationConfirmation() {
    setDeactivationConfirmation(null)
    setError(null)
  }

  function closeRestoreDialog() {
    setRestoreTarget(null)
    setError(null)
  }

  function submitSearch(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setAppliedSearch(searchDraft.trim())
    setOffset(0)
  }

  const dialogErrorMessage = error ? <FormError>{error}</FormError> : null
  const dialogOpen = Boolean(editor || roleEditor || deleteTarget || restoreTarget)

  return (
    <section className="dictionary-panel-v2 users-panel-v2" aria-label="Пользователи" onClick={() => setContextMenu(null)}>
      <div className="section-heading">
        <div>
          <p className="eyebrow">Пользователи</p>
          <h2>Доступ в систему и роли сотрудников</h2>
        </div>
        {hasLoadedPage ? <span>{page.totalCount} пользователей</span> : null}
      </div>

      {error && !dialogOpen ? (
        <AsyncErrorState message={error} onRetry={() => void retryUsersLoad()} retrying={loading} />
      ) : null}

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
            <button className="secondary-button create-action-button" type="button" onClick={() => openEditor('create')} disabled={loading || roles.length === 0}>
              <UserPlus size={16} aria-hidden="true" />
              <span>Добавить</span>
            </button>
          </div>

          <div className="dictionary-table-scroll">
            <table className="dictionary-data-table users-data-table" aria-label="Список пользователей" aria-busy={loading} onContextMenu={(event) => event.preventDefault()}>
              <thead>
                <tr>
                  <th>Сотрудник</th>
                  <th>Email</th>
                  <th>Роль</th>
                  <th>Статус</th>
                  <th>Последний вход</th>
                  <th className="dictionary-actions-column users-actions-column table-actions-column">Действия</th>
                </tr>
              </thead>
              <tbody>
                {hasLoadedPage ? page.items.map((managedUser) => (
                  <tr
                    key={managedUser.id}
                    tabIndex={0}
                    onContextMenu={loading ? undefined : (event) => {
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
                    <td className="dictionary-actions-column users-actions-column table-actions-column">
                      <span className="dictionary-row-actions users-row-actions">
                        <button className="icon-button dictionary-row-action" type="button" aria-label={`Изменить пользователя ${managedUser.displayName}`} title="Изменить" disabled={loading} onClick={() => openEditor('edit', managedUser)}>
                          <Pencil size={16} aria-hidden="true" />
                        </button>
                        {managedUser.isActive ? (
                          <button className="icon-button danger-icon-button dictionary-row-action" type="button" aria-label={`Удалить пользователя ${managedUser.displayName}`} title="Удалить" disabled={loading} onClick={() => openDeleteDialog(managedUser)}>
                            <Trash2 size={16} aria-hidden="true" />
                          </button>
                        ) : (
                          <button className="icon-button dictionary-row-action" type="button" aria-label={`Восстановить пользователя ${managedUser.displayName}`} title="Восстановить" disabled={loading} onClick={() => setRestoreTarget(managedUser)}>
                            <RotateCcw size={16} aria-hidden="true" />
                          </button>
                        )}
                      </span>
                    </td>
                  </tr>
                )) : null}
                {hasLoadedPage && !loading && page.items.length === 0 ? (
                  <tr>
                    <td colSpan={6}>
                      <EmptyState>Пользователей пока нет</EmptyState>
                    </td>
                  </tr>
                ) : null}
              </tbody>
            </table>
            {loading && !hasLoadedPage ? <TableLoadingState label="Загружаем пользователей" /> : null}
            {loading && hasLoadedPage ? <BackgroundRefreshStatus label="Обновляем список пользователей" /> : null}
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

      {contextMenu && !loading ? (
        <div className="context-menu" role="menu" style={{ left: contextMenu.x, top: contextMenu.y }} onClick={(event) => event.stopPropagation()}>
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
        <div className="modal-backdrop" role="presentation" onMouseDown={busy ? undefined : closeEditor}>
          <section ref={editorDialogRef} className="detail-dialog dictionary-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="user-editor-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <h3 id="user-editor-title">{editor.mode === 'create' ? 'Новый пользователь' : 'Изменить пользователя'}</h3>
                <p>{editor.mode === 'create' ? 'Создайте сотрудника и назначьте роль.' : 'Измените имя, роли, статус или пароль.'}</p>
              </div>
              <button ref={editorCloseRef} className="icon-button" type="button" onClick={closeEditor} aria-label="Закрыть окно пользователя" disabled={busy}>
                <X size={18} />
              </button>
            </div>
            <form className="dictionary-modal-form" autoComplete="off" onSubmit={saveUser}>
              {editor.mode === 'create' ? (
                <FormField label="Email">
                  <input aria-label="Email пользователя" autoComplete="off" data-1p-ignore data-lpignore="true" name="managed-user-email" placeholder="email@example.com" value={form.email} onChange={(event) => setForm({ ...form, email: event.target.value })} type="email" disabled={busy} required />
                </FormField>
              ) : (
                <FormField label="Email">
                  <input aria-label="Email пользователя" autoComplete="off" name="managed-user-email-readonly" value={form.email} disabled />
                </FormField>
              )}
              <FormField label="Имя сотрудника">
                <input aria-label="Имя пользователя" autoComplete="off" name="managed-user-display-name" placeholder="ФИО или рабочее имя" value={form.displayName} onChange={(event) => setForm({ ...form, displayName: event.target.value })} disabled={busy} required />
              </FormField>
              <FormField label="Роли">
                <div className="user-role-assignment" role="group" aria-label="Роли пользователя">
                  {roles.map((role) => (
                    <label className="contractors-check-row" key={role.code}>
                      <input
                        type="checkbox"
                        aria-label={`Роль пользователя: ${role.name}`}
                        checked={form.roleCodes.includes(role.code)}
                        disabled={busy}
                        onChange={(event) => toggleUserRole(role.code, event.target.checked)}
                      />
                      <span>{role.name}</span>
                    </label>
                  ))}
                </div>
              </FormField>
              <FormField label="Статус">
                <SelectControl
                  aria-label="Статус пользователя"
                  value={form.isActive ? 'active' : 'inactive'}
                  disabled={busy}
                  options={[{ value: 'active', label: 'Активен' }, { value: 'inactive', label: 'Отключен' }]}
                  onChange={(value) => setForm({ ...form, isActive: value === 'active' })} />
              </FormField>
              {editor.user?.isActive && !form.isActive ? (
                <FormField label="Причина отключения">
                  <textarea
                    aria-label="Причина отключения пользователя"
                    placeholder="Например: сотрудник больше не работает или доступ выдан ошибочно"
                    maxLength={1000}
                    value={form.deactivationReason}
                    disabled={busy}
                    onChange={(event) => setForm({ ...form, deactivationReason: event.target.value })}
                    required={actionCommentsRequired}
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
                  disabled={busy}
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
                  disabled={busy}
                  onChange={(event) => setForm({ ...form, passwordConfirmation: event.target.value })}
                  type="password"
                  minLength={editor.mode === 'create' ? 8 : undefined}
                  required={editor.mode === 'create'}
                />
              </FormField>
              <p className="form-hint" id="new-user-password-policy-hint">Минимум 8 символов.</p>
              <FormValidationSummary title={editor.mode === 'create' ? 'Проверьте нового пользователя' : 'Проверьте пользователя'} items={validationErrors} />
              {!deactivationConfirmation ? dialogErrorMessage : null}
              <div className="detail-dialog-actions">
                <button className="secondary-button" type="submit" disabled={busy || roles.length === 0}>
                  <Save size={16} />
                  <span>{saving === editor.mode ? 'Сохраняем...' : 'Сохранить'}</span>
                </button>
                <button className="ghost-button" type="button" onClick={closeEditor} disabled={busy}>Отмена</button>
              </div>
            </form>
          </section>
        </div>
      ) : null}

      {deactivationConfirmation ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={saving === 'edit' ? undefined : closeDeactivationConfirmation}>
          <section ref={deactivationConfirmationDialogRef} className="detail-dialog dictionary-confirmation-dialog" role="dialog" aria-modal="true" aria-labelledby="user-deactivation-confirmation-title" aria-describedby="user-deactivation-confirmation-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <p className="eyebrow">Отключение</p>
                <h3 id="user-deactivation-confirmation-title">Отключить пользователя?</h3>
                <p>{deactivationConfirmation.user.displayName}</p>
              </div>
              <button className="icon-button" type="button" aria-label="Отменить отключение пользователя" onClick={closeDeactivationConfirmation} disabled={saving === 'edit'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="user-deactivation-confirmation-description">Пользователь потеряет доступ. Причина отключения сохранится в истории изменений.</p>
            {dialogErrorMessage}
            <div className="detail-dialog-actions">
              <button ref={deactivationConfirmationCancelRef} className="ghost-button" type="button" onClick={closeDeactivationConfirmation} disabled={saving === 'edit'}>Отмена</button>
              <button className="ghost-button danger-button" type="button" onClick={() => void confirmDeactivateUser()} disabled={saving === 'edit'}>
                <Trash2 size={16} />
                <span>{saving === 'edit' ? 'Отключаем...' : 'Отключить'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {roleEditor ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={saving === 'role' ? undefined : closeRoleEditor}>
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
                  const requiredBySelection = isPermissionRequiredBySelection(group.permission, roleEditor.permissions)
                  return (
                    <label className="contractors-check-row" key={group.permission}>
                      <input
                        type="checkbox"
                        aria-label={`${roleEditor.role.name}: ${group.label}`}
                        checked={roleEditor.permissions.includes(group.permission)}
                        disabled={saving === 'role' || administratorUsersManage || requiredBySelection}
                        onChange={(event) => toggleRolePermission(group.permission, event.target.checked)}
                      />
                      <span>{group.label}{requiredBySelection ? ' · требуется выбранным правом' : ''}</span>
                    </label>
                  )
                })}
              </div>
              {rolePermissionError ? <FormError>{rolePermissionError}</FormError> : null}
              {dialogErrorMessage}
              <p className="form-hint">Права применяются к пользователям с этой ролью после обновления их сессии. Изменение будет записано в историю.</p>
              <div className="detail-dialog-actions">
                <button className="ghost-button" type="button" onClick={closeRoleEditor} disabled={saving === 'role'}>Отмена</button>
                <button className="secondary-button" type="submit" disabled={saving === 'role'}>
                  <Save size={16} />
                  <span>{saving === 'role' ? 'Сохраняем...' : 'Сохранить'}</span>
                </button>
              </div>
            </form>
          </section>
        </div>
      ) : null}

      {deleteTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={saving === 'delete' ? undefined : closeDeleteDialog}>
          <section ref={deleteDialogRef} className="detail-dialog dictionary-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="user-delete-title" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <h3 id="user-delete-title">Удалить пользователя</h3>
                <p>{deleteTarget.displayName} будет отключен. История изменений сохранится.</p>
              </div>
              <button className="icon-button" type="button" onClick={closeDeleteDialog} aria-label="Закрыть подтверждение удаления" disabled={saving === 'delete'}>
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
              required={actionCommentsRequired}
            />
            {deleteReasonError ? <p className="form-error" id="user-delete-reason-error">{deleteReasonError}</p> : null}
            {dialogErrorMessage}
            <div className="detail-dialog-actions">
              <button ref={deleteCancelRef} className="ghost-button" type="button" onClick={closeDeleteDialog} disabled={saving === 'delete'}>Отмена</button>
              <button className="secondary-button danger-button" type="button" onClick={deleteUser} disabled={saving === 'delete' || (actionCommentsRequired && !deleteReason.trim())}>
                <Trash2 size={16} />
                <span>{saving === 'delete' ? 'Удаляем...' : 'Удалить'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      {restoreTarget ? (
        <div className="modal-backdrop" role="presentation" onMouseDown={saving === 'restore' ? undefined : closeRestoreDialog}>
          <section ref={restoreDialogRef} className="detail-dialog dictionary-editor-dialog" role="dialog" aria-modal="true" aria-labelledby="user-restore-title" aria-describedby="user-restore-description" onMouseDown={(event) => event.stopPropagation()}>
            <div className="detail-dialog-header">
              <div>
                <h3 id="user-restore-title">Вернуть пользователя?</h3>
                <p>{restoreTarget.displayName} снова сможет входить в систему с прежними ролями.</p>
              </div>
              <button className="icon-button" type="button" onClick={closeRestoreDialog} aria-label="Отменить восстановление пользователя" disabled={saving === 'restore'}>
                <X size={18} />
              </button>
            </div>
            <p className="confirmation-text" id="user-restore-description">Действие будет записано в историю изменений.</p>
            {dialogErrorMessage}
            <div className="detail-dialog-actions">
              <button ref={restoreCancelRef} className="ghost-button" type="button" onClick={closeRestoreDialog} disabled={saving === 'restore'}>Отмена</button>
              <button className="secondary-button" type="button" onClick={() => void restoreUser()} disabled={saving === 'restore'}>
                <RotateCcw size={16} />
                <span>{saving === 'restore' ? 'Возвращаем...' : 'Вернуть'}</span>
              </button>
            </div>
          </section>
        </div>
      ) : null}

      <ToastViewport toast={toast} onDismiss={dismissToast} />
    </section>
  )
}

function haveSamePermissions(current: readonly string[], next: readonly string[]) {
  return current.length === next.length && current.every((permission) => next.includes(permission))
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

      <div className="role-matrix-table-scroll" tabIndex={0} role="region" aria-label="Прокручиваемая матрица ролей и прав">
        <table className="role-matrix-table" aria-label="Матрица ролей и прав">
          <thead>
            <tr>
              <th scope="col">Роль</th>
              {rolePermissionGroups.map((group) => (
                <th scope="col" key={group.permission}>{group.label}</th>
              ))}
              <th scope="col">Действия</th>
            </tr>
          </thead>
          <tbody>
            {roles.length === 0 ? (
              <tr>
                <td colSpan={rolePermissionGroups.length + 2}><StatusMessage>Роли пока не загружены</StatusMessage></td>
              </tr>
            ) : null}
            {roles.map((role) => (
              <tr key={role.code}>
                <th scope="row">
                  <strong>{role.name}</strong>
                  <small>{role.code}</small>
                </th>
                {rolePermissionGroups.map((group) => {
                  const allowed = role.permissions.includes(group.permission)
                  return (
                    <td aria-label={`${role.name}: ${group.label} - ${allowed ? 'разрешено' : 'нет доступа'}`} key={group.permission}>
                      <span className={allowed ? 'status-active' : 'status-disabled'}>{allowed ? 'Да' : 'Нет'}</span>
                    </td>
                  )
                })}
                <td className="role-matrix-actions">
                  <button className="icon-button" type="button" aria-label={`Изменить права роли ${role.name}`} title={`Изменить права роли ${role.name}`} onClick={() => onEditRole(role)}>
                    <ShieldCheck size={16} />
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}
