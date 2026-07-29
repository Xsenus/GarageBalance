\pset pager off
\pset null '—'

SELECT EXISTS (
    SELECT 1
    FROM pg_extension
    WHERE extname = 'pg_stat_statements'
) AS pg_stat_statements_available
\gset

\if :pg_stat_statements_available
\echo 'GarageBalance PostgreSQL: pg_stat_statements metadata'
SELECT
    stats_reset,
    dealloc,
    stats_reset < clock_timestamp() - interval '24 hours' AS collected_over_24_hours
FROM pg_stat_statements_info;

\echo 'GarageBalance PostgreSQL: top statements by total execution time'
SELECT
    statements.queryid,
    database.datname AS database_name,
    statements.calls,
    statements.rows,
    round(statements.total_exec_time::numeric, 2) AS total_exec_ms,
    round(statements.mean_exec_time::numeric, 2) AS mean_exec_ms,
    round(statements.max_exec_time::numeric, 2) AS max_exec_ms,
    statements.shared_blks_read,
    statements.shared_blks_hit,
    statements.temp_blks_read,
    statements.temp_blks_written
FROM pg_stat_statements AS statements
JOIN pg_database AS database ON database.oid = statements.dbid
WHERE database.datname = current_database()
ORDER BY statements.total_exec_time DESC
LIMIT 25;

\echo 'GarageBalance PostgreSQL: repeated slow statements by mean execution time'
SELECT
    statements.queryid,
    database.datname AS database_name,
    statements.calls,
    statements.rows,
    round(statements.total_exec_time::numeric, 2) AS total_exec_ms,
    round(statements.mean_exec_time::numeric, 2) AS mean_exec_ms,
    round(statements.max_exec_time::numeric, 2) AS max_exec_ms
FROM pg_stat_statements AS statements
JOIN pg_database AS database ON database.oid = statements.dbid
WHERE database.datname = current_database()
  AND statements.calls >= 5
ORDER BY statements.mean_exec_time DESC
LIMIT 25;

\echo 'GarageBalance PostgreSQL: top statements by maximum execution time'
SELECT
    statements.queryid,
    database.datname AS database_name,
    statements.calls,
    statements.rows,
    round(statements.total_exec_time::numeric, 2) AS total_exec_ms,
    round(statements.mean_exec_time::numeric, 2) AS mean_exec_ms,
    round(statements.max_exec_time::numeric, 2) AS max_exec_ms
FROM pg_stat_statements AS statements
JOIN pg_database AS database ON database.oid = statements.dbid
WHERE database.datname = current_database()
ORDER BY statements.max_exec_time DESC
LIMIT 25;
\else
\echo 'pg_stat_statements is not installed in the current database; no statistics were read.'
\endif
