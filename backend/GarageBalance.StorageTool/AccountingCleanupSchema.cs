using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GarageBalance.Api.Application.Maintenance;
using GarageBalance.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GarageBalance.StorageTool;

// Reads the cleanup source and refuses to work when a foreign key to one of the
// cleaned tables is not in the reviewed list: a new dependency could otherwise be
// deleted by cascade or block the cleanup halfway.
internal static class AccountingCleanupSchema
{
    public static async Task<AccountingCleanupSource> ReadAsync(GarageBalanceDbContext db,
        IReadOnlyDictionary<string, string> reviewedLinks, CancellationToken cancellationToken)
    {
        var targets = reviewedLinks.Keys.Select(key => key[(key.IndexOf("->", StringComparison.Ordinal) + 2)..]).Distinct().ToArray();
        if (!db.Database.IsNpgsql())
        {
            var links = db.Model.GetEntityTypes().SelectMany(entity => entity.GetForeignKeys())
                .Where(key => targets.Contains(key.PrincipalEntityType.GetTableName()))
                .Select(key => $"public.{key.DeclaringEntityType.GetTableName()}.{string.Join(',', key.Properties.Select(property => property.Name))}->{key.PrincipalEntityType.GetTableName()}")
                .Order(StringComparer.Ordinal).ToArray();
            if (links.Length != reviewedLinks.Count || links.Any(link => !reviewedLinks.ContainsKey(link)))
                throw Error();
            return new(Environment.MachineName, "sqlite-test", 0, db.Database.GetDbConnection().Database,
                db.Database.GetDbConnection().GetHashCode().ToString(CultureInfo.InvariantCulture), Hash(string.Join('|', links)));
        }

        var connection = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database))
            throw new AccountingCleanupException("cleanup_source_invalid", "Нужны однозначные узел и имя базы данных.");
        var profile = await db.Database.SqlQuery<DatabaseProfile>($"""
            SELECT oid::bigint AS "DatabaseOid", pg_postmaster_start_time() AS "ServerStartedAt"
            FROM pg_database WHERE datname = current_database()
            """).SingleAsync(cancellationToken);
        var rows = await db.Database.SqlQuery<DatabaseLink>($"""
            SELECT namespace.nspname || '.' || relation.relname AS "TableName", target.relname AS "TargetName",
                constraint_row.confdeltype::text AS "DeleteRule", pg_get_constraintdef(constraint_row.oid) AS "Definition",
                array_to_string(ARRAY(SELECT attribute.attname FROM unnest(constraint_row.conkey) WITH ORDINALITY AS key(number, position)
                    JOIN pg_attribute AS attribute ON attribute.attrelid = constraint_row.conrelid AND attribute.attnum = key.number ORDER BY key.position), ',') AS "Columns"
            FROM pg_constraint AS constraint_row
            JOIN pg_class AS relation ON relation.oid = constraint_row.conrelid
            JOIN pg_namespace AS namespace ON namespace.oid = relation.relnamespace
            JOIN pg_class AS target ON target.oid = constraint_row.confrelid
            WHERE constraint_row.contype = 'f' AND target.relname = ANY({targets})
            """).ToListAsync(cancellationToken);
        if (rows.Count != reviewedLinks.Count || rows.Any(row =>
                !reviewedLinks.TryGetValue($"{row.TableName}.{row.Columns}->{row.TargetName}", out var rule) || rule != row.DeleteRule))
            throw Error();
        var migrations = await db.Database.GetAppliedMigrationsAsync(cancellationToken);
        var schema = Hash(string.Join('\n', rows.Select(row => $"{row.TableName}|{row.TargetName}|{row.Definition}").Order(StringComparer.Ordinal)
            .Concat(migrations.Order(StringComparer.Ordinal))));
        return new(Environment.MachineName, connection.Host, connection.Port, connection.Database!,
            $"{profile.DatabaseOid}:{profile.ServerStartedAt.ToUniversalTime():O}", schema);
    }

    public static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private static AccountingCleanupException Error() =>
        new("cleanup_schema_unreviewed", "В схеме есть непроверенная связь с очищаемыми данными. Удаление запрещено.");

    private sealed class DatabaseProfile
    {
        public long DatabaseOid { get; set; }
        public DateTimeOffset ServerStartedAt { get; set; }
    }

    private sealed class DatabaseLink
    {
        public string TableName { get; set; } = "";
        public string TargetName { get; set; } = "";
        public string DeleteRule { get; set; } = "";
        public string Definition { get; set; } = "";
        public string Columns { get; set; } = "";
    }
}
