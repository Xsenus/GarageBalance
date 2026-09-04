namespace GarageBalance.Api.Tests.Deployment;

public sealed class GitHubActionsDeploymentTests
{
    [Fact]
    public void StagingWorkflowVerifiesBuildsPackagesAndDeploysThroughRestrictedServerScript()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File
            .ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "deploy-staging.yml"))
            .ReplaceLineEndings("\n");

        Assert.Contains("branches:", workflow, StringComparison.Ordinal);
        Assert.Contains("- master", workflow, StringComparison.Ordinal);
        Assert.Contains("backend:\n", workflow, StringComparison.Ordinal);
        Assert.Contains("frontend:\n", workflow, StringComparison.Ordinal);
        Assert.Contains("backend-quality:\n", workflow, StringComparison.Ordinal);
        Assert.Contains("frontend-audit:\n", workflow, StringComparison.Ordinal);
        Assert.Contains("deploy:\n", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test GarageBalance.slnx --configuration Release --no-restore", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet format GarageBalance.slnx --verify-no-changes --no-restore", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet list GarageBalance.slnx package --vulnerable --include-transitive", workflow, StringComparison.Ordinal);
        Assert.Contains("./infrastructure/scripts/verify-package-privacy.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("npm ci --prefer-offline --no-audit", workflow, StringComparison.Ordinal);
        Assert.Contains("npm audit --package-lock-only --audit-level=high", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run test", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run lint", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run build", workflow, StringComparison.Ordinal);
        Assert.Contains("npm run check:bundle", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet tool run dotnet-ef migrations script --idempotent", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet publish", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/api.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/frontend.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("artifacts/operations.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/download-artifact@v8", workflow, StringComparison.Ordinal);
        Assert.Contains("garagebalance-staging-backend-${{ github.sha }}", workflow, StringComparison.Ordinal);
        Assert.Contains("garagebalance-staging-frontend-${{ github.sha }}", workflow, StringComparison.Ordinal);
        Assert.Contains("infrastructure/scripts/audit-staging-database.sh", workflow, StringComparison.Ordinal);
        Assert.Contains("secrets.VPS_SSH_KEY", workflow, StringComparison.Ordinal);
        Assert.Contains("Host garagebalance-staging", workflow, StringComparison.Ordinal);
        Assert.Contains("ControlMaster auto", workflow, StringComparison.Ordinal);
        Assert.Contains("ControlPersist 120", workflow, StringComparison.Ordinal);
        Assert.Contains("garagebalance-staging:~/uploads/${RELEASE_ID}/api.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("garagebalance-staging:~/uploads/${RELEASE_ID}/operations.tar.gz", workflow, StringComparison.Ordinal);
        Assert.Contains("ssh -O exit garagebalance-staging", workflow, StringComparison.Ordinal);
        Assert.Contains("sudo /usr/local/bin/garagebalance-deploy-apply", workflow, StringComparison.Ordinal);
        Assert.Contains("curl --http2 --compressed --fail", workflow, StringComparison.Ordinal);
        Assert.Contains("--connect-timeout 10 --max-time 20", workflow, StringComparison.Ordinal);
        Assert.Contains("https://sgk.blagodaty.ru${ENTRY_ASSET}?deployment=${GITHUB_SHA}-${ATTEMPT}", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void StagingWorkflowRunsIndependentGatesInParallelAndCancelsOnlySupersededVerification()
    {
        var workflow = File
            .ReadAllText(Path.Combine(FindRepositoryRoot(), ".github", "workflows", "deploy-staging.yml"))
            .ReplaceLineEndings("\n");

        foreach (var gate in new[] { "backend", "frontend", "backend-quality", "frontend-audit" })
        {
            Assert.Contains($"group: garagebalance-staging-${{{{ github.ref }}}}-{gate}", workflow, StringComparison.Ordinal);
        }

        Assert.Equal(4, CountOccurrences(workflow, "cancel-in-progress: true"));
        Assert.Contains("group: garagebalance-staging-deploy", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", workflow, StringComparison.Ordinal);
        Assert.Contains("needs:\n      - backend\n      - frontend\n      - backend-quality\n      - frontend-audit", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryAwareTestsSupportRegularCheckoutsAndGitWorktrees()
    {
        var testRoot = Path.Combine(FindRepositoryRoot(), "backend", "GarageBalance.Api.Tests");
        var repositoryAwareSources = Directory
            .EnumerateFiles(testRoot, "*.cs", SearchOption.AllDirectories)
            .Select(path => new { Path = path, Source = File.ReadAllText(path) })
            .Where(file => file.Source.Contains("FindRepositoryRoot", StringComparison.Ordinal) &&
                           file.Source.Contains("\".git\"", StringComparison.Ordinal));

        foreach (var file in repositoryAwareSources)
        {
            Assert.Contains("Directory.Exists", file.Source, StringComparison.Ordinal);
            Assert.Contains("File.Exists", file.Source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void VpsApplyReleaseScriptCreatesBackupAppliesMigrationsChecksHealthAndKeepsRollback()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "vps-apply-release.sh"));

        Assert.Contains("pg_dump --format=custom", script, StringComparison.Ordinal);
        Assert.Contains("chown \"${APP_USER}:${APP_GROUP}\" \"$BACKUP_FILE\"", script, StringComparison.Ordinal);
        Assert.Contains("garagebalance_restore_check_${TIMESTAMP//-/}_$$", script, StringComparison.Ordinal);
        Assert.Contains("pg_restore \\", script, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(script, "< \"$BACKUP_FILE\" >/dev/null"));
        Assert.DoesNotContain("\"$BACKUP_FILE\" >/dev/null", script.Replace("< \"$BACKUP_FILE\" >/dev/null", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("--exit-on-error", script, StringComparison.Ordinal);
        Assert.Contains("restored_table_count", script, StringComparison.Ordinal);
        Assert.Contains("cleanup_restore_check", script, StringComparison.Ordinal);
        Assert.Contains("restoreCheckStatus=completed", script, StringComparison.Ordinal);
        Assert.Contains("psql --set ON_ERROR_STOP=1", script, StringComparison.Ordinal);
        Assert.Contains("systemctl stop \"$SERVICE_NAME\"", script, StringComparison.Ordinal);
        Assert.Contains("systemctl start \"$SERVICE_NAME\"", script, StringComparison.Ordinal);
        Assert.Contains("restore_previous_release", script, StringComparison.Ordinal);
        Assert.Contains("DATABASE_MUTATION_STARTED=1", script, StringComparison.Ordinal);
        Assert.Contains("databaseRollbackStatus=started", script, StringComparison.Ordinal);
        Assert.Contains("pg_terminate_backend", script, StringComparison.Ordinal);
        Assert.Contains("--clean", script, StringComparison.Ordinal);
        Assert.Contains("--if-exists", script, StringComparison.Ordinal);
        Assert.Contains("databaseRollbackStatus=completed", script, StringComparison.Ordinal);
        Assert.Contains("curl -fsS -H \"Host: ${PUBLIC_HOST}\"", script, StringComparison.Ordinal);
        Assert.Contains("curl -fsSk -H \"Host: ${PUBLIC_HOST}\" \"https://127.0.0.1/health/ready\"", script, StringComparison.Ordinal);
        Assert.Contains("deployStatus=ok", script, StringComparison.Ordinal);
        Assert.Contains("garagebalance_${TIMESTAMP}_${release_id}.pgdump", script, StringComparison.Ordinal);
        Assert.Contains("FRONTEND_ASSET_RETENTION_DAYS=30", script, StringComparison.Ordinal);
        Assert.Contains("cp -a -n \"${APP_ROOT}/frontend/assets/.\" \"${NEXT_FRONTEND}/assets/\"", script, StringComparison.Ordinal);
        Assert.Contains("-mtime \"+${FRONTEND_ASSET_RETENTION_DAYS}\" -delete", script, StringComparison.Ordinal);
        Assert.Contains("frontend_entry_assets", script, StringComparison.Ordinal);
        Assert.Contains("frontend entry asset was not found or empty", script, StringComparison.Ordinal);
        Assert.Contains("https://127.0.0.1${asset_path}", script, StringComparison.Ordinal);
        Assert.Contains("OPERATIONS_ARCHIVE=\"${UPLOAD_DIR}/operations.tar.gz\"", script, StringComparison.Ordinal);
        Assert.Contains("bash -n", script, StringComparison.Ordinal);
        Assert.Contains("GARAGEBALANCE_DEPLOY_REEXECUTED", script, StringComparison.Ordinal);
        Assert.Contains("cmp --silent \"$0\" \"$packaged_apply_script\"", script, StringComparison.Ordinal);
        Assert.Contains("releasePrepare=reexec-updated-apply-script", script, StringComparison.Ordinal);
        Assert.Contains("exec bash \"$REEXEC_APPLY_SCRIPT\" \"$release_id\"", script, StringComparison.Ordinal);
        Assert.Contains("bash \"${OPERATIONS_DIR}/infrastructure/scripts/install-vps-performance-configuration.sh\" \"$OPERATIONS_DIR\"", script, StringComparison.Ordinal);
        Assert.Contains("/usr/local/bin/garagebalance-deploy-apply", script, StringComparison.Ordinal);
        Assert.Contains("audit-database", script, StringComparison.Ordinal);
        Assert.Contains("/usr/local/bin/garagebalance-audit-database", script, StringComparison.Ordinal);
        Assert.Contains("PREVIOUS_RELEASE_RETENTION_COUNT=2", script, StringComparison.Ordinal);
        Assert.Contains("RELEASE_METADATA_RETENTION_COUNT=5", script, StringComparison.Ordinal);
        Assert.Contains("OPERATIONAL_BACKUP_RETENTION_COUNT=30", script, StringComparison.Ordinal);
        Assert.Contains("prune_old_directories \"$APP_ROOT\" \"api.prev-\"", script, StringComparison.Ordinal);
        Assert.Contains("prune_old_directories \"$APP_ROOT\" \"frontend.prev-\"", script, StringComparison.Ordinal);
        Assert.Contains("prune_old_directories \"${APP_ROOT}/releases\" \"\"", script, StringComparison.Ordinal);
        Assert.Contains("-name 'garagebalance_[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]-[0-9][0-9][0-9][0-9][0-9][0-9]_*.pgdump'", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-name 'garagebalance_*.pgdump'", script, StringComparison.Ordinal);
        Assert.Contains("retentionStatus=refused; path=${obsolete_path}", script, StringComparison.Ordinal);
        Assert.Contains("retentionStatus=warning; target=operational-backups", script, StringComparison.Ordinal);
    }

    [Fact]
    public void StagingDatabaseAuditRunsOnlyThroughProtectedServerCommandAndVerifiesHealth()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "audit-staging-database.yml"));

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("AUDIT GARAGEBALANCE STAGING", workflow, StringComparison.Ordinal);
        Assert.Contains("sudo /usr/local/bin/garagebalance-deploy-apply audit-database", workflow, StringComparison.Ordinal);
        Assert.Contains("https://sgk.blagodaty.ru/health/ready", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("psql", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void StagingDatabaseAuditBacksUpRestoresAndQueriesOnlyReadOnlyCopy()
    {
        var repositoryRoot = FindRepositoryRoot();
        var script = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "audit-staging-database.sh"));

        Assert.Contains("pg_dump --format=custom \"$database_name\"", script, StringComparison.Ordinal);
        Assert.Contains("before_integrity_audit.pgdump", script, StringComparison.Ordinal);
        Assert.Contains("garagebalance_integrity_audit_", script, StringComparison.Ordinal);
        Assert.Contains("pg_restore \\", script, StringComparison.Ordinal);
        Assert.Contains("--dbname=\"$audit_database\"", script, StringComparison.Ordinal);
        Assert.Contains("SET default_transaction_read_only = on", script, StringComparison.Ordinal);
        Assert.Contains("duplicate_active_garage_numbers", script, StringComparison.Ordinal);
        Assert.Contains("duplicate_active_quick_list_names", script, StringComparison.Ordinal);
        Assert.Contains("invalid_garage_starting_overdue_debt", script, StringComparison.Ordinal);
        Assert.Contains("overlapping_tariff_periods", script, StringComparison.Ordinal);
        Assert.Contains("duplicate_regular_accruals", script, StringComparison.Ordinal);
        Assert.Contains("missing_regular_accrual_calculation_snapshots", script, StringComparison.Ordinal);
        Assert.Contains("historical_snapshot_amount_mismatches", script, StringComparison.Ordinal);
        Assert.Contains("customer_target_garage_due_date_review", script, StringComparison.Ordinal);
        Assert.Contains("income_operations_without_type_or_allocation_evidence", script, StringComparison.Ordinal);
        Assert.Contains("legacy_income_types_inferred_from_allocations", script, StringComparison.Ordinal);
        Assert.Contains("allocations_to_invalid_operations", script, StringComparison.Ordinal);
        Assert.Contains("allocation_totals_above_accrual", script, StringComparison.Ordinal);
        Assert.Contains("duplicate_active_allocations", script, StringComparison.Ordinal);
        Assert.Contains("allocation_income_type_mismatches", script, StringComparison.Ordinal);
        Assert.Contains("duplicate_active_supplier_accruals", script, StringComparison.Ordinal);
        Assert.Contains("linked_supplier_accrual_mismatches", script, StringComparison.Ordinal);
        Assert.Contains("staff_without_salary_rate_history", script, StringComparison.Ordinal);
        Assert.Contains("duplicate_staff_salary_rate_period_starts", script, StringComparison.Ordinal);
        Assert.Contains("FROM (SELECT \\\"StaffMemberId\\\", \\\"EffectiveFrom\\\" FROM staff_salary_rate_periods GROUP BY \\\"StaffMemberId\\\", \\\"EffectiveFrom\\\" HAVING count(*) > 1) q", script, StringComparison.Ordinal);
        Assert.DoesNotContain("staff_salary_rate_periods a JOIN staff_salary_rate_periods b", script, StringComparison.Ordinal);
        Assert.Contains("overlapping_staff_employment_periods", script, StringComparison.Ordinal);
        Assert.Contains("customer_target_staff_match", script, StringComparison.Ordinal);
        Assert.Contains("invalid_opening_balance_adjustment_targets", script, StringComparison.Ordinal);
        Assert.Contains("negative_cash_or_bank_balance", script, StringComparison.Ordinal);
        Assert.Contains("exact_duplicate_financial_operations", script, StringComparison.Ordinal);
        Assert.Contains("duplicate_meter_readings", script, StringComparison.Ordinal);
        Assert.Contains("invalid_fund_operation_math", script, StringComparison.Ordinal);
        Assert.Contains("fund_operation_chain_breaks", script, StringComparison.Ordinal);
        Assert.Contains("fund_operation_same_timestamp_order", script, StringComparison.Ordinal);
        Assert.Contains("fund_balance_mismatch", script, StringComparison.Ordinal);
        Assert.Contains("unallocated_historical_floor_adjustment", script, StringComparison.Ordinal);
        Assert.Contains("cash_bank_fund_reconciliation_mismatch", script, StringComparison.Ordinal);
        Assert.Contains("codex_marked_business_records", script, StringComparison.Ordinal);
        Assert.DoesNotContain("--dbname=\"$database_name\"", script, StringComparison.Ordinal);
        Assert.Contains("OPERATIONAL_BACKUP_RETENTION_COUNT=30", script, StringComparison.Ordinal);
        Assert.Contains("prune_operational_backups", script, StringComparison.Ordinal);
        Assert.DoesNotContain("-name 'garagebalance_*.pgdump'", script, StringComparison.Ordinal);
        Assert.Contains("retentionStatus=refused", script, StringComparison.Ordinal);
        Assert.Contains("retentionStatus=warning; target=operational-backups", script, StringComparison.Ordinal);
    }

    [Fact]
    public void DockerReleaseWorkflowBlocksPublishingWhenDependencyAuditFails()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "publish-docker-release.yml"));

        Assert.Contains("dotnet list GarageBalance.slnx package --vulnerable --include-transitive", workflow, StringComparison.Ordinal);
        Assert.Contains("npm audit --prefix frontend --audit-level=high", workflow, StringComparison.Ordinal);
        Assert.Contains("needs:", workflow, StringComparison.Ordinal);
        Assert.Contains("- backend", workflow, StringComparison.Ordinal);
        Assert.Contains("- frontend", workflow, StringComparison.Ordinal);
        Assert.Contains("- distribution", workflow, StringComparison.Ordinal);
        Assert.True(
            workflow.IndexOf("backend:", StringComparison.Ordinal) < workflow.IndexOf("publish:", StringComparison.Ordinal) &&
            workflow.IndexOf("frontend:", StringComparison.Ordinal) < workflow.IndexOf("publish:", StringComparison.Ordinal) &&
            workflow.IndexOf("distribution:", StringComparison.Ordinal) < workflow.IndexOf("publish:", StringComparison.Ordinal),
            "Every verification job must complete before Docker images are published.");
    }

    [Fact]
    public void WorkflowsUseNode24CompatibleOfficialActions()
    {
        var repositoryRoot = FindRepositoryRoot();
        var workflows = new[]
        {
            File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "deploy-staging.yml")),
            File.ReadAllText(Path.Combine(repositoryRoot, ".github", "workflows", "publish-docker-release.yml")),
        };

        foreach (var workflow in workflows)
        {
            Assert.Contains("actions/checkout@v6", workflow, StringComparison.Ordinal);
            Assert.Contains("actions/setup-dotnet@v5", workflow, StringComparison.Ordinal);
            Assert.Contains("actions/setup-node@v6", workflow, StringComparison.Ordinal);
            Assert.Contains("actions/upload-artifact@v6", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("actions/checkout@v4", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("actions/setup-dotnet@v4", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("actions/setup-node@v4", workflow, StringComparison.Ordinal);
            Assert.DoesNotContain("actions/upload-artifact@v4", workflow, StringComparison.Ordinal);
        }

        Assert.Contains("actions/download-artifact@v8", workflows[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ShellScripts_KeepLinuxLineEndingsInReleaseArchives()
    {
        var repositoryRoot = FindRepositoryRoot();
        var attributes = File.ReadAllText(Path.Combine(repositoryRoot, ".gitattributes"));

        Assert.Contains("*.sh text eol=lf", attributes, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")) &&
                (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                 File.Exists(Path.Combine(directory.FullName, ".git"))))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }

    private static int CountOccurrences(string value, string fragment)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }

        return count;
    }
}
