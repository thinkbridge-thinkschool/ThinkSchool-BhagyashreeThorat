-- Day-11 perf lab — pull the actual SQL the API emitted, from Query Store.
-- Azure SQL has Query Store ON by default, so after you load-test the slow
-- endpoint this shows exactly which statements ran, how many times, and how
-- expensive each was. This is the "capture the SQL it emits" deliverable,
-- straight from the server (no app logs needed).

SELECT TOP 25
    qt.query_sql_text,
    rs.count_executions,
    CAST(rs.avg_duration / 1000.0 AS decimal(10,2))   AS avg_duration_ms,
    CAST(rs.avg_logical_io_reads AS bigint)           AS avg_logical_reads,
    rs.last_execution_time
FROM sys.query_store_query AS q
JOIN sys.query_store_query_text AS qt ON q.query_text_id = qt.query_text_id
JOIN sys.query_store_plan AS p        ON q.query_id = p.query_id
JOIN sys.query_store_runtime_stats AS rs ON p.plan_id = rs.plan_id
WHERE qt.query_sql_text LIKE '%Quotes%'
ORDER BY rs.avg_logical_io_reads DESC;

-- Tip: the slow run shows ~200 executions of the per-author SELECT, each with
-- high logical reads (the scan). After the covering index, the same statement
-- shows far fewer reads and a different (seek) plan_id.
