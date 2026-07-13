# Receipt Printing Device Acceptance Checklist

This checklist is used only after the receipt printing scenario, responsible party, mandatory requisites, and device or print method are approved. It must not contain real fiscal secrets, personal document data, bank details, raw fiscal payloads, or production customer records.

## Source Context

- `docs/archive/project-roadmap.md`: Stage 10 acceptance item `Проверить печать на реальном или тестовом устройстве`.
- `docs/receipt-printing-design.md`: target adapter boundary, statuses, audit events, and frontend expectations.
- `docs/receipt-printing-scenario-decision-template.md`: selected internal, fiscal, or mixed scenario.
- `docs/receipt-printing-equipment-decision-template.md`: selected device, local printer, browser/PDF print, emulator, or external service.
- `docs/receipt-printing-obligation-decision-template.md`: responsible party and operation scope.
- `docs/receipt-printing-requisites-decision-template.md`: mandatory printed fields and data minimization.

## Preconditions

- [ ] Scenario decision is approved: internal receipt, fiscal receipt, or mixed flow.
- [ ] Responsible party and printable operation scope are approved.
- [ ] Mandatory requisites and data minimization rules are approved.
- [ ] Device, local printer, browser/PDF method, emulator, or external service is selected.
- [ ] Test environment is isolated from production fiscalization unless real fiscal acceptance is explicitly planned.
- [ ] Secrets are configured through environment/deployment secrets or protected settings, not committed files.
- [ ] Test users have the required permissions and no broader production access than needed.
- [ ] Rollback and retry procedure is agreed before the first print attempt.

## Safe Test Data

- Use synthetic owner names, garage numbers, amounts, accounting months, and receipt identifiers when possible.
- Do not print passport data, full addresses, phone numbers, full bank details, or raw Access import rows.
- If real production data must be used for acceptance, record only sanitized evidence in Git-facing docs.
- Store screenshots, photos, fiscal reports, or raw device logs outside Git unless the user explicitly marks them as private project artifacts.

## Device Or Method Setup

- [ ] Connection type recorded: USB, COM, network, local printer, browser/PDF, emulator, or external API.
- [ ] Driver, service, browser, or adapter version recorded outside source code when it may contain local paths or secrets.
- [ ] Device availability failure is tested and shown as a safe user-facing error.
- [ ] Retry behavior after temporary device failure is tested.
- [ ] Long device response or timeout does not freeze the UI.
- [ ] Adapter logs do not contain tokens, fiscal secrets, raw QR payloads, passport data, full addresses, or phone numbers.

## Internal Receipt Smoke

Run this section only when the internal receipt path is selected.

- [ ] Primary print or PDF/browser print succeeds for an allowed payment.
- [ ] Receipt contains only approved mandatory requisites.
- [ ] Amount, accounting month, garage number, payer, operation type, and document identifier are correct.
- [ ] Copy/reprint is marked with `КОПИЯ` and cannot be confused with the primary receipt.
- [ ] Cancelled payments, payouts, and excluded operations are not printed as primary receipts.
- [ ] User without the required permission cannot print or reprint.
- [ ] Audit history records print request, result status, copy flag, reason where applicable, and safe adapter message.

## Fiscal Receipt Smoke

Run this section only when the fiscal path is selected.

- [ ] Primary fiscal print succeeds in the approved test or real fiscal mode.
- [ ] Required fiscal fields, QR code, and fiscal identifiers appear according to the selected device/method.
- [ ] Tax/VAT/payment-object settings match the approved business decision.
- [ ] Fiscal error is mapped to a safe user-facing status without leaking protected payloads.
- [ ] Retry, duplicate protection, and external receipt identifier behavior are verified.
- [ ] Copy or repeat print follows the approved legal/device rules and is marked clearly.
- [ ] Device reports or fiscal evidence are stored outside Git unless explicitly approved as private artifacts.

## Frontend Acceptance

- [ ] Print actions are visible only where the selected scenario allows them.
- [ ] Loading, success, unavailable device, validation error, permission denied, and retry states are visible and accessible.
- [ ] Dialog focus, Escape, confirmation, and reason/comment flows work for print, cancel, and reprint.
- [ ] UI text does not claim fiscal success before the adapter/device confirms it.
- [ ] Browser console has no new errors during the acceptance flow.

## Backend Acceptance

- [ ] Endpoint authorization matches the approved permission model.
- [ ] Adapter request contains only approved fields and no over-broad personal data.
- [ ] Statuses are bounded to the documented set: `pending_adapter`, `printed`, `device_error`, `template_error`, `fiscalization_error`.
- [ ] Audit events are written through `IAuditEventWriter`.
- [ ] Logs are structured and sanitized.
- [ ] No schema migration is required unless the selected adapter needs durable new fields.

## Evidence To Record In Roadmap

- Date and environment of the acceptance run.
- Selected scenario and device/method.
- Commands, backend/frontend tests, build, bundle check, and migration check results.
- Whether local PostgreSQL, Docker, device/emulator, and browser console checks were available.
- Sanitized result of primary print, copy/reprint, error path, permission denial, and audit verification.
- Remaining risks, manual acceptance notes, or reason why the item stays `[acceptance]`.

## Close Conditions

The roadmap item `Проверить печать на реальном или тестовом устройстве` can be closed as `[x]` only after:

- selected scenario, equipment/method, operation scope, and requisites are approved;
- selected backend/frontend implementation and tests are complete;
- acceptance has been run on the selected real device, emulator, local printer, browser/PDF flow, or approved external test service;
- primary print, copy/reprint, error path, permission denial, audit, logs, and UI console are verified;
- evidence is recorded without secrets or sensitive personal data.
