# Agent Instructions

## Project Context

GarageBalance is a web application for financial accounting in a garage-building cooperative. The target stack is C# / ASP.NET Core controllers, React, PostgreSQL, and Docker-ready deployment.

The product must support local installation on one PC and later web/VPS deployment. Do not assume that the system is only desktop or only cloud: keep configuration portable.

## Development Order

Follow the agreed project order unless the user explicitly changes priorities:

1. Users, authentication, roles, permissions, and audit history.
2. Dictionaries matching the old Access database and the updated technical assignment.
3. Encryption and protected handling of personal, financial, and import data.
4. Import from the old Access database without manual re-entry.
5. Payments, accruals, meters, tariffs, balances, and reports.
6. Stage 2 integrations: 1C Fresh synchronization and receipt/check printing.
7. Docker packaging, deployment documentation, and final acceptance.

## Standard Task Workflow

When the user sends a task from chat, screenshots, photos, attached images, or a combination of chat text and images, first study all visible text and customer notes. Treat chat comments above the images as part of the task. If the same request appears more than once, check whether it is already fully implemented; if it is complete and verified, do not duplicate it, otherwise finish or improve the existing work.

After every task received through chat, clean up everything created only for that task before sending the final response. This applies to implementation, analysis, diagnosis, documentation, failed checks, interrupted runs, and tasks that end without code changes. Stop every task-owned process and helper, remove temporary files, logs, screenshots, previews, generated inspection output, scratch directories, caches, and other disposable artifacts, then perform a final command-line process and artifact audit. Never leave something running or stored merely because the task failed or was interrupted. Do not stop user-started or shared Codex/MCP/IDE services. A process or artifact may remain only when the user explicitly requested it, it is a deliverable, or it is required for continued operation; record every such retained item and its purpose in the final response.

For implementation tasks, proceed end to end unless the user explicitly asks only for analysis or a plan:

- clarify only when the missing answer cannot be discovered and guessing would be risky;
- implement the requested behavior completely, including backend, frontend, data, documentation, and deployment pieces that are in scope;
- add or update tests for every developed element, workflow, form, dialog, endpoint, permission branch, validation state, and important edge case so the behavior is protected from regressions;
- run relevant backend/frontend tests, builds, lint, formatting, privacy checks, and encoding checks;
- after local verification, stop every temporary process you started for testing or development, including frontend dev/preview servers, backend API hosts, database containers, background workers, file watchers, browser automation helpers, and one-off smoke-test servers;
- before the final response and before committing, perform an explicit process audit by command line for workspace-related `dotnet`, `testhost`, `node`, Vitest, Vite, Playwright, database, watcher, and dev-server processes; stop every process started for the task, run `dotnet build-server shutdown` when .NET checks were used, and verify that no task-owned process remains; do not stop shared Codex/MCP servers, language servers, IDE processes, or user-started services, and record any intentionally retained process with its purpose in the final response;
- clean up temporary artifacts created during the work, such as generated previews, screenshots, scratch exports, copied customer files, temporary reports, migration scripts used only for inspection, logs, caches, and local analysis folders; keep an artifact only when the user explicitly asked for it, it is intentionally part of the deliverable, or it is required for a committed test fixture/documentation example;
- check local database behavior whenever a local PostgreSQL/database environment is available; if it is unavailable, state that honestly in the roadmap history and final response, and use the closest safe substitute such as migration SQL generation, EF/integration tests, or the VPS test environment when appropriate;
- update the relevant active roadmap status and `История выполнения` with what was done, why, how it was checked, and what remains; if there is no active roadmap outside `docs/archive/`, skip this step and do not create a replacement roadmap unless the user asks for one;
- add an end-user "Что нового" entry when the change is visible to cooperative staff/admins or changes business rules, permissions, integrations, data handling, reports, imports, or visible defects;
- if no release note is required because the change is infrastructure-only or documentation-only, leave the release file unchanged and mention the reason when useful;
- after successful verification of an implementation task, make logical commits with Russian commit messages unless the user explicitly asked not to commit yet; never treat a commit as permission to push;
- do not push until the user explicitly asks for push.

When the user asks to check whether the last task is done, inspect the current code, active docs, tests, active roadmap when one exists, and release notes before deciding. Archived roadmaps must not be used to expand or resume the task unless the user explicitly orders work from the archive.

## Roadmaps

