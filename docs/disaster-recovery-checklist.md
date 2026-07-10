# Checklist: recovery after failed import or update

This checklist is used when an Access import, migration, deployment, or version update fails after touching real GarageBalance data. It does not replace a live restore-check: production recovery is accepted only after PostgreSQL, `psql`, `pg_dump`, and `pg_restore` are available on the target machine.

## 1. Stop the risky operation

- [ ] Stop new imports, payments, accrual generation, report exports, and deployment actions.
- [ ] Record the current commit, release version, operator, time, and failed action.
- [ ] Save application logs without secrets, raw Access data, passport data, bank details, or full financial tables.
- [ ] Do not manually delete rows from PostgreSQL.
- [ ] Do not run a second import or migration attempt before the recovery path is chosen.

## 2. Preserve evidence and backups

- [ ] Locate the latest pre-import or pre-update `.pgdump`.
- [ ] Keep the previous backup until user acceptance of the recovered state.
- [ ] If PostgreSQL is still reachable and the current broken state may be useful for analysis, create a separate diagnostic backup first:

```powershell
.\infrastructure\scripts\backup-postgres.ps1 `
  -Database garagebalance_local `
  -HostName 127.0.0.1 `
  -Port 5432 `
  -Username garagebalance_local `
  -BackupDirectory C:\GarageBalance\Backups
```

- [ ] Generate or keep the idempotent migration SQL used by the failed update:

```powershell
.\infrastructure\scripts\generate-migration-script.ps1 `
  -OutputPath artifacts\deploy-migrations.sql
```

## 3. Restore-check before production restore

Always restore the selected backup into a check database first.

```powershell
.\infrastructure\scripts\check-local-postgres.ps1 `
  -Database garagebalance_local `
  -HostName 127.0.0.1 `
  -Port 5432 `
  -Username garagebalance_local `
  -RequirePsql

.\infrastructure\scripts\restore-postgres.ps1 `
  -BackupFile C:\GarageBalance\Backups\garagebalance_local-20260624_091500.pgdump `
  -TargetDatabase garagebalance_restore_check `
  -DropAndCreate
```

- [ ] Confirm the restore target is `garagebalance_restore_check`.
- [ ] Confirm `restore-postgres.ps1` was not run with `-AllowProductionTarget`.
- [ ] Check that the restored database accepts a connection.
- [ ] Compare key counts or smoke checks before deciding whether production restore is required.

## 4. Failed Access import

- [ ] Keep the import run id, dry-run report, quarantine list, run log, and audit entries.
- [ ] If the import failed before applying rows, cancel the apply request and record the reason.
- [ ] If rows may have been written, use the pre-import backup as the rollback source.
- [ ] Run restore-check into `garagebalance_restore_check` before touching the production database.
- [ ] Verify dictionaries, garages, owners, payments, accruals, balances, reports, audit, and "Что нового" on the restored check database.
- [ ] Restore into the production database only after a written decision from the responsible administrator.

## 5. Failed update or migration

- [ ] Keep the failed release directory, migration SQL, service logs, and health-check result.
- [ ] Roll back the application binaries to the previous release before changing data again.
- [ ] Run restore-check for the pre-update backup.
- [ ] If schema migrations were partially applied and cannot be safely rolled forward, restore the pre-update backup.
- [ ] Never edit EF migration history manually unless a separate recovery plan is written and reviewed.
- [ ] After rollback, check `/health`, login, users, dictionaries, payments, reports, imports, audit, and "Что нового".

## 6. Production restore gate

Restoring into `garagebalance_local`, `garagebalance`, or `garagebalance_staging` is destructive. The default restore script blocks these targets.

- [ ] Confirm there is a fresh diagnostic backup of the broken state, if PostgreSQL is reachable.
- [ ] Confirm the selected good backup has passed restore-check.
- [ ] Confirm the responsible administrator approved production restore.
- [ ] Run `restore-postgres.ps1` with `-AllowProductionTarget` only after the previous checks are recorded.
- [ ] Immediately run the smoke checks and write the result into roadmap history or the operations log.

## 7. Checks after recovery

- [ ] `curl -fsS http://127.0.0.1:5080/health` or the VPS/domain health endpoint succeeds.
- [ ] Admin login works.
- [ ] Main dictionaries open.
- [ ] Payments and accruals open without console/API errors.
- [ ] Reports open for the last accepted month.
- [ ] Import panel shows the expected previous state.
- [ ] Audit history contains recovery notes or the operator records the recovery in the operations log.
- [ ] No `.pgdump`, `.accdb`, `.env`, raw import folder, or private diagnostic archive is committed to Git.

## Current local limitation

On the current development machine the live restore-check cannot be completed until local PostgreSQL and command-line clients are available: `postgresTcp=False`, `psql=False`, `docker=False`. Until then, agents may verify scripts, documentation, generated idempotent SQL, tests, JSON, encoding, and package privacy, but must not mark the live recovery check as complete.
