# Stage 11 Final Acceptance Signoff Template

Use this template only when the customer is ready to accept the final deployment and operations stage or provide a motivated refusal. Do not store secrets, database dumps, Access files, real payment tables, passport data, full addresses, phone numbers, bank details, fiscal payloads, or private VPS credentials in this document.

## Source Context

- `docs/archive/project-roadmap.md`: Stage 11 item `Получить финальную приемку и закрыть этап актом/автоматической приемкой`.
- `docs/local-pc-install-checklist.md`: local PC installation evidence.
- `docs/vps-deployment-checklist.md`: VPS/domain/TLS deployment evidence.
- `docs/postgres-backup-restore.md`: backup, restore-check, and scheduled backup evidence.
- `docs/migration-verification-checklist.md`: clean and post-import migration evidence.
- `docs/disaster-recovery-checklist.md`: failed import and failed update recovery evidence.
- `docs/final-performance-checklist.md`: performance gates and live PostgreSQL checks.
- `backend/GarageBalance.Api/AppReleases/releases.json`: user-facing final readiness note.

## Signoff Metadata

- Date:
- Customer / representative:
- Project representative:
- Environment accepted:
- Deployment mode: local PC / VPS / both / other:
- Database checked:
- Application version or commit:
- Acceptance evidence location outside Git:

## Decision

Choose one:

- [ ] Accepted without motivated remarks.
- [ ] Accepted with remarks already recorded in the roadmap.
- [ ] Not accepted; motivated remarks are listed below.
- [ ] Acceptance postponed; missing evidence is listed below.

## Required Live Evidence

- [ ] Local PostgreSQL or approved deployment database is available and checked.
- [ ] Migrations apply on a clean database.
- [ ] Migrations apply on a post-import or restored database when applicable.
- [ ] Backup is created and restore-check succeeds in a non-production database.
- [ ] Regular local backup is configured and first manual run is verified when local PC deployment is selected.
- [ ] Docker Compose or no-Docker local install smoke is completed when local PC deployment is selected.
- [ ] VPS workflow, service, nginx, TLS, `/health`, and frontend smoke are completed when VPS deployment is selected.
- [ ] Final performance checklist is completed on realistic cooperative data.
- [ ] Access import acceptance is completed or explicitly deferred with reason.
- [ ] 1C Fresh acceptance is completed or explicitly deferred with reason.
- [ ] Receipt printing acceptance is completed or explicitly deferred with reason.
- [ ] Open accounting and ownership decisions are accepted, deferred, or converted to roadmap items.
- [ ] "Что нового" is visible to administrators and describes the acceptance gates honestly.

## Smoke Checklist

- [ ] Login works for approved roles.
- [ ] Users, roles, dictionaries, garages, contractors, tariffs, payments, accruals, reports, import, audit, and "Что нового" open without console errors.
- [ ] Permission-denied states are checked for a limited user.
- [ ] Reports and exports produce expected sanitized results.
- [ ] Browser console has no new errors during the smoke route.
- [ ] Backend logs contain no new errors and no sensitive values.

## Motivated Remarks

| Priority | Area | Remark | Expected result | Owner | Due date | Roadmap item |
| --- | --- | --- | --- | --- | --- | --- |
|  |  |  |  |  |  |  |

## Deferrals

Use this section only when the customer accepts the current stage but explicitly defers a known external dependency.

| Deferred gate | Reason | Risk accepted by customer | Follow-up roadmap item |
| --- | --- | --- | --- |
|  |  |  |  |

## Safe Evidence Rules

- Store screenshots, fiscal evidence, database dumps, Access files, VPS logs, and private reports outside Git unless the user explicitly marks them as private project artifacts.
- In Git-facing docs, record only sanitized summaries, command names, status codes, counts, and non-sensitive timestamps.
- Never paste `.env`, JWT keys, database passwords, SSH keys, fiscal tokens, raw QR payloads, full personal data, or full financial tables.
- If a remark needs real data, describe the symptom with masked identifiers and keep the raw evidence in the agreed private location.

## Close Conditions

The Stage 11 final acceptance item can be marked `[x]` only after:

- the decision section is filled by the customer or authorized representative;
- required live evidence is completed, explicitly deferred, or converted to roadmap items;
- motivated remarks are either absent or recorded as checkable roadmap work;
- local/VPS/database/backup/migration/performance gates relevant to the chosen deployment mode are verified;
- no secrets or sensitive personal, financial, Access import, fiscal, or deployment data are committed.