Active roadmaps live directly in `docs/` as human-readable Markdown documents. Before starting implementation that the user explicitly tied to an active roadmap, open that roadmap and update statuses as the work progresses.

Everything under `docs/archive/` is frozen archive material. Do not edit, move, rename, reformat, update statuses or history, calculate it as active progress, implement its checklist items, or use it to broaden a chat task. Do not change production code or tests merely to satisfy an archived roadmap. Existing tests may read the frozen snapshot only for historical regression evidence; a failure involving archived evidence does not authorize changing the archive or implementing an archived item. Reading an archived file is allowed only when the user explicitly asks to inspect or execute that archive, or when a current task needs a historical fact that cannot be obtained from active sources. Work from an archived roadmap may resume only after the user explicitly names the archived roadmap or item and orders its execution. Until then, archived `[ ]`, `[~]`, `[!]`, `[decision]`, and `[acceptance]` markers are historical records, not pending work.

Use these status markers consistently:

- `[ ]` not started.
- `[~]` in progress.
- `[x]` completed and verified.
- `[!]` blocked; write the blocking reason on the same line.
- `[decision]` requires a user/business decision.
- `[acceptance]` requires manual acceptance on real data or environment.

Every roadmap must include:

- source materials considered;
- assumptions and decisions;
- milestones;
- detailed checkable tasks;
- definition of done;
- open risks and questions;
- a bottom section named `История выполнения`.

When the user asks to create or prepare a roadmap, make it a full execution roadmap, not a short plan. It must be checkable by the same status markers, broken into backend, frontend, data/migrations, tests, documentation, deployment, and acceptance work where relevant. If the roadmap is large or spans several modules, create a separate `docs/*-roadmap.md` file and add a reference or history entry in the main project roadmap. Future agents must be able to open the roadmap, continue from the current statuses, and update each item as work is completed.

Always update `История выполнения` in the relevant active roadmap when you complete, verify, defer, or unblock meaningful roadmap work. If no active roadmap exists, do not update archived history and do not create a roadmap solely to log the task.

When reporting an active roadmap status to the user, include a percentage summary: how many checkable items are completed, in progress, not started, blocked, waiting for decisions, and waiting for acceptance. Calculate the percentage only from the active roadmap checklist, excluding `docs/archive/` and the `История выполнения` log entries so archived and historical records do not inflate progress.

Do not mark a roadmap item as `[x]` until the related code, tests, encoding checks, database checks, and documentation updates required by that item are complete.

## Release Notes: "Что нового"

When implementing a user-facing mechanism, changing business rules, permissions, integrations, data handling, reports, imports, or fixing a visible defect, prepare an end-user release note for the "Что нового" module.

Release notes must be written for cooperative staff and administrators, not for developers. Describe what is available, improved, fixed, or important, where the user will see it, and how it affects daily work. Avoid file names, commits, internal architecture, deployment steps, and vague phrases like "minor fixes".

Use the release item types:

- `new`: new functionality.
- `improved`: improved existing behavior.
- `fixed`: corrected bugs or display issues.
- `important`: user-facing rules or caveats that affect work.

Store source release entries in `backend/GarageBalance.Api/AppReleases/releases.json`. Later the backend should sync this file into PostgreSQL on startup and allow admins to manage release notes in the UI.

If a release note cannot be added directly, include the proposed text in the final response and in the roadmap history.

## Security And Data

Treat personal, payment, contract, and Access import data as sensitive. Do not copy passport data, full bank details, real addresses, phone numbers, or raw legacy database contents into public docs, tests, fixtures, or release notes unless the user explicitly asks and the file is intentionally private.

Secrets must not be committed. Use environment variables, `.env.example`, user secrets, or deployment secrets. Keep real `.env`, database dumps, `.accdb`, `.mdb`, backups, and private import files out of Git.

Do not weaken the root `.gitignore` rules for secrets, Access files, database dumps, backups, and private import folders. Before committing security/data-handling changes, run the backend test that verifies these ignore rules and keep it green together with the full test suite.

Authentication and authorization must be enforced on the backend, not only hidden in the UI. Financial data, reports, import tools, settings, and audit logs require explicit permissions.

All changes to financial values, tariffs, permissions, imports, manual corrections, and integrations must create audit history.

## Backend Guidelines

Prefer established ASP.NET Core patterns:

