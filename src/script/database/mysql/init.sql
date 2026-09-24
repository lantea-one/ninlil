-- This script provisions a fresh MySQL or MariaDB server for Ninlil. Run it once, connected
-- as a user with CREATE USER/CREATE DATABASE/GRANT privileges (for example, via
-- `mysql -u root -p < init.sql`), before ever starting the application against this server.
--
-- Everything Entity Framework Core needs beyond this point -- every table, column, and index --
-- is created automatically by Ninlil's own migrations the first time the application starts.

-- Create the database the application stores everything in.
CREATE DATABASE IF NOT EXISTS ninlil
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

-- Create the login the application itself will connect as.
-- Change this password before running the script against anything but a local, throwaway
-- development server.
CREATE USER IF NOT EXISTS 'ninlil'@'%'
    IDENTIFIED BY 'change-me-before-first-run';

-- Grant the login every privilege it needs, but only on its own database.
GRANT ALL PRIVILEGES ON ninlil.* TO 'ninlil'@'%';

FLUSH PRIVILEGES;
