-- The staging database's users: run by the owner, connected as the server's Entra admin to the WorldRankGuesser
-- database (portal query editor), twice: after the first main.bicep deploy, and again after the first scraper
-- deploy, when dbo.CurrentCountryRankings exists and the game user's grant on it can be applied. Every statement is
-- idempotent. Production: infra/production/bootstrap.sql. A managed identity's user name is the identity's name.

-- The game schema exists before the game's migrations run, so its grants can be given now; the migration's own
-- CREATE SCHEMA is skipped when the schema exists.
IF SCHEMA_ID(N'game') IS NULL EXEC (N'CREATE SCHEMA [game] AUTHORIZATION dbo;');

-- Migrations run from the GitHub runner as the deploy identity.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'id-wrg-staging-deploy')
    CREATE USER [id-wrg-staging-deploy] FROM EXTERNAL PROVIDER;
ALTER ROLE db_owner ADD MEMBER [id-wrg-staging-deploy];

-- The scraper reads and writes its own schema, dbo; its migrations are the deploy identity's job.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'id-wrg-staging-scraper')
    CREATE USER [id-wrg-staging-scraper] FROM EXTERNAL PROVIDER;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::dbo TO [id-wrg-staging-scraper];

-- The game reads one view (ownership chaining covers the tables beneath it) and owns its data in schema game.
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'id-wrg-staging-game')
    CREATE USER [id-wrg-staging-game] FROM EXTERNAL PROVIDER;
GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::game TO [id-wrg-staging-game];
IF OBJECT_ID(N'dbo.CurrentCountryRankings', N'V') IS NOT NULL
    GRANT SELECT ON OBJECT::dbo.CurrentCountryRankings TO [id-wrg-staging-game];
ELSE
    PRINT 'dbo.CurrentCountryRankings does not exist yet: run this file again after the first scraper deploy.';

SELECT dp.name AS [user], dp.type_desc, r.name AS [role]
FROM sys.database_principals dp
LEFT JOIN sys.database_role_members rm ON rm.member_principal_id = dp.principal_id
LEFT JOIN sys.database_principals r ON r.principal_id = rm.role_principal_id
WHERE dp.name LIKE 'id-wrg-%';