- controllers as the default API surface; do not add minimal API endpoints for business functionality unless the user explicitly approves an exception;
- DTOs at API boundaries;
- EF Core migrations for PostgreSQL schema changes; production backend code must not call `EnsureCreated` / `EnsureDeleted` or run raw schema DDL outside `Infrastructure/Data/Migrations`;
- decimal money values, explicit rounding rules, and date/time handling with clear local business dates;
- cancellation tokens for I/O;
- structured logs without sensitive values.

Use this backend layering unless a roadmap decision changes it:

- `Controllers`: HTTP surface only, authorization, DTO binding, response codes.
- `Application`: use cases, orchestration, transactions, permission checks that depend on business context.
- `Domain`: tariff, accrual, payment, balance, import, and reporting rules that can be tested without ASP.NET.
- `Infrastructure`: EF Core, PostgreSQL, file import readers, external integrations, printers, 1C Fresh clients.
- `Contracts`: request/response DTOs and API-facing validation models when a feature grows beyond a single controller.

Keep business calculations testable outside controllers. Add unit tests for tariff, accrual, meter, debt, import mapping, and permission logic.

Every controller action must have tests for the successful path, validation errors, permission denial, and important edge cases. Do not leave generated sample controllers, unused endpoints, or untested public methods in the project. If a method is not worth testing, it is probably not worth keeping as public surface.

Controller code must stay thin: validate the request, call application/business services, map DTOs, and return clear HTTP results. Business rules belong in services that are easy to test directly.
Controllers must not depend on `Infrastructure`, `GarageBalanceDbContext`, repositories, EF Core APIs, or raw SQL/DDL directly, and controller actions must not expose `Domain` entities as API responses. Keep this enforced by controller architecture tests when new HTTP endpoints are added.

Backend tests must include:

- unit tests for domain calculations and validators;
- controller tests for routes, status codes, DTO shape, validation, and authorization;
- integration tests for EF Core mappings, PostgreSQL-specific queries, migrations, and transactions;
- import tests for Access mapping, idempotency, malformed data, duplicates, and rollback behavior;
- report tests for filters, totals, date ranges, permissions, and export contents.

Do not add unbounded `ToList`, `Include`, or in-memory grouping in financial/reporting flows without a written reason in the roadmap. Queries that can grow with historical payments must be paginated, filtered, or aggregated in PostgreSQL.

## Frontend Guidelines

Build a pleasant but operational interface: compact, calm, readable, and suitable for repeated accounting work. The interface may borrow the comfortable clarity of ChatGPT, but the product must feel like a working accounting system, not a landing page.

Use icons for navigation and actions where appropriate. Keep dense tables readable, make filters obvious, and avoid decorative layouts that reduce useful working space.

Use one shared visual pattern for all single-value comboboxes/selects. Controls must have the same height, border, radius, typography, spacing, chevron, hover/focus, error and disabled states as the rest of the form; a later CSS rule must not reset the shared chevron or other select styling. Every combobox must have an accessible label and keyboard-friendly native or shared accessible behavior. Do not introduce an isolated browser-default or one-off combobox style in a feature form.

For important flows, design loading, empty, error, validation, and permission-denied states from the start.

Use one loading pattern throughout the application. Initial screen, form, card, and table loads must render a shared skeleton that follows the final content shape and keeps the layout stable; do not show a bare `Загрузка...` / `Загружаем...` paragraph in place of content. Use a compact progress indicator inside the affected button only for short submit/save actions. Loading UI must expose a concise `role="status"` announcement with `aria-live="polite"`, keep decorative skeleton bars hidden from assistive technologies, honor `prefers-reduced-motion`, prevent conflicting actions, and never show an empty-state message until loading has finished. Empty table states must use the shared empty-state presentation with comfortable vertical padding instead of an unstyled hint directly under the header.

React functionality must be covered by tests. Add component, hook, service, and integration-style tests for visible behavior, validation, permissions, filters, tables, reports, dialogs, imports, and error states. Any user-facing change should include or update frontend tests unless it is documentation-only.

Do not merge untested UI paths for money, permissions, imports, reports, or data editing. Use stable selectors or accessible roles/names in tests so the tests describe the user's real workflow.

Keep UI business decisions out of components. Components should render state and call hooks/services; calculations for money, tariffs, balances, filters, and import summaries must live in tested utility or domain modules shared by the feature.

Frontend tests must include:

