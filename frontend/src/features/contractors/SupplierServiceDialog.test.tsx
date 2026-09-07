import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { SupplierServiceDialog } from './SupplierServiceDialog'

const services = [
  { id: 'clean', name: 'Уборка', version: 'v1', isArchived: false },
  { id: 'old', name: 'Архивная', version: 'v2', isArchived: true },
]

describe('SupplierServiceDialog', () => {
  it('validates and creates a name-only service, preventing repeated saves and closing while saving', async () => {
    const user = userEvent.setup()
    let complete!: () => void
    const onSave = vi.fn(() => new Promise<void>((resolve) => { complete = resolve }))
    const onClose = vi.fn()
    render(<SupplierServiceDialog services={services} onSave={onSave} onClose={onClose} />)
    expect(screen.getAllByRole('textbox')).toHaveLength(1)
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument()
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Введите наименование')
    expect(onSave).not.toHaveBeenCalled()
    fireEvent.change(screen.getByLabelText('Наименование услуги'), { target: { value: 'x'.repeat(201) } })
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(onSave).not.toHaveBeenCalled()
    fireEvent.change(screen.getByLabelText('Наименование услуги'), { target: { value: ' Уборка двора ' } })
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(onSave).toHaveBeenCalledWith({ name: 'Уборка двора' }, undefined)
    expect(screen.getByRole('status')).toHaveTextContent('Сохраняем услугу')
    expect(screen.getByRole('button', { name: 'Отмена' })).toBeDisabled()
    await user.keyboard('{Escape}')
    fireEvent.submit(screen.getByRole('button', { name: 'Сохраняем...' }).closest('form')!)
    expect(onClose).not.toHaveBeenCalled()
    expect(onSave).toHaveBeenCalledTimes(1)
    complete()
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce())
  })

  it('renames the selected active service with its version and retains input on a server error', async () => {
    const user = userEvent.setup()
    const onSave = vi.fn().mockRejectedValueOnce(new Error('Запись изменена другим пользователем.')).mockResolvedValue(undefined)
    const onClose = vi.fn()
    render(<SupplierServiceDialog edit services={services} onSave={onSave} onClose={onClose} />)
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(screen.getByRole('alert')).toHaveTextContent('Выберите услугу')
    await user.click(screen.getByRole('combobox', { name: 'Услуга для изменения' }))
    expect(screen.queryByRole('option', { name: 'Архивная' })).not.toBeInTheDocument()
    await user.click(screen.getByRole('option', { name: 'Уборка' }))
    expect(screen.getByLabelText('Наименование услуги')).toHaveValue('Уборка')
    await user.type(screen.getByLabelText('Наименование услуги'), ' территории')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Запись изменена')
    expect(onClose).not.toHaveBeenCalled()
    expect(screen.getByLabelText('Наименование услуги')).toHaveValue('Уборка территории')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(onSave).toHaveBeenLastCalledWith({ name: 'Уборка территории', version: 'v1' }, 'clean')
    await waitFor(() => expect(onClose).toHaveBeenCalledOnce())
  })

  it('shows a fallback error and allows cancelling without saving', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    render(<SupplierServiceDialog services={[]} onSave={vi.fn().mockRejectedValue(null)} onClose={onClose} />)
    await user.type(screen.getByLabelText('Наименование услуги'), 'Услуга')
    await user.click(screen.getByRole('button', { name: 'Сохранить' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('Не удалось сохранить')
    await user.keyboard('{Escape}')
    expect(onClose).toHaveBeenCalledOnce()
    await user.click(screen.getByRole('button', { name: 'Отмена' }))
    await user.click(screen.getByRole('button', { name: 'Закрыть форму услуги' }))
    expect(onClose).toHaveBeenCalledTimes(3)
  })
})
