# Maintenance integration fixture

This executable is **test-only**. It does not issue a production execution permit,
qualify an installed host, release the staging hold, or create annual charges.
The `annual-release` suite calls the actual annual release engine and shared
protocol/recovery implementation, rather than the selective-cleanup engine.

## Prerequisites and isolation

Run on a disposable Linux/systemd CI runner, never a VPS or a machine containing
customer databases. The caller must already have permission to use root for these
isolated tests. The fixture does not configure sudo, install software, change
permissions of production resources, read production environment files, or use SSH.

Required: .NET 10 SDK for publishing, Bash, Python 3, systemd/systemd-run,
util-linux (`runuser`, `flock`), GNU coreutils, and installed PostgreSQL client/server
binaries plus its `postgres` OS account. Set `GB_FIXTURE_PG_BIN` to the installed
`/usr/lib/postgresql/<major>/bin` directory (default: version 18). Ubuntu GitHub
runners may use version 16. Missing prerequisites fail; they are not a skipped pass.

The runner creates its own synthetic Unix-socket-only PostgreSQL cluster using the
installed binaries, not the system PostgreSQL service. CI-managed disposable
clusters are the intended environment. Local machines must follow the repository's
database-creation approval rules before executing it.

- All databases start with `garagebalance_it_`; the C# constructor independently
  checks socket directory, port, database name, username, and disabled pooling.
- Every systemd unit is under a random `gb-maint-integration-<8hex>` prefix.
- API, storage and health participants share an isolated private network namespace.
  Their ports are not host listeners, and their outbound production network is absent.
- Production application, database socket/data directories and environment files are
  inaccessible inside units. Writes are limited to the unique fixture directories.
- The actual participant script and deploy gate preamble are private copies with
  fixture paths/identities substituted. The deploy payload is replaced with a
  failing sentinel, so a broken gate cannot execute any deployment work.
- A fixture host cannot mint the production execution permit; the suite asserts this.
- Background annual accrual generation and outbound integrations are disabled.

## Reproducible build and run

From the repository root on the disposable runner:

```bash
set -Eeuo pipefail
fixture_dir=infrastructure/fixtures/maintenance-integration
dotnet publish "$fixture_dir/MaintenanceIntegrationFixture.csproj" \
  --configuration Release --runtime linux-x64 --self-contained true \
  -p:DefineConstants=MAINTENANCE_FIXTURE \
  --output "$RUNNER_TEMP/maintenance-publish"

package="/opt/gb-maint-integration-package.$(od -An -N4 -tx1 /dev/urandom | tr -d ' \n')"
sudo install -d -m 0700 "$package" "$package/scripts"
sudo cp -a "$RUNNER_TEMP/maintenance-publish" "$package/publish"
sudo cp "$fixture_dir/run-integration-fixture.sh" \
  "$fixture_dir/annual-release-cases.sh" \
  "$fixture_dir/cleanup-integration-fixture.sh" "$package/"
sudo cp infrastructure/scripts/staging-maintenance-barrier.sh \
  infrastructure/scripts/garagebalance-healthcheck.sh \
  infrastructure/scripts/vps-apply-release.sh "$package/scripts/"
sudo chown -R root:root "$package"
sudo bash -c 'cd "$1"; find publish scripts -type f -print0 | sort -z | xargs -0 sha256sum > evidence.sha256;
  sha256sum run-integration-fixture.sh annual-release-cases.sh cleanup-integration-fixture.sh >> evidence.sha256' bash "$package"
expected=$(sudo sha256sum "$package/evidence.sha256" | cut -d' ' -f1)
# Persist these two non-secret values for the CI always() cleanup step.
printf 'MAINTENANCE_PACKAGE=%s\nMAINTENANCE_EVIDENCE=%s\n' "$package" "$expected" >> "$GITHUB_ENV"

sudo env GB_FIXTURE_PG_BIN=/usr/lib/postgresql/16/bin \
  timeout --signal=TERM --kill-after=90s 25m \
  bash "$package/run-integration-fixture.sh" "$package" "$expected" annual-release
```

An independent CI `if: always()` step must run cancellation cleanup even when the
test step fails. Do not delete the package if cleanup cannot verify all processes
stopped. This is idempotent after normal cleanup and uses only the exact root-owned
fixture registry. It refuses unknown identities, symbolic-link roots and unexpected
unit names.

```bash
if [[ -n ${MAINTENANCE_PACKAGE:-} && -n ${MAINTENANCE_EVIDENCE:-} ]]; then
  sudo bash "$MAINTENANCE_PACKAGE/cleanup-integration-fixture.sh" \
    "$MAINTENANCE_PACKAGE" "$MAINTENANCE_EVIDENCE"
  # Remove the reviewed package only after cleanup succeeded and identity is checked.
  [[ "$MAINTENANCE_PACKAGE" =~ ^/opt/gb-maint-integration-package[.][a-f0-9]{8}$ ]]
  [[ $(sudo realpath -e "$MAINTENANCE_PACKAGE") == "$MAINTENANCE_PACKAGE" ]]
  sudo rm -rf --one-file-system -- "$MAINTENANCE_PACKAGE"
fi
```

The default two-argument invocation retains the existing `selective-cleanup` suite.
The two suites are not substitutes for each other. `maintenance-barrier-fixture.sh`
is a separate synthetic participant-only test and also cannot qualify annual SQL.

## Annual assertions

The suite seeds two held years, a fixed annual tariff, garages, financial history,
allocations, baseline meters and funds. It takes a fresh custom-format backup and
restores/verifies it before executing the annual release.

Cases include success; concurrent app/health/storage starts, deploy and release;
lost activity lease before release and immediately before COMMIT; SIGKILL before release, before COMMIT, after COMMIT and after
receipt; marker creation/removal, receipt creation and outcome-read failures;
health-completion failure; altered committed outcome and corrupt receipt.

Assertions verify:

- Only the reviewed year's hold changes, and exactly one annual release audit exists.
- Full-row hashes of financial and reference tables and the other held year remain
  unchanged. No selective cleanup audit appears and release creates no charges.
- Competing participants and retry executions cannot enter the release callback.
- Read-only recovery classifies committed/rolled-back state without calling release
  or creating an additional audit. Known rollback is not treated as permission to release.
- Unknown or corrupt evidence retains the blocking marker, leaves the API offline,
  stops timers and rejects new app/storage/health/deploy starts.
- Normal completion restores the recorded service and timer participants and removes
  operation artifacts. Cleanup verifies no fixture service/cgroup/cluster remains.

The corrupt-receipt case deliberately retains the original marker (failure while
removing it). Arbitrary external corruption of an already-fsynced receipt after the
marker was removed is outside this suite's guarantee. The shared app wrapper permits
receipt-only startup; the annual recovery entrypoint installs a blocking marker on
unresolved evidence, but that does not close a separate start before recovery runs.
Do not represent this fixture as protection against arbitrary filesystem tampering.

## Evidence limits

`bash -n`, Python parsing, and a Windows .NET build are syntax/compile checks only.
They do **not** establish PostgreSQL behavior, `flock`, `fsync`, systemd isolation,
SIGKILL outcomes or production qualification. Claim a Linux pass only after this
exact suite runs successfully and prints all scenario results and verified cleanup.
