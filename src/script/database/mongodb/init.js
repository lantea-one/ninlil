// Provisions a fresh MongoDB server for Ninlil. Run it once, connected as a user with the
// userAdmin (or root) role (for example, `mongosh --file init.js`), before the application first
// starts against this server.
//
// Input: none; edit the password below first. Output: the "ninlil" login, with readWrite on the
// "ninlil" database. Point NINLIL_STORAGE_CONNECTION_STRING at that database and login.
//
// MongoDB needs no schema step: collections are created the first time the application writes
// to them, using the class maps registered by Ninlil.Dns.Data.MongoDb and
// Ninlil.Dhcp.Data.MongoDb, and the repositories that need indexes (the query log, webhook
// delivery attempts) create them on first use. The database itself appears on the first write.

// Switch into the application's database. MongoDB creates it on the first write inside it.
db = db.getSiblingDB("ninlil");

// Create the login the application connects as, able to read and write only in its database.
// Change this password before running the script against anything but a local, throwaway
// development server.
db.createUser({
  user: "ninlil",
  pwd: "change-me-before-first-run",
  roles: [{ role: "readWrite", db: "ninlil" }],
});
