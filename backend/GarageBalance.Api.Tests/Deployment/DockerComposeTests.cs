namespace GarageBalance.Api.Tests.Deployment;

public sealed class DockerComposeTests
{
    [Fact]
    public void ComposeDefinesServiceHealthChecksAndStartupOrder()
    {
        var compose = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docker-compose.yml"));

        Assert.Contains("name: garagebalance", compose, StringComparison.Ordinal);
        Assert.Contains("postgres:", compose, StringComparison.Ordinal);
        Assert.Contains("POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?Set POSTGRES_PASSWORD in .env before running docker compose}", compose, StringComparison.Ordinal);
        Assert.Contains("pg_isready -U ${POSTGRES_USER:-garagebalance} -d ${POSTGRES_DB:-garagebalance}", compose, StringComparison.Ordinal);
        Assert.Contains("\"${POSTGRES_BIND_ADDRESS:-127.0.0.1}:${POSTGRES_PORT:-5432}:5432\"", compose, StringComparison.Ordinal);
        Assert.Contains("${BACKUP_HOST_PATH:-./backups}:/backups", compose, StringComparison.Ordinal);
        Assert.Equal(2, CountOccurrences(compose, "${BACKUP_HOST_PATH:-./backups}:/backups"));
        Assert.Contains("postgres-data:/var/lib/postgresql/data", compose, StringComparison.Ordinal);
        Assert.Contains("condition: service_healthy", compose, StringComparison.Ordinal);

        Assert.Contains("api:", compose, StringComparison.Ordinal);
        Assert.Contains("dockerfile: backend/GarageBalance.Api/Dockerfile", compose, StringComparison.Ordinal);
        Assert.Contains("ASPNETCORE_ENVIRONMENT: ${ASPNETCORE_ENVIRONMENT:-Production}", compose, StringComparison.Ordinal);
        Assert.Contains("ASPNETCORE_URLS: http://+:8080", compose, StringComparison.Ordinal);
        Assert.Contains("Jwt__SigningKey: ${JWT_SIGNING_KEY:?Set JWT_SIGNING_KEY in .env before running docker compose}", compose, StringComparison.Ordinal);
        Assert.Contains("DataProtection__KeysPath: ${DATA_PROTECTION_KEYS_PATH:-/var/lib/garagebalance/keys}", compose, StringComparison.Ordinal);
        Assert.Contains("data-protection-keys:${DATA_PROTECTION_KEYS_PATH:-/var/lib/garagebalance/keys}", compose, StringComparison.Ordinal);
        Assert.Contains("Cors__AllowedOrigins__0: ${FRONTEND_ORIGIN:-http://127.0.0.1:5173}", compose, StringComparison.Ordinal);
        Assert.Contains("Database__ApplyMigrationsOnStartup: ${APPLY_MIGRATIONS_ON_STARTUP:-true}", compose, StringComparison.Ordinal);
        Assert.Contains("Database__RequirePreMigrationBackup: ${REQUIRE_PRE_MIGRATION_BACKUP:-true}", compose, StringComparison.Ordinal);
        Assert.Contains("DatabaseBackup__AutomaticEnabled: ${DATABASE_BACKUP_AUTOMATIC_ENABLED:-true}", compose, StringComparison.Ordinal);
        Assert.Contains("DatabaseBackup__RetentionCount: ${DATABASE_BACKUP_RETENTION_COUNT:-30}", compose, StringComparison.Ordinal);
        Assert.Contains("curl -fsS http://127.0.0.1:8080/health > /dev/null", compose, StringComparison.Ordinal);
        Assert.Contains("\"${API_BIND_ADDRESS:-127.0.0.1}:${API_PORT:-5080}:8080\"", compose, StringComparison.Ordinal);

        Assert.Contains("frontend:", compose, StringComparison.Ordinal);
        Assert.Contains("dockerfile: frontend/Dockerfile", compose, StringComparison.Ordinal);
        Assert.Contains("wget -q --spider http://127.0.0.1/", compose, StringComparison.Ordinal);
        Assert.Contains("\"${FRONTEND_BIND_ADDRESS:-127.0.0.1}:${FRONTEND_PORT:-5173}:80\"", compose, StringComparison.Ordinal);
        Assert.Contains("data-protection-keys:", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void DockerImagesContainDatabaseToolsAndSameOriginApiProxy()
    {
        var repositoryRoot = FindRepositoryRoot();
        var apiDockerfile = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Dockerfile"));
        var nginx = File.ReadAllText(Path.Combine(repositoryRoot, "frontend", "nginx.conf"));

        Assert.Contains("postgresql-client", apiDockerfile, StringComparison.Ordinal);
        Assert.Contains("location /api/", nginx, StringComparison.Ordinal);
        Assert.Contains("proxy_pass http://api:8080", nginx, StringComparison.Ordinal);
        Assert.Contains("location = /health", nginx, StringComparison.Ordinal);
    }

    [Fact]
    public void NonDockerConfigurationEnablesPortableBackupsAndVpsKeepsWritableStorage()
    {
        var repositoryRoot = FindRepositoryRoot();
        var appSettings = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "appsettings.json"));
        var deployScript = File.ReadAllText(Path.Combine(repositoryRoot, "infrastructure", "scripts", "vps-apply-release.sh"));

        Assert.Contains("\"DatabaseBackup\"", appSettings, StringComparison.Ordinal);
        Assert.Contains("\"Enabled\": true", appSettings, StringComparison.Ordinal);
        Assert.Contains("\"AutomaticEnabled\": true", appSettings, StringComparison.Ordinal);
        Assert.Contains("\"Directory\": \"auto\"", appSettings, StringComparison.Ordinal);
        Assert.Contains("install -d -o \"${APP_USER}\" -g \"${APP_GROUP}\" -m 750 \"$BACKUP_DIR\"", deployScript, StringComparison.Ordinal);
        Assert.Contains("ensure_env_setting \"DatabaseBackup__Directory\" \"$BACKUP_DIR\"", deployScript, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupBackupRunsBeforeDatabaseMigration()
    {
        var repositoryRoot = FindRepositoryRoot();
        var startupService = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Infrastructure", "Data", "DatabaseStartupHostedService.cs"));
        var program = File.ReadAllText(Path.Combine(repositoryRoot, "backend", "GarageBalance.Api", "Program.cs"));

        var backupPosition = startupService.IndexOf("DatabaseBackupKind.PreUpdate", StringComparison.Ordinal);
        var migrationPosition = startupService.IndexOf("MigrateAsync", StringComparison.Ordinal);
        Assert.True(backupPosition >= 0 && migrationPosition > backupPosition, "Pre-update backup must complete before migrations start.");

        var databaseStartupPosition = program.IndexOf("AddHostedService<DatabaseStartupHostedService>", StringComparison.Ordinal);
        var dependentWorkerPosition = program.IndexOf("AddHostedService<AppReleaseCatalogSynchronizer>", StringComparison.Ordinal);
        Assert.True(databaseStartupPosition >= 0 && dependentWorkerPosition > databaseStartupPosition, "Database startup must be registered before database-dependent workers.");
    }

    [Fact]
    public void BeginnerDockerGuideCoversPersistenceUpdateRestoreAndDestructiveCommands()
    {
        var guide = File.ReadAllText(Path.Combine(FindRepositoryRoot(), "docs", "docker-install-update-guide.md"));

        Assert.Contains("docker compose up -d", guide, StringComparison.Ordinal);
        Assert.Contains("docker compose build --pull", guide, StringComparison.Ordinal);
        Assert.Contains("docker compose down -v", guide, StringComparison.Ordinal);
        Assert.Contains("garagebalance_postgres-data", guide, StringComparison.Ordinal);
        Assert.Contains("BACKUP_HOST_PATH", guide, StringComparison.Ordinal);
        Assert.Contains("Настройки` → `Резервные копии", guide, StringComparison.Ordinal);
        Assert.Contains("pg_restore --list", guide, StringComparison.Ordinal);
        Assert.Contains("garagebalance_restore_check", guide, StringComparison.Ordinal);
        Assert.Contains("data-protection-keys.tar.gz", guide, StringComparison.Ordinal);
        Assert.Contains("персональные и финансовые данные", guide, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(search, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += search.Length;
        }

        return count;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GarageBalance.slnx")) &&
                Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
