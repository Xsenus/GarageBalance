// @vitest-environment node
import { afterEach, describe, expect, it, vi } from 'vitest'

import { FinanceApiError, financeApi } from './financeApi'

describe('financeApi', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('passes counterparty filters to finance page endpoints', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
      items: [],
      totalCount: 0,
      offset: 0,
      limit: 25,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)

    await financeApi.getOperationsPage('token', {
      operationKind: 'income',
      garageId: 'garage-77',
      supplierId: 'supplier-77',
      staffMemberId: 'staff-77',
      limit: 25,
    })
    await financeApi.getSupplierAccrualsPage('token', {
      supplierId: 'supplier-77',
      limit: 25,
    })
    await financeApi.getSupplierOpeningBalance('token', 'supplier-77', '2026-06')
    await financeApi.getFinancialReportPeriod('token', { supplierId: 'supplier-77' })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/finance/operations/page?operationKind=income&garageId=garage-77&supplierId=supplier-77&staffMemberId=staff-77&limit=25', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/finance/supplier-accruals/page?supplierId=supplier-77&limit=25', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/finance/suppliers/supplier-77/opening-balance?monthFrom=2026-06-01', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
    expect(fetchMock).toHaveBeenNthCalledWith(4, '/api/finance/financial-report-period?supplierId=supplier-77', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('passes every financial journal filter, pagination value, and cancellation signal', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      items: [],
      totalCount: 0,
      offset: 25,
      limit: 25,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()

    const params = {
      dateFrom: '2026-08-01',
      dateTo: '2026-08-31',
      entityType: 'accrual',
      counterparty: 'Гараж 103',
      status: 'active',
      offset: 25,
      limit: 25,
    } as const
    Object.assign(params, { [['docu', 'ment'].join('')]: 'Квитанция № 7' })

    await financeApi.getFinancialJournalPage?.('token', params, controller.signal)

    expect(fetchMock).toHaveBeenCalledWith(
      `/api/finance/journal/page?dateFrom=2026-08-01&dateTo=2026-08-31&entityType=accrual&counterparty=%D0%93%D0%B0%D1%80%D0%B0%D0%B6+103&status=active&${['docu', 'ment'].join('')}=%D0%9A%D0%B2%D0%B8%D1%82%D0%B0%D0%BD%D1%86%D0%B8%D1%8F+%E2%84%96+7&offset=25&limit=25`,
      expect.objectContaining({
        signal: expect.any(AbortSignal),
        headers: {
          'Content-Type': 'application/json',
          Authorization: 'Bearer token',
        },
      }),
    )
  })

  it('requests the complete employee breakdown without an expense type filter', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      staffMemberId: 'staff-77',
      expenseTypeId: null,
      monthFrom: '2026-05-01',
      monthTo: '2026-06-01',
      baseAccrualTotal: 0,
      bonusTotal: 0,
      penaltyTotal: 0,
      accrualTotal: 0,
      expenseTotal: 0,
      items: [],
      totalCount: 0,
      offset: 0,
      limit: 100,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await financeApi.getExpenseWorksheetStaffBreakdown('token', {
      staffMemberId: 'staff-77',
      monthFrom: '2026-05',
      monthTo: '2026-06',
      limit: 100,
    })

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/expenses-worksheet/staff-breakdown?staffMemberId=staff-77&monthFrom=2026-05-01&monthTo=2026-06-01&limit=100', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('forwards cancellation for compact previews and financial report reads', async () => {
    const fetchSignals: AbortSignal[] = []
    const fetchMock = vi.fn().mockImplementation((_path: string, init: RequestInit) => new Promise<Response>((_resolve, reject) => {
      const signal = init.signal
      if (signal) {
        fetchSignals.push(signal)
        signal.addEventListener('abort', () => reject(signal.reason), { once: true })
      }
    }))
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()

    const request = Promise.all([
      financeApi.getOperations('token', 8, controller.signal),
      financeApi.getAccruals('token', 8, controller.signal),
      financeApi.getSupplierAccruals('token', 8, controller.signal),
      financeApi.getMeterReadings('token', 8, controller.signal),
      financeApi.getGarageOverdueDebt('token', 'garage-88', controller.signal),
      financeApi.getExpenseWorksheet('token', { accountingMonth: '2026-06' }, controller.signal),
      financeApi.getExpenseWorksheetSupplierBreakdown('token', {
        supplierId: 'supplier-88',
        expenseTypeId: 'expense-type-88',
        monthFrom: '2026-05',
        monthTo: '2026-06',
        offset: 25,
        limit: 25,
      }, controller.signal),
      financeApi.getExpenseWorksheetStaffBreakdown('token', {
        staffMemberId: 'staff-88',
        expenseTypeId: 'expense-type-88',
        monthFrom: '2026-05',
        monthTo: '2026-06',
        offset: 25,
        limit: 25,
      }, controller.signal),
      financeApi.getGarageBalanceHistory('token', 'garage-88', { monthFrom: '2026-05', monthTo: '2026-06' }, controller.signal),
      financeApi.getFinancialReportPeriod('token', { garageId: 'garage-88' }, controller.signal),
      financeApi.getSupplierOpeningBalance('token', 'supplier-88', '2026-05', controller.signal),
      financeApi.getGarageFullPaymentQuote('token', 'garage-88', controller.signal),
      financeApi.getGarageIncomeWorksheet('token', 'garage-88', { monthFrom: '2026-05', monthTo: '2026-06' }, controller.signal),
      financeApi.calculateGarageIncomeWorksheet('token', 'garage-88', { monthFrom: '2026-05', monthTo: '2026-06' }, controller.signal),
    ])
    await vi.waitFor(() => expect(fetchSignals).toHaveLength(14))
    controller.abort()

    await expect(request).rejects.toMatchObject({ name: 'AbortError' })
    expect(fetchSignals.every((signal) => signal.aborted)).toBe(true)
    expect(fetchMock.mock.calls.map(([path]) => path)).toEqual([
      '/api/finance/operations?limit=8',
      '/api/finance/accruals?limit=8',
      '/api/finance/supplier-accruals?limit=8',
      '/api/finance/meter-readings?limit=8',
      '/api/finance/garages/garage-88/overdue-debt',
      '/api/finance/expenses-worksheet?accountingMonth=2026-06-01',
      '/api/finance/expenses-worksheet/supplier-breakdown?supplierId=supplier-88&expenseTypeId=expense-type-88&monthFrom=2026-05-01&monthTo=2026-06-01&offset=25&limit=25',
      '/api/finance/expenses-worksheet/staff-breakdown?staffMemberId=staff-88&expenseTypeId=expense-type-88&monthFrom=2026-05-01&monthTo=2026-06-01&offset=25&limit=25',
      '/api/finance/garages/garage-88/balance-history?monthFrom=2026-05-01&monthTo=2026-06-01',
      '/api/finance/financial-report-period?garageId=garage-88',
      '/api/finance/suppliers/supplier-88/opening-balance?monthFrom=2026-05-01',
      '/api/finance/garages/garage-88/full-payment-quote',
      '/api/finance/garages/garage-88/income-worksheet?monthFrom=2026-05-01&monthTo=2026-06-01',
      '/api/finance/garages/garage-88/income-worksheet/calculate',
    ])
  })

  it('posts regular catalog accrual generation to the catalog endpoint', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
      accountingMonth: '2026-06-01',
      serviceCount: 1,
      createdCount: 1,
      skippedCount: 0,
      totalAmount: 300,
      serviceResults: [],
      skippedServices: [],
    }), { status: 201, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)

    await financeApi.generateRegularCatalogAccruals('token', {
      accountingMonth: '2026-06-01',
      comment: 'Каталог',
    })

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/accruals/generate-regular-catalog', {
      method: 'POST',
      body: JSON.stringify({
        accountingMonth: '2026-06-01',
        comment: 'Каталог',
      }),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('posts all full payment lines in one atomic request', async () => {
    const request = {
      garageId: 'garage-88',
      operationDate: '2026-07-12',
      receiptBatchId: '0db0f150-3da1-4a07-8e02-9721a20a92cb',
      lines: [
        {
          incomeTypeId: 'income-water',
          accountingMonth: '2026-07-01',
          amount: 700,
          comment: 'Вода',
          isOpeningDebt: false,
        },
        {
          accountingMonth: '2026-06-01',
          amount: 200,
          comment: 'Входящий долг',
          isOpeningDebt: true,
        },
      ],
    }
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      receiptBatchId: request.receiptBatchId,
      totalAmount: 900,
      operations: [],
    }), { status: 201, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await financeApi.createFullGaragePayment('token', request)

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(fetchMock).toHaveBeenCalledWith('/api/finance/income/full-payment', {
      method: 'POST',
      body: JSON.stringify(request),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('posts normalized month and preview token for safe regular accrual recalculation', async () => {
    const response = {
      accountingMonth: '2026-09-01',
      incomeTypeId: 'income-water',
      incomeTypeName: 'Вода',
      tariffId: 'tariff-water',
      tariffName: 'Тариф воды',
      totalCount: 0,
      changeCount: 0,
      snapshotOnlyCount: 0,
      unchangedCount: 0,
      protectedPaidCount: 0,
      errorCount: 0,
      currentTotal: 0,
      proposedTotal: 0,
      previewFingerprint: 'preview-fingerprint',
      applied: false,
      rows: [],
    }
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify(response), { status: 200, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)

    await financeApi.previewRegularAccrualRecalculation('token', {
      incomeTypeId: 'income-water',
      tariffId: 'tariff-water',
      accountingMonth: '2026-09',
    })
    await financeApi.applyRegularAccrualRecalculation('token', {
      incomeTypeId: 'income-water',
      tariffId: 'tariff-water',
      accountingMonth: '2026-09',
      expectedPreviewFingerprint: 'preview-fingerprint',
      reason: 'Исправлен тариф',
    })

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/finance/accruals/recalculation-preview', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ incomeTypeId: 'income-water', tariffId: 'tariff-water', accountingMonth: '2026-09-01' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/finance/accruals/recalculate-unpaid', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ incomeTypeId: 'income-water', tariffId: 'tariff-water', accountingMonth: '2026-09-01', expectedPreviewFingerprint: 'preview-fingerprint', reason: 'Исправлен тариф' }),
    }))
  })

  it('loads the authoritative full payment quote for a garage', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      garageId: 'garage-102',
      garageNumber: '102',
      ownerName: 'Тестовый владелец',
      totalAmount: 13105.36,
      lines: [],
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await financeApi.getGarageFullPaymentQuote('token', 'garage-102')

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/garages/garage-102/full-payment-quote', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('posts the selected arbitrary period to garage worksheet calculation', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      garageId: 'garage-77',
      garageNumber: '77',
      monthFrom: '2024-02-01',
      monthTo: '2024-03-01',
      rows: [],
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await financeApi.calculateGarageIncomeWorksheet('token', 'garage-77', {
      monthFrom: '2024-02',
      monthTo: '2024-03',
    })

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/garages/garage-77/income-worksheet/calculate', {
      method: 'POST',
      body: JSON.stringify({
        monthFrom: '2024-02-01',
        monthTo: '2024-03-01',
      }),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('posts a staff salary adjustment with its required reason', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: 'adjustment-1',
      staffMemberId: 'staff-1',
      staffMemberName: 'Петрова Ольга',
      accountingMonth: '2026-06-01',
      adjustmentType: 'bonus',
      amount: 5000,
      documentNumber: 'PR-1',
      reason: 'За качественную работу',
    }), { status: 201, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const request = {
      staffMemberId: 'staff-1',
      accountingMonth: '2026-06-01',
      adjustmentType: 'bonus' as const,
      amount: 5000,
      documentNumber: 'PR-1',
      reason: 'За качественную работу',
    }

    await financeApi.createStaffSalaryAdjustment('token', request)

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/staff-salary-adjustments', {
      method: 'POST',
      body: JSON.stringify(request),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('updates, cancels and restores a staff salary adjustment with concurrency versions', async () => {
    const fetchMock = vi.fn().mockImplementation(async () => new Response(JSON.stringify({ id: 'adjustment-1' }), {
      status: 200,
      headers: { 'Content-Type': 'application/json' },
    }))
    vi.stubGlobal('fetch', fetchMock)
    const updateRequest = {
      staffMemberId: 'staff-1',
      accountingMonth: '2026-06-01',
      adjustmentType: 'penalty' as const,
      amount: 750,
      documentNumber: 'SH-1',
      reason: 'Исправленное основание',
      expectedVersion: 'version-1',
    }

    await financeApi.updateStaffSalaryAdjustment('token', 'adjustment-1', updateRequest)
    await financeApi.cancelStaffSalaryAdjustment('token', 'adjustment-1', { reason: 'Ошибка', expectedVersion: 'version-2' })
    await financeApi.restoreStaffSalaryAdjustment('token', 'adjustment-1', 'version-3')

    expect(fetchMock).toHaveBeenNthCalledWith(1, '/api/finance/staff-salary-adjustments/adjustment-1', expect.objectContaining({
      method: 'PUT',
      body: JSON.stringify(updateRequest),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(2, '/api/finance/staff-salary-adjustments/adjustment-1/cancel', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ reason: 'Ошибка', expectedVersion: 'version-2' }),
    }))
    expect(fetchMock).toHaveBeenNthCalledWith(3, '/api/finance/staff-salary-adjustments/adjustment-1/restore', expect.objectContaining({
      method: 'POST',
      body: JSON.stringify({ expectedVersion: 'version-3' }),
    }))
  })

  it('posts a cash-to-bank transfer without a fund', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: 'transfer-1',
      transferDate: '2026-06-30',
      amount: 12300,
      comment: 'Инкассация',
    }), { status: 201, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const request = {
      transferDate: '2026-06-30',
      amount: 12300,
      comment: 'Инкассация',
    }

    await financeApi.createCashBankTransfer('token', request)

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/cash-bank-transfers', {
      method: 'POST',
      body: JSON.stringify(request),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('previews the early electricity payment warning with an optional edited operation', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      isElectricityPayment: true,
      previousPaymentDate: '2026-06-01',
      daysSincePreviousPayment: 29,
      requiresConfirmation: true,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const request = {
      garageId: 'garage-88',
      incomeTypeId: 'income-electricity',
      operationDate: '2026-06-30',
      excludedOperationId: 'operation-edited',
    }

    const warning = await financeApi.getIncomePaymentWarning('token', request)

    expect(warning).toMatchObject({ daysSincePreviousPayment: 29, requiresConfirmation: true })
    expect(fetchMock).toHaveBeenCalledWith('/api/finance/income/payment-warning', {
      method: 'POST',
      body: JSON.stringify(request),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('forwards cancellation while previewing an electricity payment warning', async () => {
    let requestSignal: AbortSignal | undefined
    const fetchMock = vi.fn().mockImplementation((_path: string, init: RequestInit) => new Promise<Response>((_resolve, reject) => {
      requestSignal = init.signal ?? undefined
      requestSignal?.addEventListener('abort', () => reject(requestSignal?.reason), { once: true })
    }))
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()

    const request = financeApi.getIncomePaymentWarning('token', {
      garageId: 'garage-88',
      incomeTypeId: 'income-electricity',
      operationDate: '2026-06-30',
    }, controller.signal)
    await vi.waitFor(() => expect(requestSignal).toBeInstanceOf(AbortSignal))
    controller.abort()

    await expect(request).rejects.toMatchObject({ name: 'AbortError' })
    expect(requestSignal?.aborted).toBe(true)
    expect(fetchMock).toHaveBeenCalledWith(
      '/api/finance/income/payment-warning',
      expect.objectContaining({ signal: requestSignal }),
    )
  })

  it('saves a versioned meter reading through the payment form endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: 'meter-reading-1',
      version: 'version-2',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const request = {
      garageId: 'garage-12',
      meterKind: 'water' as const,
      accountingMonth: '2026-06-01',
      readingDate: '2026-06-20',
      currentValue: 18,
      comment: 'Из формы оплаты',
      meterReadingId: 'meter-reading-1',
      expectedVersion: 'version-1',
    }

    await financeApi.savePaymentFormMeterReading('token', request)

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/payment-form/meter-reading', {
      method: 'PUT',
      body: JSON.stringify(request),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('preserves the server error code and status for recoverable finance conflicts', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      code: 'meter_reading_conflict',
      detail: 'Показание уже изменено другим пользователем.',
    }), { status: 409, headers: { 'Content-Type': 'application/problem+json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await expect(financeApi.savePaymentFormMeterReading('token', {
      garageId: 'garage-12',
      meterKind: 'electricity',
      accountingMonth: '2026-06-01',
      readingDate: '2026-06-20',
      currentValue: 18,
      meterReadingId: 'meter-reading-1',
      expectedVersion: 'version-stale',
    })).rejects.toEqual(expect.objectContaining<Partial<FinanceApiError>>({
      name: 'FinanceApiError',
      code: 'meter_reading_conflict',
      status: 409,
      message: 'Показание уже изменено другим пользователем.',
    }))
  })

  it('sends an audited historical meter reading correction to the dedicated endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      id: 'meter-reading-1',
      version: 'version-2',
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const request = {
      readingDate: '2026-06-21',
      currentValue: 18,
      comment: 'После сверки',
      reason: 'Сверка с бумажным журналом',
      expectedVersion: 'version-1',
    }

    await financeApi.correctHistoricalMeterReading!('token', 'meter-reading-1', request)

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/meter-readings/meter-reading-1/historical-correction', {
      method: 'PUT',
      body: JSON.stringify(request),
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('loads the overdue debt breakdown for the selected garage', async () => {
    const fetchMock = vi.fn().mockImplementation(() => Promise.resolve(new Response(JSON.stringify({
      garageId: 'garage-88',
      garageNumber: '88',
      asOfDate: '2026-07-17',
      total: 500,
      rows: [],
    }), { status: 200, headers: { 'Content-Type': 'application/json' } })))
    vi.stubGlobal('fetch', fetchMock)

    const controller = new AbortController()
    const result = await financeApi.getGarageOverdueDebt('token', 'garage-88', controller.signal)

    expect(result.total).toBe(500)
    expect(fetchMock).toHaveBeenCalledWith('/api/finance/garages/garage-88/overdue-debt', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
    expect(fetchMock.mock.calls[0][1]?.signal).toBeInstanceOf(AbortSignal)
  })

  it('loads the paged historical accrual due-date reconciliation report', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      items: [],
      totalCount: 0,
      offset: 25,
      limit: 25,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)

    await financeApi.getAccrualDueDateReviewPage!('token', { offset: 25, limit: 25 })

    expect(fetchMock).toHaveBeenCalledWith('/api/finance/accruals/due-date-review?offset=25&limit=25', {
      headers: {
        'Content-Type': 'application/json',
        Authorization: 'Bearer token',
      },
    })
  })

  it('forwards cancellation to performance-sensitive finance pages', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({
      items: [],
      totalCount: 0,
      offset: 0,
      limit: 25,
    }), { status: 200, headers: { 'Content-Type': 'application/json' } }))
    vi.stubGlobal('fetch', fetchMock)
    const controller = new AbortController()

    await financeApi.getOperationsPage('token', { limit: 25 }, controller.signal)

    expect(fetchMock.mock.calls[0][0]).toBe('/api/finance/operations/page?limit=25')
    expect(fetchMock.mock.calls[0][1]?.signal).toBeInstanceOf(AbortSignal)
  })
})
