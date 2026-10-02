-- Provisions a fresh Microsoft SQL Server instance for Ninlil. Run it once, connected as an
-- administrator (for example, `sqlcmd -S localhost -U sa -P <password> -i init.sql`), before the
-- application first starts against this server. It uses sqlcmd's GO batch separator.
--
-- Input: none; edit the password below first. Output: the ninlil database, the ninlil login
-- (with the server's password policy enforced) and a ninlil user in db_owner. Every table,
-- column and index is then created by the application's Entity Framework Core migrations, which
-- it applies on every start. Each step is skipped when its object already exists.

-- Create the database the application stores everything in.
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'ninlil')
BEGIN
    CREATE DATABASE [ninlil];
END
GO

-- Create the login the application connects as.
-- Change this password before running the script against anything but a local, throwaway
-- development server.
IF NOT EXISTS (SELECT name FROM sys.sql_logins WHERE name = N'ninlil')
BEGIN
    CREATE LOGIN [ninlil]
        WITH PASSWORD = N'Change-Me-Before-First-Run1!',
        CHECK_POLICY = ON;
END
GO

-- Map the login to a user in its database and make it db_owner there only, so the migrations
-- can create and alter tables.
USE [ninlil];
GO

IF NOT EXISTS (SELECT name FROM sys.database_principals WHERE name = N'ninlil')
BEGIN
    CREATE USER [ninlil] FOR LOGIN [ninlil];
END
GO

ALTER ROLE [db_owner] ADD MEMBER [ninlil];
GO
