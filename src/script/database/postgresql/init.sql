-- This script provisions a fresh PostgreSQL server for Ninlil. Run it once, connected as a
-- superuser (for example, via `psql -U postgres -f init.sql`), before ever starting the
-- application against this server.
--
-- Everything Entity Framework Core needs beyond this point -- every table, column, and index --
-- is created automatically by Ninlil's own migrations the first time the application starts.

-- Change this password before running the script against anything but a local, throwaway
-- development server.
\set application_password 'change-me-before-first-run'

-- Create the login the application itself will connect as. It deliberately cannot create or drop
-- other databases or roles; it only needs to read and write inside its own database.
CREATE ROLE ninlil
    WITH LOGIN
    PASSWORD :'application_password';

-- Create the database the application stores everything in, owned by the login above so that
-- Entity Framework Core's migrations can freely create tables inside it.
CREATE DATABASE ninlil
    WITH OWNER = ninlil
    ENCODING = 'UTF8';

-- Grant the login every privilege it needs on its own database.
GRANT ALL PRIVILEGES ON DATABASE ninlil TO ninlil;
