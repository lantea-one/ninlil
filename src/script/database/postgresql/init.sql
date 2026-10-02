-- Provisions a fresh PostgreSQL server for Ninlil. Run it once with psql (it uses psql's \set),
-- connected as a superuser (for example, `psql -U postgres -f init.sql`), before the application
-- first starts against this server.
--
-- Input: none; edit the password below first. Output: the ninlil login and the ninlil database
-- (UTF8) it owns. Every table, column and index is then created by the application's Entity
-- Framework Core migrations, which it applies on every start.

-- Change this password before running the script against anything but a local, throwaway
-- development server.
\set application_password 'change-me-before-first-run'

-- Create the login the application connects as. It cannot create databases or roles; it only
-- works inside its database.
CREATE ROLE ninlil
    WITH LOGIN
    PASSWORD :'application_password';

-- Create the database the application stores everything in, owned by the login above so that
-- Entity Framework Core's migrations can freely create tables inside it.
CREATE DATABASE ninlil
    WITH OWNER = ninlil
    ENCODING = 'UTF8';

-- Grant the login every database-level privilege on its database (as owner it holds them already).
GRANT ALL PRIVILEGES ON DATABASE ninlil TO ninlil;
