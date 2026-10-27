-- Run after all numbered schema scripts as the migration/DB administrator.
-- Create the actual login/user separately using the deployment secret store.
-- Example after creating [ameen_api]: ALTER ROLE [ameen_runtime] ADD MEMBER [ameen_api];
IF DATABASE_PRINCIPAL_ID(N'ameen_runtime') IS NULL
    CREATE ROLE [ameen_runtime];
GRANT SELECT, INSERT, UPDATE ON SCHEMA::dbo TO [ameen_runtime];
-- Draft replacement removes old lines; other entities are cancelled/disabled, never deleted.
GRANT DELETE ON OBJECT::dbo.JournalLines TO [ameen_runtime];
GRANT DELETE ON OBJECT::dbo.RecurringJournalLines TO [ameen_runtime];
GRANT DELETE ON OBJECT::dbo.ItemUnits TO [ameen_runtime];
GRANT DELETE ON OBJECT::dbo.WarehouseLocations TO [ameen_runtime];
GRANT DELETE ON OBJECT::dbo.InventoryOpeningLines TO [ameen_runtime];
DENY UPDATE, DELETE ON OBJECT::dbo.AuditEvents TO [ameen_runtime];
DENY UPDATE, DELETE ON OBJECT::dbo.IdempotencyRecords TO [ameen_runtime];
DENY UPDATE, DELETE ON OBJECT::dbo.ExchangeRates TO [ameen_runtime];
DENY UPDATE, DELETE ON OBJECT::dbo.RecurringJournalGenerations TO [ameen_runtime];
DENY INSERT, UPDATE, DELETE ON OBJECT::dbo.__AmeenSchemaVersions TO [ameen_runtime];
-- No CREATE/ALTER/DROP, db_owner, or server administrator permission is needed by the API.

DENY UPDATE, DELETE ON OBJECT::dbo.StockMovements TO [ameen_runtime];

DENY UPDATE, DELETE ON OBJECT::dbo.DocumentNumberAllocations TO [ameen_runtime];