- component tests for forms, tables, dialogs, and visible states;
- hook/service tests for API interaction, retries, validation mapping, and caching behavior;
- workflow tests for login, permissions, dictionary editing, payment entry, report filtering, import dry-run, and "Что нового";
- accessibility-oriented assertions using roles, names, labels, and keyboard-reachable controls.

### Unified table pagination

All large working tables must use the shared `TablePagination` and `PageNavigator` components. Do not create feature-specific pagination markup, native page-size selects, or separate Previous/Next controls.

- Keep the page-size buttons `10`, `25`, `50`, and `100` together with numbered page navigation on the left side of the pagination bar.
- Show the current page, total pages, total result count, and visible range in the status area on the right.
- Use the same spacing, control sizes, focus states, disabled states, responsive stacking, accessible navigation label, page-size group label, and numbered button names in every table.
- Page-size controls must have stable dimensions and must never overlap their labels, values, or navigation buttons.
- When a compact table genuinely needs pagination, reuse the same component instead of inventing a smaller visual variant.

### Unified context menus

All right-click menus must use the shared `context-menu` structure and the same compact visual language throughout the application. Do not create feature-specific menu spacing, separators, action order, or verbose labels.

- Wrap related commands in `context-menu-group`; a menu with one logical category uses one group without a decorative separator.
- Keep record-management commands such as edit and delete/archive/restore in one group. Style destructive actions consistently and retain confirmation where the business flow requires it.
- Put navigation, report, history, export, or other secondary commands in a separate group and divide categories with an accessible `role="separator"` element using the shared `context-menu-separator` class.
- Use concise visible labels such as `Финансовый отчет` instead of `Открыть финансовый отчет`; keep a full verb phrase in `aria-label` or `title` when it improves accessibility.
- Preserve the shared icons, keyboard navigation, focus restoration, disabled states, hover/focus states, and menu-item accessible names in every section.

## Performance

The system must feel fast on the customer's real cooperative data. Design lists, search, reports, imports, and dashboards with performance in mind from the beginning.

Backend performance rules:

- push filtering, sorting, pagination, and aggregation to PostgreSQL where practical;
- add indexes for lookup fields such as garage number, owner name, supplier, month, date, payment type, and import identifiers;
- avoid loading entire tables into memory for reports, search, or balances;
- keep production list/query endpoints bounded before materialization; if a provider-specific fallback must materialize first for tests, keep the fallback explicitly scoped and keep the production branch server-limited;
- keep import jobs resumable and observable instead of blocking the UI;
- add performance-oriented tests or diagnostics for heavy calculations and report queries once real data volume is known.

Frontend performance rules:

- avoid rendering huge tables without pagination or virtualization;
- debounce search inputs and keep filters predictable;
- avoid unnecessary global re-renders in financial tables;
- keep bundle growth visible and review new dependencies before adding them.

Initial performance gates for feature acceptance:

- common search suggestions should respond without visible lag on realistic cooperative data;
- main tables must use server pagination or virtualization before real data can exceed a few hundred rows;
- report endpoints must return filtered periods through indexed PostgreSQL queries, not full-table scans in application memory;
- import must show progress and never freeze the browser during parsing or server processing;
- any dependency that materially increases bundle size must be justified in the roadmap history.

## Docker And Deployment

Docker is planned for the end of the project, but keep it ready from the beginning. Update Dockerfiles and `docker-compose.yml` when backend ports, frontend build steps, database settings, or environment variables change.

The app must support:

- local development without Docker;
- local Docker Compose with PostgreSQL;
- future VPS deployment behind a domain;
- local-only installation on the customer's PC.

## Git Preparation

The project is prepared for Git, but do not publish, push, or create a remote repository unless the user explicitly asks. Keep commits scoped when Git is initialized later.

Before any future push/deployment, verify tests, builds, migrations, Docker configuration, and release notes.

When the user explicitly asks to push:

- ensure the intended commits are present and scoped;
- run or confirm the required checks before push;
- push to the requested branch;
- wait for GitHub Actions to finish;
- if GitHub Actions fails, inspect the failing job/logs, fix the problem, commit the fix, push again, and repeat until the workflow passes or a real external blocker is found;
- when the workflow deploys to VPS, verify the deployed service after Actions completes, including `garagebalance-staging.service`, nginx configuration, `/health`, frontend response, and any task-specific smoke checks;
- report the commit, workflow result, deploy result, and any remaining risks.
