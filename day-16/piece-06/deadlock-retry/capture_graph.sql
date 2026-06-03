-- Extract most recent deadlock graph from the always-on system_health XE session
SET QUOTED_IDENTIFIER ON;
SET NOCOUNT ON;
WITH x AS (
  SELECT CAST(t.target_data AS xml) AS td
  FROM sys.dm_xe_session_targets t
  JOIN sys.dm_xe_sessions s ON s.address = t.event_session_address
  WHERE s.name = N'system_health' AND t.target_name = N'ring_buffer')
SELECT TOP (1)
       n.value('@timestamp','datetime2') AS event_time,
       n.query('.') AS deadlock_graph
FROM x
CROSS APPLY x.td.nodes('//RingBufferTarget/event[@name="xml_deadlock_report"]') AS q(n)
ORDER BY n.value('@timestamp','datetime2') DESC;
