// This script provisions a fresh MongoDB server for Ninlil. Run it once, connected as a user
// with the userAdmin (or root) role (for example, via `mongosh --file init.js`), before ever
// starting the application against this server.
//
// Unlike the relational engines, MongoDB has no separate "create the tables" step for Ninlil
// to run later: collections and their indexes are created automatically the first time the
// application writes to them, using the same class maps and conventions registered by
// Ninlil.Dns.Data.MongoDb. This script only needs to create the database (which MongoDB does
// implicitly, the moment something is written into it) and the login the application will use.

// Switch into the database the application stores everything in. MongoDB does not actually
// create the database until the first write happens inside it, which is expected.
db = db.getSiblingDB("ninlil");

// Create the login the application itself will connect as, scoped to read and write only inside
// its own database.
// Change this password before running the script against anything but a local, throwaway
// development server.
db.createUser({
  user: "ninlil",
  pwd: "change-me-before-first-run",
  roles: [{ role: "readWrite", db: "ninlil" }],
});
