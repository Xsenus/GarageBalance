# Access Import Frontend Tests Checklist

Этот checklist фиксирует, какие React/frontend-тесты импорта Access уже есть и что блокирует закрытие roadmap-пункта как `[x]`.

Сейчас покрыты dry-run, отчет, apply/cancel request, created records и quarantine UI. Расширенный мастер фактического переноса остается заблокирован, пока нет live transfer flow и reconciliation UI.

## Что Уже Покрыто

- [x] `App.test.tsx` covers choosing `.accdb` and running Access dry-run.
- [x] `App.test.tsx` covers checks, warning/error counters, Russian statuses, summary, history and run log.
- [x] `App.test.tsx` covers apply/cancel request dialogs with reason and backup confirmation.
- [x] `App.test.tsx` covers created records loading, visible rows and empty state in the import panel.
- [x] `App.test.tsx` covers quarantine list, refresh and resolve behavior.
- [x] `importApi.test.ts` covers dry-run, report download, apply, apply cancel, created records and quarantine endpoints.

## Что Блокирует Полное Закрытие

- [ ] Live transfer UI exists beyond `pending_access_reader`.
- [ ] Reconciliation report endpoint/UI exists.
- [ ] Frontend can show transfer progress, status and final result from an actual transfer run.
- [ ] Frontend tests cover imported/skipped/quarantined/decision-required counts after transfer.
- [ ] Frontend tests cover duplicate repeat-run result without creating extra target records.
- [ ] Frontend tests cover recovery after transfer errors.
- [ ] Frontend tests prove raw sensitive Access rows are not rendered in errors, quarantine summaries, created records or report previews.
- [ ] Manual acceptance is run on a private Access copy.

## Required Future Frontend Test Groups

- [ ] Transfer wizard confirmation, disabled states, progress, cancellation and retry.
- [ ] Transfer result screen with imported, skipped, quarantined and duplicate counters.
- [ ] Reconciliation summary with safe report download and no raw personal/payment rows.
- [ ] Permission-denied states for apply, rollback, quarantine resolve and report download.
- [ ] Error states for failed transfer, unavailable Access reader, failed reconciliation and failed report download.
- [ ] Repeat-run UX when idempotency skips already imported rows.

## Когда Roadmap-Пункт Можно Закрыть

- [ ] Guard `AccessImportFrontendTestsRoadmapItemRemainsBlockedUntilTransferWizardAndReconciliationUiExist` can be removed only after transfer wizard/reconciliation UI/tests exist.
- [ ] Roadmap line can move from `[!]` to `[x]` only after frontend tests, backend checks, local DB/migration checks and privacy checks pass.
