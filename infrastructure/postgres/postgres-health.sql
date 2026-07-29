\pset pager off
\pset null '—'

\echo 'GarageBalance PostgreSQL: database statistics'
SELECT
    datname AS database_name,
    pg_size_pretty(pg_database_size(datname)) AS database_size,
    numbackends AS connections,
    round(100.0 * blks_hit / NULLIF(blks_hit + blks_read, 0), 3) AS cache_hit_percent,
    temp_files,
    pg_size_pretty(temp_bytes) AS temp_size,
    deadlocks,
    stats_reset
FROM pg_stat_database
WHERE datname = current_database();

\echo 'GarageBalance PostgreSQL: table maintenance'
WITH settings AS
(
    SELECT
        current_setting('autovacuum_vacuum_threshold')::numeric AS vacuum_threshold,
        current_setting('autovacuum_vacuum_scale_factor')::numeric AS vacuum_scale_factor,
        current_setting('autovacuum_analyze_threshold')::numeric AS analyze_threshold,
        current_setting('autovacuum_analyze_scale_factor')::numeric AS analyze_scale_factor
)
SELECT
    stats.relname AS table_name,
    stats.n_live_tup AS live_rows,
    stats.n_dead_tup AS dead_rows,
    round(100.0 * stats.n_dead_tup / NULLIF(stats.n_live_tup + stats.n_dead_tup, 0), 2) AS dead_percent,
    stats.n_mod_since_analyze,
    stats.n_dead_tup > settings.vacuum_threshold
        + settings.vacuum_scale_factor * stats.n_live_tup AS vacuum_due,
    stats.n_mod_since_analyze > settings.analyze_threshold
        + settings.analyze_scale_factor * stats.n_live_tup AS analyze_due,
    stats.last_autovacuum,
    stats.last_autoanalyze,
    pg_size_pretty(pg_total_relation_size(stats.relid)) AS total_size
FROM pg_stat_user_tables AS stats
CROSS JOIN settings
ORDER BY pg_total_relation_size(stats.relid) DESC;

\echo 'GarageBalance PostgreSQL: totals'
SELECT
    count(*) AS tables,
    sum(n_live_tup)::bigint AS live_rows,
    sum(n_dead_tup)::bigint AS dead_rows,
    round(100.0 * sum(n_dead_tup) / NULLIF(sum(n_live_tup + n_dead_tup), 0), 2) AS dead_percent,
    pg_size_pretty(sum(pg_total_relation_size(relid))) AS total_relation_size
FROM pg_stat_user_tables;

\echo 'GarageBalance PostgreSQL: waits and long transactions'
SELECT
    count(*) FILTER (WHERE wait_event_type = 'Lock') AS waiting_locks,
    count(*) FILTER (WHERE state = 'idle in transaction') AS idle_in_transaction,
    count(*) FILTER (
        WHERE xact_start IS NOT NULL
          AND clock_timestamp() - xact_start > interval '30 seconds'
    ) AS transactions_over_30_seconds,
    count(*) FILTER (
        WHERE query_start IS NOT NULL
          AND state = 'active'
          AND clock_timestamp() - query_start > interval '5 seconds'
    ) AS queries_over_5_seconds
FROM pg_stat_activity
WHERE datname = current_database();

\echo 'GarageBalance PostgreSQL: table-specific autovacuum overrides'
SELECT
    namespace.nspname AS schema_name,
    relation.relname AS table_name,
    relation.reloptions
FROM pg_class AS relation
JOIN pg_namespace AS namespace ON namespace.oid = relation.relnamespace
WHERE namespace.nspname = 'public'
  AND relation.relkind = 'r'
  AND relation.reloptions IS NOT NULL
ORDER BY relation.relname;

\echo 'GarageBalance PostgreSQL: memory and connection settings'
SELECT
    name,
    setting,
    unit,
    source
FROM pg_settings
WHERE name IN (
    'autovacuum_work_mem',
    'effective_cache_size',
    'huge_pages',
    'jit',
    'maintenance_work_mem',
    'max_connections',
    'max_parallel_workers',
    'max_parallel_workers_per_gather',
    'max_worker_processes',
    'shared_buffers',
    'superuser_reserved_connections',
    'temp_buffers',
    'wal_buffers',
    'work_mem'
)
ORDER BY name;

\echo 'GarageBalance PostgreSQL: connection utilization'
WITH limits AS
(
    SELECT
        current_setting('max_connections')::integer AS maximum,
        current_setting('superuser_reserved_connections')::integer AS reserved
)
SELECT
    limits.maximum,
    limits.reserved,
    limits.maximum - limits.reserved AS client_capacity,
    count(*) FILTER (WHERE backend_type = 'client backend') AS clients,
    count(*) FILTER (WHERE state = 'active') AS active,
    count(*) FILTER (WHERE state = 'idle') AS idle,
    count(*) FILTER (WHERE state = 'idle in transaction') AS idle_in_transaction,
    round(
        100.0 * count(*) FILTER (WHERE backend_type = 'client backend')
        / NULLIF(limits.maximum - limits.reserved, 0),
        2
    ) AS client_capacity_percent
FROM pg_stat_activity
CROSS JOIN limits
GROUP BY limits.maximum, limits.reserved;

\echo 'GarageBalance PostgreSQL: client connections by database'
SELECT
    coalesce(datname, 'background') AS database_name,
    coalesce(state, 'background') AS state,
    count(*) AS connections
FROM pg_stat_activity
WHERE backend_type = 'client backend'
GROUP BY datname, state
ORDER BY database_name, state;
