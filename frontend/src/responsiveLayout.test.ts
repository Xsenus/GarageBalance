import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'

import { describe, expect, it } from 'vitest'

describe('responsive layout styles', () => {
  const appCss = readFileSync(resolve(process.cwd(), 'src', 'App.css'), 'utf8')
  const normalizedAppCss = appCss.replace(/\r\n/g, '\n')

  it('collapses the main shell and data rows on tablet width', () => {
    expect(appCss).toContain('@media (max-width: 980px)')
    expect(normalizedAppCss).toContain('.app-shell {\n    grid-template-columns: 1fr;')
    expect(normalizedAppCss).toContain('.user-table-row,\n  .operation-row {\n    grid-template-columns: 1fr;')
  })

  it('keeps mobile navigation usable without stretching the viewport', () => {
    expect(appCss).toContain('@media (max-width: 640px)')
    expect(normalizedAppCss).toContain('.app-shell {\n    overflow-x: hidden;')
    expect(normalizedAppCss).toContain('.nav-list {\n    display: flex;\n    width: 100%;\n    max-width: 100%;\n    overflow-x: auto;')
    expect(normalizedAppCss).toContain('.nav-item {\n    flex: 0 0 160px;\n    min-width: 160px;')
    expect(normalizedAppCss).toContain('.operation-list {\n    overflow-x: hidden;')
  })

  it('allows long table cell text to shrink inside its grid column', () => {
    expect(normalizedAppCss).toContain('.nav-item {\n  display: flex;\n  width: 100%;\n  min-width: 0;')
    expect(normalizedAppCss).toContain('.user-table-row > *,\n.operation-row > * {\n  min-width: 0;')
  })

  it('keeps tall dialogs scrollable inside the viewport', () => {
    expect(normalizedAppCss).toContain('.modal-backdrop {\n  position: fixed;\n  inset: 0;\n  z-index: 20;\n  display: grid;\n  place-items: center;\n  overflow-y: auto;')
    expect(normalizedAppCss).toContain('.detail-dialog {\n  width: min(560px, 100%);\n  max-height: min(860px, calc(100dvh - 48px));\n  overflow-y: auto;')
    expect(appCss).toContain('box-sizing: border-box;')
    expect(appCss).toContain('overscroll-behavior: contain;')
    expect(appCss).toContain('scrollbar-gutter: stable;')
    expect(appCss).toContain('overflow-wrap: anywhere;')
    expect(normalizedAppCss).toContain('.detail-dialog-header {\n  position: sticky;')
    expect(normalizedAppCss).toContain('.detail-dialog-actions {\n  position: sticky;')
  })

  it('keeps the tariff threshold action at the left edge of its table cell', () => {
    expect(normalizedAppCss).toContain('.tariffs-add-threshold-button {\n  justify-self: start;')
  })

  it('keeps audit controls and event details readable at every supported width', () => {
    expect(normalizedAppCss).toContain('.select-control__trigger,\n.localized-date-picker input {\n  width: 100%;')
    expect(normalizedAppCss).toContain('.select-control__list {\n  position: absolute;')
    expect(normalizedAppCss).toContain('.localized-date-picker__popover {\n  position: absolute;')
    expect(normalizedAppCss).toContain('.audit-detail-dialog {\n  width: min(1120px, calc(100vw - 48px));')
    expect(normalizedAppCss).toContain('.audit-detail-grid {\n  grid-template-columns: repeat(3, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('@media (max-width: 980px) {\n  .audit-detail-grid {\n    grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('@media (max-width: 640px) {\n  .audit-detail-grid {\n    grid-template-columns: 1fr;')
  })

  it('keeps shared dialogs usable on narrow screens without action overlap', () => {
    expect(appCss).toContain('@media (max-width: 640px)')
    expect(normalizedAppCss).toContain('.modal-backdrop {\n    align-items: start;\n    padding: 12px;')
    expect(normalizedAppCss).toContain('.detail-dialog {\n    width: 100%;\n    max-height: calc(100dvh - 24px);\n    padding: 14px;')
    expect(normalizedAppCss).toContain('.detail-dialog-header {\n    top: -14px;\n    margin: -14px -14px 14px;\n    padding: 14px;')
    expect(normalizedAppCss).toContain('.detail-dialog-actions {\n    bottom: -14px;\n    display: grid;\n    grid-template-columns: 1fr;')
    expect(normalizedAppCss).toContain('.detail-dialog-actions button {\n    width: 100%;')
  })

  it('keeps garage multi-selection compact and responsive', () => {
    expect(normalizedAppCss).toContain('.payments-prototype-heading {\n  display: grid;\n  grid-template-columns: minmax(320px, 1fr) auto;')
    expect(normalizedAppCss).toContain('.payments-prototype-garage-summary {\n  display: grid;\n  grid-template-columns: repeat(4, minmax(112px, 142px));')
    expect(normalizedAppCss).toContain('.payments-prototype-search-results {\n  position: absolute;')
    expect(appCss).toContain('grid-template-columns: repeat(3, minmax(190px, 1fr));')
    expect(normalizedAppCss).toContain('.payments-prototype-topline {\n  display: grid;\n  gap: 10px;\n  width: 100%;')
    expect(normalizedAppCss).toContain('.payments-prototype-search {\n  display: flex;\n  align-items: center;\n  width: min(680px, 100%);')
    expect(normalizedAppCss).toContain('.payments-prototype-selected-list {\n  display: flex;\n  flex-wrap: wrap;')
    expect(normalizedAppCss).toContain('.payments-prototype-selected-item {\n  position: relative;\n  display: grid;\n  grid-template-columns: minmax(0, 1fr) 30px;\n  align-items: center;\n  width: 100%;\n  max-width: 220px;\n  flex: 0 1 220px;')
    expect(normalizedAppCss).toContain('.payments-prototype-garage-summary-value {\n  justify-self: end;\n  text-align: right;')
    expect(normalizedAppCss).toContain('.payments-prototype-owner-row .payments-prototype-actions {\n  display: grid;\n  grid-template-columns: repeat(auto-fit, minmax(170px, 1fr));')
    expect(normalizedAppCss).toContain('.payments-prototype-garage-summary {\n    grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('@media (max-width: 1180px) {\n  .payments-prototype-heading {\n    grid-template-columns: minmax(0, 1fr);')
    expect(normalizedAppCss).toContain('.payments-prototype-search-results {\n    grid-template-columns: repeat(2, minmax(190px, 1fr));')
    expect(normalizedAppCss).toContain('.payments-prototype-search-results {\n    grid-template-columns: minmax(0, 1fr);\n    width: calc(100vw - 32px);')
    expect(normalizedAppCss).toContain('.payments-prototype-selected-item {\n    max-width: none;\n    flex-basis: 100%;')
  })

  it('keeps the yearly meter table inside the desktop workspace', () => {
    expect(normalizedAppCss).toContain('.workspace--meter-readings {\n  display: flex;\n  height: 100vh;')
    expect(normalizedAppCss).toContain('.meter-readings-page {\n  display: flex;\n  min-height: 0;\n  flex: 1 1 auto;')
    expect(normalizedAppCss).toContain('.meter-readings-table-shell {\n  min-height: 0;\n  flex: 1 1 0;\n  overflow: auto;\n  max-height: none;')
    expect(normalizedAppCss).toContain('.meter-readings-controls .form-field {\n  display: flex;\n  align-items: center;')
    expect(normalizedAppCss).toContain('.meter-readings-data-row > span:not(:first-child) {\n  display: block;\n  min-height: 32px;')
  })

  it('keeps contractor pagination visible while the directory table scrolls', () => {
    expect(normalizedAppCss).toContain('.workspace--contractors {\n  display: flex;\n  height: 100dvh;\n  min-height: 0;\n  flex-direction: column;\n  overflow: hidden;\n  box-sizing: border-box;')
    expect(normalizedAppCss).toContain('.workspace--contractors > .contractors-page--directory {\n  display: flex;\n  min-height: 0;\n  flex: 1 1 auto;\n  flex-direction: column;')
    expect(normalizedAppCss).toContain('.contractors-page--directory > .contractors-directory-card > .contractors-directory-table {\n  min-height: 0;\n  flex: 1 1 auto;\n  overflow: auto;')
    expect(normalizedAppCss).toContain('.contractors-page--directory > .contractors-directory-card > .dictionary-pagination {\n  flex: 0 0 auto;')
  })

  it('keeps settings navigation full-height and settings forms compact', () => {
    expect(normalizedAppCss).toContain('.settings-layout {\n  display: grid;\n  grid-template-columns: 240px minmax(0, 1fr);\n  gap: 18px;\n  min-height: calc(100dvh - 210px);')
    expect(normalizedAppCss).toContain('.settings-section-nav {\n  position: sticky;\n  top: 18px;\n  display: grid;\n  min-height: 100%;')
    expect(normalizedAppCss).toContain('.settings-card {\n  width: min(980px, 100%);')
    expect(normalizedAppCss).toContain('.settings-card--security {\n  grid-template-columns: minmax(220px, 0.55fr) minmax(440px, 1fr);')
    expect(normalizedAppCss).toContain('.settings-display-switch > span:first-child {\n  display: grid;')
    expect(normalizedAppCss).toContain('.settings-layout {\n    grid-template-columns: 1fr;\n    min-height: 0;')
  })

  it('keeps the garage editor wide, compact and responsive', () => {
    expect(normalizedAppCss).toContain('.detail-dialog.contractors-dialog--garage {\n  width: min(1120px, calc(100vw - 48px));')
    expect(normalizedAppCss).toContain('.contractors-dialog--garage .contractors-modal-form {\n  gap: 10px;')
    expect(normalizedAppCss).toContain('.contractors-garage-form-details,\n.contractors-garage-form-notes {\n  display: grid;\n  grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.contractors-garage-form-notes textarea {\n  min-height: 64px;')
    expect(normalizedAppCss).toContain('.contractors-garage-form-columns,\n  .contractors-garage-form-details,\n  .contractors-garage-form-notes {\n    grid-template-columns: 1fr;')
  })

  it('keeps the supplier editor wide, compact and responsive', () => {
    expect(normalizedAppCss).toContain('.detail-dialog.contractors-dialog--supplier {\n  width: min(1280px, calc(100vw - 48px));')
    expect(normalizedAppCss).toContain('.contractors-dialog--supplier .contractors-modal-form {\n  gap: 9px;')
    expect(normalizedAppCss).toContain('.contractors-supplier-lookup-grid {\n  grid-template-columns: minmax(180px, 0.65fr) minmax(180px, 0.65fr) minmax(320px, 1.7fr);')
    expect(normalizedAppCss).toContain('.contractors-supplier-footer-grid {\n  grid-template-columns: minmax(180px, 0.7fr) minmax(220px, 0.8fr) minmax(320px, 1.5fr);')
    expect(normalizedAppCss).toContain('.contractors-contacts-preview--editable {\n  min-height: 196px;\n  max-height: 280px;')
    expect(normalizedAppCss).toContain('.contractors-supplier-primary-grid,\n  .contractors-supplier-lookup-grid,\n  .contractors-supplier-footer-grid,\n  .contractors-staff-fields {\n    grid-template-columns: 1fr;')
  })

  it('keeps staff rate in the right column and submit actions at the right edge', () => {
    expect(normalizedAppCss).toContain('.contractors-staff-fields {\n  display: grid;\n  grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('.contractors-dialog-submit-actions {\n  display: inline-flex;\n  align-items: center;\n  gap: 8px;\n  margin-left: auto;')
    expect(normalizedAppCss).toContain('.contractors-supplier-footer-grid,\n  .contractors-staff-fields {\n    grid-template-columns: 1fr;')
  })

  it('centers fund action columns', () => {
    expect(normalizedAppCss).toContain('.funds-table .funds-table-action-column {\n  text-align: center;')
  })

  it('lays out release notes as an adaptive card grid', () => {
    expect(normalizedAppCss).toContain('.release-list {\n  display: grid;\n  grid-template-columns: repeat(3, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('@media (max-width: 1280px) {\n  .release-list {\n    grid-template-columns: repeat(2, minmax(0, 1fr));')
    expect(normalizedAppCss).toContain('@media (max-width: 720px) {\n  .release-list {\n    grid-template-columns: minmax(0, 1fr);')
  })
})
