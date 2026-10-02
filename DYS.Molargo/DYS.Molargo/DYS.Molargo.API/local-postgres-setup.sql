-- Creates the local development database and the role the API connects as.
--
-- Run once, as a superuser, from this folder. Pass the password you want the role to have —
-- it must match the one in appsettings.Development.json:
--
--     psql -U postgres -v pw=devpassword -f local-postgres-setup.sql
--
-- psql will then ask for the *postgres* password you chose when PostgreSQL was installed.
-- That one belongs to you and appears nowhere in this repository.
--
-- The role password is passed in rather than written here, so changing it does not mean
-- editing a committed file — and so this script does not become a second place that has to
-- be kept in step with the settings.

\if :{?pw}
\else
  \echo 'Pass the role password, matching appsettings.Development.json:'
  \echo '    psql -U postgres -v pw=yourpassword -f local-postgres-setup.sql'
  \quit
\endif

-- A separate role rather than connecting as the superuser. The API has no business being
-- able to drop other databases, and a development setup that grants it anyway is the habit
-- that gets copied to a server.
--
-- Created if missing, and its password set either way, so re-running this is how a
-- changed password in appsettings.Development.json is applied.
SELECT format(
    CASE WHEN EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'molargo')
         THEN 'ALTER ROLE molargo LOGIN PASSWORD %L'
         ELSE 'CREATE ROLE molargo LOGIN PASSWORD %L'
    END, :'pw')
\gexec

-- Owned by that role, so `dotnet ef database update` can create tables without any further
-- grants. CREATE DATABASE is not transactional, so it cannot go in a DO block.
SELECT 'CREATE DATABASE molargo OWNER molargo'
WHERE NOT EXISTS (SELECT 1 FROM pg_database WHERE datname = 'molargo')
\gexec

\echo ''
\echo 'Done. Next:'
\echo '  dotnet ef database update --project DYS.Molargo.API'
\echo '  dotnet run --project DYS.Molargo.API -- seed --demo'
\echo ''
