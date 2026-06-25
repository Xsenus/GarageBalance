export function getPasswordPolicyErrors(password: string, emptyMessage = 'Укажите пароль.') {
  const errors: string[] = []
  if (!password) {
    errors.push(emptyMessage)
  } else {
    if (password.length < 8) {
      errors.push('Пароль должен быть не короче 8 символов.')
    }

    if (!/[A-ZА-ЯЁ]/.test(password)) {
      errors.push('Добавьте заглавную букву в пароль.')
    }

    if (!/[a-zа-яё]/.test(password)) {
      errors.push('Добавьте строчную букву в пароль.')
    }

    if (!/\d/.test(password)) {
      errors.push('Добавьте хотя бы одну цифру в пароль.')
    }
  }

  return errors
}

export function getAuthValidationErrors(mode: 'bootstrap' | 'login', email: string, displayName: string, password: string) {
  const errors: string[] = []
  const trimmedEmail = email.trim()

  if (!trimmedEmail) {
    errors.push('Укажите email.')
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)) {
    errors.push('Проверьте формат email.')
  }

  if (mode === 'bootstrap' && !displayName.trim()) {
    errors.push('Укажите имя пользователя.')
  }

  errors.push(...getPasswordPolicyErrors(password))

  return errors
}

export function getPasswordChangeValidationErrors(currentPassword: string, newPassword: string, repeatPassword: string) {
  const errors: string[] = []

  if (!currentPassword) {
    errors.push('Укажите текущий пароль.')
  }

  errors.push(...getPasswordPolicyErrors(newPassword, 'Укажите новый пароль.'))

  if (!repeatPassword) {
    errors.push('Повторите новый пароль.')
  } else if (newPassword !== repeatPassword) {
    errors.push('Новый пароль и повтор пароля не совпадают.')
  }

  return errors
}

export function getManagedUserValidationErrors(email: string, displayName: string, password: string, roleCode: string) {
  const errors: string[] = []
  const trimmedEmail = email.trim()

  if (!trimmedEmail) {
    errors.push('Укажите email пользователя.')
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(trimmedEmail)) {
    errors.push('Проверьте формат email пользователя.')
  }

  if (!displayName.trim()) {
    errors.push('Укажите имя пользователя.')
  }

  errors.push(...getPasswordPolicyErrors(password, 'Укажите пароль пользователя.'))

  if (!roleCode) {
    errors.push('Выберите роль пользователя.')
  }

  return errors
}
