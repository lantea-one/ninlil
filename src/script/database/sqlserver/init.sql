-- This script provisions a fresh Microsoft SQL Server instance for Ninlil. Run it once,
-- connected as an administrator (for example, via `sqlcmd -S localhost -U sa -P <password> -i init.sql`),
-- before ever starting the application against this server.
--
-- Everything Entity Framework Core needs beyond this point -- every table, column, and index --
-- is created automatically by Ninlil's own migrations the first time the application starts.

-- Create the database the application stores everything in.
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'ninlil')
BEGIN
    CREATE DATABASE [ninlil];
END
GO

-- Create the login the application itself will connect as.
-- Change this password before running the script against anything but a local, throwaway
-- development server.
IF NOT EXISTS (SELECT name FROM sys.sql_logins WHERE name = N'ninlil')
BEGIN
    CREATE LOGIN [ninlil]
        WITH PASSWORD = N'Change-Me-Before-First-Run1!',
        CHECK_POLICY = ON;
END
GO

-- Grant the login ownership of its own database only.
USE [ninlil];
GO

IF NOT EXISTS (SELECT name FROM sys.database_principals WHERE name = N'ninlil')
BEGIN
    CREATE USER [ninlil] FOR LOGIN [ninlil];
END
GO

ALTER ROLE [db_owner] ADD MEMBER [ninlil];
GO
