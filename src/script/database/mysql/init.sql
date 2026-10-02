-- Provisions a fresh MySQL or MariaDB server for Ninlil. Run it once, connected as a user with
-- CREATE USER, CREATE DATABASE and GRANT privileges (for example, `mysql -u root -p < init.sql`),
-- before the application first starts against this server.
--
-- Input: none; edit the password below first. Output: the ninlil database (utf8mb4) and the
-- ninlil login. Every table, column and index is then created by the application's Entity
-- Framework Core migrations, which it applies on every start.

-- Create the database the application stores everything in.
CREATE DATABASE IF NOT EXISTS ninlil
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

-- Create the login the application connects as. '%' accepts it from any host; narrow it to the
-- application's address where you can. Change this password before running the script against
-- anything but a local, throwaway development server.
CREATE USER IF NOT EXISTS 'ninlil'@'%'
    IDENTIFIED BY 'change-me-before-first-run';

-- Grant the login every privilege on its database only; the migrations create and alter tables.
GRANT ALL PRIVILEGES ON ninlil.* TO 'ninlil'@'%';

FLUSH PRIVILEGES;
