#!/usr/bin/env node
// admin-console-smoke.mjs
//
// What it does
//   A dependency-free smoke test for the Ninlil admin console. It starts a throw-away Ninlil host
//   (SQLite database and runtime settings file in the output directory, free ports, a known
//   administrator), seeds sample data through the REST API, starts headless Chromium, signs in
//   through the login form as the administrator and as two limited users, and visits the console's
//   pages at several widths in the light and the dark theme. Every visit runs these checks:
//     - no uncaught script exception and no console or log error (see ALLOWED_CONSOLE_ERRORS);
//     - every same-origin response is below 400, except the statuses the page lists as expected;
//     - a visible heading (h1, h2 or a card header);
//     - no horizontal overflow (document scrollWidth <= innerWidth + 1);
//     - the network goes idle (no request for 500 ms, bounded by 15 s).
//   It then saves a full-page screenshot. It also records facts a later change may need, such as
//   whether the browser's time zone list holds UTC.
//
//   The host and Chromium are started by this script and stopped by it at the end, also on a
//   failure, on SIGINT and on SIGTERM. Only the process groups it started are signalled.
//
//   Not in CI: a person or an agent runs it by hand after building the console into
//   src/dotnet/src/Ninlil.Web/wwwroot (.forgejo/scripts/build-admin-console.sh).
//
// Usage
//   node src/script/development/admin-console-smoke.mjs --out <directory> [options]
//
// Arguments
//   --out <dir>        Output directory (required). The script creates it, empties its shots/,
//                      chrome/ and data files and writes report.json, report.md, shots/*.png,
//                      host.log and chrome.log there. Use a directory outside the repository.
//   --chrome <path>    Chromium binary. Default: $CHROME_BIN, then chromium, chromium-browser,
//                      google-chrome from PATH.
//   --pages <list>     Comma-separated substrings; only pages whose slug contains one are visited.
//   --widths <list>    Comma-separated viewport widths. Default 1400,900,390 (heights 900, 1000,
//                      844).
//   --themes <list>    light, dark or both. Default light,dark.
//   --users <list>     admin, limited or both. Default admin,limited.
//   --skip-build       Do not run `dotnet build` before starting the host (the host runs with
//                      --no-build either way, so the build output must exist).
//   --list             Print the page slugs and exit (no host, no browser).
//
// Environment
//   CHROME_BIN                  Chromium binary when --chrome is not given.
//   NODE_TLS_REJECT_UNAUTHORIZED  Set to 0 by this script for its own process: the host's HTTPS
//                      certificate is self-signed and the script's API calls (health, sign-in,
//                      seed data) go to it over loopback. Node's fetch honours the variable.
//   Requires Node 22 or later (global fetch and WebSocket) and the `dotnet` SDK on PATH.
//
// Outputs and exit code
//   <out>/report.json and <out>/report.md with every check (failures first), counts and the
//   screenshot index. Exit code 0 when every check passed and at least one page was visited; 1
//   when a check failed or the sweep visited nothing; 2 when the host or browser could not start.

import { spawn, spawnSync } from 'node:child_process';
import crypto from 'node:crypto';
import fs from 'node:fs';
import net from 'node:net';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// The host's certificate is self-signed on loopback. This applies to this process only.
process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0';

const repoRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..', '..');
const webProject = path.join(repoRoot, 'src', 'dotnet', 'src', 'Ninlil.Web');

const ADMIN_USER = 'smokeadmin';
const ADMIN_PASSWORD = 'Smoke-Password-1234!';
const LIMITED_PASSWORD = 'Smoke-Limited-1234!';

/** Viewport heights by width: desktop, tablet and phone. Unknown widths use 900. */
const HEIGHTS = { 1400: 900, 900: 1000, 390: 844 };

/**
 * Console and log messages that are not defects of the console. Each entry says why. Anything
 * else fails the check.
 */
const ALLOWED_CONSOLE_ERRORS = [
  // A same-origin response of 400 or more is already judged by the status check, which knows the
  // statuses a page expects. Chromium also logs it as "Failed to load resource", which would
  // fail an expected 403 a second time.
  { pattern: /^Failed to load resource: the server responded with a status of \d+/, reason: 'covered by the response status check' },
];

/** Resolves after the given number of milliseconds. */
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

// ------------------------------------------------------------------------------------------
// Arguments
// ------------------------------------------------------------------------------------------

/**
 * Parses the command line into an options object. Unknown arguments stop the script, so a typo
 * cannot silently run a different sweep.
 */
function parseArguments(argv) {
  const options = {
    out: null,
    chrome: process.env.CHROME_BIN || null,
    pages: [],
    widths: [1400, 900, 390],
    themes: ['light', 'dark'],
    users: ['admin', 'limited'],
    skipBuild: false,
    list: false,
  };
  const csv = (value) => value.split(',').map((item) => item.trim()).filter(Boolean);
  for (let index = 0; index < argv.length; index++) {
    const argument = argv[index];
    const next = () => {
      if (index + 1 >= argv.length) throw new Error(`${argument} needs a value`);
      return argv[++index];
    };
    switch (argument) {
      case '--out': options.out = path.resolve(next()); break;
      case '--chrome': options.chrome = next(); break;
      case '--pages': options.pages = csv(next()); break;
      case '--widths': options.widths = csv(next()).map(Number); break;
      case '--themes': options.themes = csv(next()); break;
      case '--users': options.users = csv(next()); break;
      case '--skip-build': options.skipBuild = true; break;
      case '--list': options.list = true; break;
      default: throw new Error(`Unknown argument ${argument}`);
    }
  }
  return options;
}

// ------------------------------------------------------------------------------------------
// Process management
// ------------------------------------------------------------------------------------------

/** The process groups this script started, so teardown signals only those. */
const started = [];

/**
 * Starts a child in its own process group (so `dotnet run`'s child dies with it) and records it.
 */
function startProcess(name, command, args, spawnOptions) {
  const child = spawn(command, args, { ...spawnOptions, detached: true });
  const entry = { name, child, exited: false };
  child.on('exit', () => { entry.exited = true; });
  started.push(entry);
  return entry;
}

/** Sends a signal to a started process group; a group that is already gone is not an error. */
function signalGroup(entry, signal) {
  try { process.kill(-entry.child.pid, signal); } catch { /* already gone */ }
}

/**
 * Stops every process this script started: SIGTERM, up to 10 s of grace, then SIGKILL.
 */
async function stopAll() {
  for (const entry of started) signalGroup(entry, 'SIGTERM');
  const deadline = Date.now() + 10_000;
  while (started.some((entry) => !entry.exited) && Date.now() < deadline) await sleep(100);
  for (const entry of started) if (!entry.exited) signalGroup(entry, 'SIGKILL');
  await sleep(200);
}

/** Asks the operating system for a free TCP port on loopback. */
function freePort() {
  return new Promise((resolve, reject) => {
    const server = net.createServer();
    server.listen(0, '127.0.0.1', () => {
      const { port } = server.address();
      server.close(() => resolve(port));
    });
    server.on('error', reject);
  });
}

// ------------------------------------------------------------------------------------------
// Host
// ------------------------------------------------------------------------------------------

/**
 * Starts the Ninlil host with the same environment the Web tests' factory sets
 * (NinlilDnsWebApplicationFactory): SQLite in the output directory, a random signing key, a known
 * administrator, free ports, the admin login rate limit off and no outbound MAC vendor refresh.
 * Returns the base URL once GET /health answers 200.
 */
async function startHost(options) {
  const httpsPort = await freePort();
  const environment = {
    ...process.env,
    DOTNET_CLI_TELEMETRY_OPTOUT: '1',
    DOTNET_NOLOGO: '1',
    NINLIL_STORAGE_ENGINE: 'Sqlite',
    NINLIL_STORAGE_CONNECTION_STRING: `Data Source=${path.join(options.out, 'smoke.sqlite3')}`,
    NINLIL_JWT_SIGNING_KEY: crypto.randomBytes(64).toString('base64'),
    NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_USER_NAME: ADMIN_USER,
    NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD: ADMIN_PASSWORD,
    NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT: String(await freePort()),
    NINLIL_DNS_SERVER_WEB_HTTP_PORT: String(await freePort()),
    NINLIL_DNS_SERVER_WEB_HTTPS_PORT: String(httpsPort),
    NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_PORT: String(await freePort()),
    NINLIL_DNS_SERVER_RUNTIME_SETTINGS_CONFIGURATION_FILE_PATH: path.join(options.out, 'runtime-settings.json'),
    // The limiter would answer 429 once the script has signed in a few times from one address.
    NINLIL_DNS_SERVER_ADMIN_LOGIN_RATE_LIMIT_ENABLE: 'false',
    // No outbound fetch: the embedded MAC vendor snapshot answers lookups and every registry URL
    // points at a loopback port nothing listens on.
    NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_ENABLE_AUTOMATIC_REFRESH: 'false',
    NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_MA_L_REGISTRY_URL: 'http://127.0.0.1:9/registry.csv',
    NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_MA_M_REGISTRY_URL: 'http://127.0.0.1:9/registry.csv',
    NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_MA_S_REGISTRY_URL: 'http://127.0.0.1:9/registry.csv',
    NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_DATA_FILE_PATH: path.join(options.out, 'mac-vendors.snapshot.br'),
  };
  if (!options.skipBuild) {
    const build = spawnSync('dotnet', ['build', webProject, '-p:NinlilSkipAngularBuild=true', '--nologo', '-v', 'q'], {
      cwd: repoRoot,
      encoding: 'utf8',
    });
    fs.writeFileSync(path.join(options.out, 'build.log'), `${build.stdout}\n${build.stderr}`);
    if (build.status !== 0) throw new StartupError('dotnet build failed, see build.log');
  }
  const log = fs.openSync(path.join(options.out, 'host.log'), 'w');
  const host = startProcess('host', 'dotnet', ['run', '--project', webProject, '--no-build', '--no-launch-profile'], {
    cwd: webProject,
    env: environment,
    stdio: ['ignore', log, log],
  });
  const baseUrl = `https://127.0.0.1:${httpsPort}`;
  const deadline = Date.now() + 120_000;
  while (Date.now() < deadline) {
    if (host.exited) throw new StartupError('The host exited during startup, see host.log');
    try {
      const response = await fetch(`${baseUrl}/health`);
      if (response.status === 200) return baseUrl;
    } catch { /* not listening yet */ }
    await sleep(500);
  }
  throw new StartupError('The host did not answer /health within 120 s, see host.log');
}

/** A failure to start the host or the browser; the script exits with code 2. */
class StartupError extends Error {}

// ------------------------------------------------------------------------------------------
// REST helper
// ------------------------------------------------------------------------------------------

/**
 * Builds a small REST client over fetch. `request` returns {status, body}; `ok` throws on a
 * status of 400 or more with the response text, so seed steps report what the server said.
 */
function createApi(baseUrl) {
  const api = {
    token: null,
    async request(method, route, body, token = api.token) {
      const response = await fetch(`${baseUrl}${route}`, {
        method,
        headers: {
          ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
        },
        body: body !== undefined ? JSON.stringify(body) : undefined,
      });
      const text = await response.text();
      let parsed = null;
      try { parsed = text ? JSON.parse(text) : null; } catch { parsed = text; }
      return { status: response.status, body: parsed };
    },
    async ok(method, route, body, token) {
      const result = await api.request(method, route, body, token);
      if (result.status >= 400) {
        throw new Error(`${method} ${route} answered ${result.status}: ${JSON.stringify(result.body).slice(0, 300)}`);
      }
      return result.body;
    },
    /** Signs in over the API and returns the access token. */
    async login(userName, password) {
      const result = await api.ok('POST', '/api/auth/login', { userName, password, rememberMe: false }, null);
      return result.accessToken;
    },
  };
  return api;
}

// ------------------------------------------------------------------------------------------
// Seed data
// ------------------------------------------------------------------------------------------

/**
 * Creates the sample data the pages need and returns the ids the page list refers to. A failed
 * step is recorded in `seedLog` and the sweep goes on: a page that needs the missing id is
 * skipped and says so in the report, instead of the whole run stopping.
 */
async function seedData(api, options, seedLog) {
  const ids = { records: {} };
  const step = async (name, action) => {
    try {
      await action();
      seedLog.push({ step: name, ok: true });
    } catch (error) {
      seedLog.push({ step: name, ok: false, message: String(error.message ?? error) });
    }
  };
  // A zone's first record must be its own SOA record.
  const addSoa = (zoneId, name) => api.ok('POST', `/api/zones/${zoneId}/records`, {
    name, type: 'SOA', timeToLiveSeconds: 3600, class: 'IN', isDisabled: false,
    data: {
      primaryNameServer: `ns1.${name}`, responsiblePersonMailbox: `admin.${name}`, serial: 2026102901,
      refreshSeconds: 3600, retrySeconds: 900, expireSeconds: 1209600, minimumTtlSeconds: 3600,
    },
  });
  const zoneBase = { type: 'Primary', isDnssecSigned: false, isDisabled: false, dnssecAlgorithm: 'ECDSAP256SHA256' };

  await step('zone smoke-main.example', async () => {
    ids.zoneId = (await api.ok('POST', '/api/zones', { ...zoneBase, name: 'smoke-main.example' })).id;
    await addSoa(ids.zoneId, 'smoke-main.example');
  });
  const records = [
    ['A', 'www.smoke-main.example', 'A', { address: '203.0.113.10' }],
    ['AAAA', 'v6.smoke-main.example', 'AAAA', { address: '2001:db8::10' }],
    ['AAAAMAPPED', 'mapped.smoke-main.example', 'AAAA', { address: '::ffff:192.0.2.1' }],
    ['CAA', 'smoke-main.example', 'CAA', { isCritical: false, tag: 'issue', value: 'letsencrypt.org' }],
    ['CAAMAIL', 'smoke-main.example', 'CAA', { isCritical: false, tag: 'issuemail', value: 'letsencrypt.org' }],
    ['APL', 'apl.smoke-main.example', 'APL', { items: [{ addressFamily: 1, prefixLength: 24, negated: false, addressFamilyDependentPart: Buffer.from([192, 0, 2]).toString('base64') }] }],
    ['MX', 'smoke-main.example', 'MX', { preference: 10, exchange: 'mail.smoke-main.example' }],
    ['TXT', 'smoke-main.example', 'TXT', { values: ['v=spf1 -all'] }],
    ['SRV', '_sip._tcp.smoke-main.example', 'SRV', { priority: 10, weight: 60, port: 5060, target: 'sip.smoke-main.example' }],
  ];
  for (const [key, name, type, data] of records) {
    await step(`record ${key}`, async () => {
      const created = await api.ok('POST', `/api/zones/${ids.zoneId}/records`, {
        name, type, timeToLiveSeconds: 3600, data, class: 'IN', isDisabled: false,
      });
      ids.records[key] = created.id;
    });
  }

  await step('zone smoke-secondary.example', async () => {
    ids.secondaryZoneId = (await api.ok('POST', '/api/zones', {
      ...zoneBase, name: 'smoke-secondary.example', type: 'Secondary', primaryServerAddresses: ['192.0.2.53'],
    })).id;
  });

  // Two signed zones: one keeps its active key; the other has its only key retired (the server
  // moves a deleted key to Removed), which leaves a zone marked signed with no key that can sign.
  for (const [label, name, retire] of [['signed', 'smoke-signed.example', false], ['nokey', 'smoke-nokey.example', true]]) {
    await step(`zone ${name}`, async () => {
      const zone = await api.ok('POST', '/api/zones', { ...zoneBase, name, isDnssecSigned: true });
      ids[`${label}ZoneId`] = zone.id;
      await addSoa(zone.id, name);
      await api.ok('POST', `/api/zones/${zone.id}/records`, {
        name: `www.${name}`, type: 'A', timeToLiveSeconds: 300, data: { address: '203.0.113.20' }, class: 'IN', isDisabled: false,
      });
      const existing = await api.ok('GET', `/api/zones/${zone.id}/dnssec-keys`);
      const items = existing.items ?? existing;
      let key = items.find((candidate) => candidate.state !== 'Removed');
      if (!key) {
        key = await api.ok('POST', `/api/zones/${zone.id}/dnssec-keys`, {
          role: 'KSK', algorithm: 'ECDSAP256SHA256', keySizeBits: 256, comments: 'smoke',
        });
      }
      ids[`${label}KeyId`] = key.id;
      if (retire) await api.ok('DELETE', `/api/zones/${zone.id}/dnssec-keys/${key.id}`);
    });
  }

  await step('client', async () => {
    ids.clientId = (await api.ok('POST', '/api/clients', {
      clientIdentifier: '00:11:22:33:44:77', protocolVersion: 'IPv4', displayName: 'Smoke client', isDisabled: false,
    })).id;
  });
  await step('client schedule (UTC)', async () => {
    ids.clientScheduleId = (await api.ok('POST', `/api/clients/${ids.clientId}/schedules`, {
      daysOfWeek: 'All', startTime: '08:00:00', endTime: '17:00:00', timeZoneId: 'UTC', isDisabled: false,
    })).id;
  });
  await step('client group', async () => {
    ids.groupId = (await api.ok('POST', '/api/client-groups', { name: 'Smoke group', isDefault: false, isDisabled: false })).id;
  });
  await step('client group CIDR network', async () => {
    ids.networkCidrId = (await api.ok('POST', `/api/client-groups/${ids.groupId}/networks`, { cidr: '192.0.2.0/24' })).id;
  });
  await step('client group ClientID network', async () => {
    ids.networkClientId = (await api.ok('POST', `/api/client-groups/${ids.groupId}/networks`, { clientId: 'smoke-device-1' })).id;
  });
  await step('client group schedule', async () => {
    ids.groupScheduleId = (await api.ok('POST', `/api/client-groups/${ids.groupId}/schedules`, {
      daysOfWeek: 'All', startTime: '22:00:00', endTime: '23:00:00', timeZoneId: 'UTC', isDisabled: false,
    })).id;
  });
  for (const [index, definition] of ['1.1.1.1', '9.9.9.9', '8.8.8.8'].entries()) {
    await step(`forwarder ${index + 1}`, async () => {
      const created = await api.ok('POST', '/api/forwarders', {
        name: `Smoke forwarder ${index + 1}`, definitionString: definition, sortOrder: index, isDisabled: false,
      });
      if (index === 0) ids.forwarderId = created.id;
    });
  }
  await step('DHCP v4 scope', async () => {
    ids.scopeId = (await api.ok('POST', '/api/dhcp-scopes', {
      name: 'Smoke v4', protocolVersion: 'IPv4', rangeStartAddress: '10.99.0.100', rangeEndAddress: '10.99.0.200',
      subnetPrefixLength: 24, defaultLeaseDuration: '08:00:00', maxLeaseDuration: '1.00:00:00', isDisabled: false,
      gatewayAddresses: ['10.99.0.1'], dnsServerAddresses: ['10.99.0.2'], kind: 'AddressAssignment',
    })).id;
  });
  await step('DHCP v4 custom option', async () => {
    await api.ok('POST', `/api/dhcp-scopes/${ids.scopeId}/custom-options`, {
      optionTag: 224, valueHex: '0a0b', isDisabled: false, valueSource: 'Custom',
    });
  });
  await step('DHCP reservation', async () => {
    ids.reservationId = (await api.ok('POST', `/api/dhcp-scopes/${ids.scopeId}/reservations`, {
      clientIdentifier: '00:11:22:33:44:55', ipAddress: '10.99.0.150', hostName: 'smoke-host', isDisabled: false,
    })).id;
  });
  await step('DHCP v6 scope', async () => {
    ids.scope6Id = (await api.ok('POST', '/api/dhcp-scopes', {
      name: 'Smoke v6', protocolVersion: 'IPv6', rangeStartAddress: '2001:db8:99::100', rangeEndAddress: '2001:db8:99::200',
      subnetPrefixLength: 64, defaultLeaseDuration: '08:00:00', maxLeaseDuration: '1.00:00:00', isDisabled: false,
      dnsServerAddresses: ['2001:db8:99::2'], kind: 'AddressAssignment',
    })).id;
  });
  await step('DHCP tag rule', async () => {
    ids.tagRuleId = (await api.ok('POST', '/api/dhcp-tag-rules', {
      name: 'Smoke tag rule', scopeId: ids.scopeId, priority: 10, isDisabled: false,
      vendorClassIdentifierMatch: 'smoke', vendorClassIdentifierMatchMode: 'Prefix',
    })).id;
  });
  await step('webhook', async () => {
    ids.webhookId = (await api.ok('POST', '/api/webhooks', {
      name: 'Smoke webhook', targetUrl: 'http://127.0.0.1:9/hook', eventTypeNames: ['DnsZoneChanged'], authMode: 'None',
      enableHmacSigning: false, maxDeliveryAttempts: 1, isDisabled: true,
    })).id;
  });
  await seedLimitedUsers(api, ids, step);
  return ids;
}

/**
 * Creates the two limited users the permission checks need: `smokelima` holds the dashboard
 * notice permission pair (selfupdate:read and selfupdatesettings:read); `smokelimb` holds
 * selfupdatesettings:read alone. Both are checked against GET /api/permissions first.
 */
async function seedLimitedUsers(api, ids, step) {
  const roles = [
    ['smoke-limited-a', 'smokelima', ['selfupdate:read', 'selfupdatesettings:read']],
    ['smoke-limited-b', 'smokelimb', ['selfupdatesettings:read']],
  ];
  ids.limited = [];
  for (const [roleName, userName, permissions] of roles) {
    await step(`limited user ${userName}`, async () => {
      const known = await api.ok('GET', '/api/permissions');
      const names = JSON.stringify(known);
      for (const permission of permissions) {
        if (!names.includes(`"${permission}"`)) throw new Error(`The API does not list permission ${permission}`);
      }
      const role = await api.ok('POST', '/api/roles', { name: roleName, description: 'Smoke harness role' });
      for (const permission of permissions) await api.ok('POST', `/api/roles/${role.id}/permissions`, { permission });
      const user = await api.ok('POST', '/api/users', {
        userName, password: LIMITED_PASSWORD, email: `${userName}@example.com`, displayName: userName,
      });
      await api.ok('POST', `/api/users/${user.id}/roles`, { roleName });
      ids.limited.push({ userName, permissions });
    });
  }
}

// ------------------------------------------------------------------------------------------
// Chrome DevTools protocol
// ------------------------------------------------------------------------------------------

/**
 * A minimal DevTools protocol client over the global WebSocket. `send` resolves with the result
 * or rejects with the protocol error; events go to the handlers registered with `on`, keyed by
 * method name; every handler gets (params, sessionId).
 */
class Cdp {
  constructor(socket) {
    this.socket = socket;
    this.nextId = 1;
    this.pending = new Map();
    this.handlers = new Map();
    socket.addEventListener('message', (event) => this.#onMessage(JSON.parse(event.data)));
  }

  static async connect(url) {
    const socket = new WebSocket(url);
    await new Promise((resolve, reject) => {
      socket.addEventListener('open', resolve, { once: true });
      socket.addEventListener('error', () => reject(new Error('DevTools connection failed')), { once: true });
    });
    return new Cdp(socket);
  }

  #onMessage(message) {
    if (message.id !== undefined) {
      const waiting = this.pending.get(message.id);
      if (!waiting) return;
      this.pending.delete(message.id);
      if (message.error) waiting.reject(new Error(`${waiting.method}: ${message.error.message}`));
      else waiting.resolve(message.result);
      return;
    }
    for (const handler of this.handlers.get(message.method) ?? []) handler(message.params, message.sessionId);
  }

  send(method, params = {}, sessionId = undefined) {
    const id = this.nextId++;
    return new Promise((resolve, reject) => {
      this.pending.set(id, { resolve, reject, method });
      this.socket.send(JSON.stringify({ id, method, params, sessionId }));
    });
  }

  on(method, handler) {
    if (!this.handlers.has(method)) this.handlers.set(method, []);
    this.handlers.get(method).push(handler);
  }

  close() { try { this.socket.close(); } catch { /* ignore */ } }
}

/**
 * Starts headless Chromium with a profile in the output directory and returns a connected
 * browser-level Cdp client. The debugging port is chosen by Chromium (port 0) and read from
 * DevToolsActivePort in the profile directory.
 */
async function startChrome(options) {
  const chromeBinary = options.chrome ?? findChrome();
  if (!chromeBinary) throw new StartupError('No Chromium found: set CHROME_BIN or pass --chrome');
  const profile = path.join(options.out, 'chrome');
  fs.rmSync(profile, { recursive: true, force: true });
  fs.mkdirSync(profile, { recursive: true });
  const log = fs.openSync(path.join(options.out, 'chrome.log'), 'w');
  const chrome = startProcess('chrome', chromeBinary, [
    '--headless=new', '--remote-debugging-port=0', `--user-data-dir=${profile}`,
    '--ignore-certificate-errors', '--no-sandbox', '--disable-gpu', '--hide-scrollbars',
    '--no-first-run', '--no-default-browser-check', 'about:blank',
  ], { stdio: ['ignore', log, log] });
  const portFile = path.join(profile, 'DevToolsActivePort');
  const deadline = Date.now() + 30_000;
  while (!fs.existsSync(portFile)) {
    if (chrome.exited) throw new StartupError('Chromium exited during startup, see chrome.log');
    if (Date.now() > deadline) throw new StartupError('Chromium did not write DevToolsActivePort in 30 s');
    await sleep(100);
  }
  const [port, socketPath] = fs.readFileSync(portFile, 'utf8').trim().split('\n');
  return Cdp.connect(`ws://127.0.0.1:${port}${socketPath}`);
}

/** Finds a Chromium binary on PATH when neither --chrome nor CHROME_BIN names one. */
function findChrome() {
  for (const name of ['chromium', 'chromium-browser', 'google-chrome', 'google-chrome-stable']) {
    const result = spawnSync('which', [name], { encoding: 'utf8' });
    if (result.status === 0) return result.stdout.trim();
  }
  return null;
}

/**
 * One browser page in its own browser context (so each user has their own cookies), with the
 * collectors the checks read. `visit` navigates, waits for the network to go idle and returns
 * what was observed since the navigation started.
 */
class BrowserPage {
  static async create(browser, baseUrl) {
    const { browserContextId } = await browser.send('Target.createBrowserContext');
    const { targetId } = await browser.send('Target.createTarget', { url: 'about:blank', browserContextId });
    const { sessionId } = await browser.send('Target.attachToTarget', { targetId, flatten: true });
    const page = new BrowserPage(browser, sessionId, baseUrl, browserContextId, targetId);
    await page.#enable();
    return page;
  }

  constructor(browser, sessionId, baseUrl, browserContextId, targetId) {
    this.browser = browser;
    this.sessionId = sessionId;
    this.baseUrl = baseUrl;
    this.browserContextId = browserContextId;
    this.targetId = targetId;
    this.resetObservations();
    this.inflight = new Set();
    this.lastNetworkActivity = Date.now();
    this.storageStub = null;
  }

  send(method, params) { return this.browser.send(method, params, this.sessionId); }

  /** Clears what the checks read; called at the start of each visit. */
  resetObservations() {
    this.exceptions = [];
    this.consoleErrors = [];
    this.responses = [];
    this.failedRequests = [];
  }

  async #enable() {
    const listen = (method, handler) => this.browser.on(method, (params, sessionId) => {
      if (sessionId === this.sessionId) handler(params);
    });
    await Promise.all([this.send('Page.enable'), this.send('Runtime.enable'), this.send('Network.enable'), this.send('Log.enable')]);
    const requestUrls = new Map();
    // Requests that never finish by design are not "in flight" for the idle check: the SignalR
    // hub (WebSocket or long poll) and server-sent streams.
    const ignored = (url) => url.includes('/hubs/');
    listen('Network.requestWillBeSent', (event) => {
      requestUrls.set(event.requestId, event.request.url);
      if (!ignored(event.request.url)) this.inflight.add(event.requestId);
      this.lastNetworkActivity = Date.now();
    });
    listen('Network.responseReceived', (event) => {
      requestUrls.set(event.requestId, event.response.url);
      this.responses.push({ url: event.response.url, status: event.response.status, type: event.type });
    });
    const finish = (event) => { this.inflight.delete(event.requestId); this.lastNetworkActivity = Date.now(); };
    listen('Network.loadingFinished', finish);
    listen('Network.loadingFailed', (event) => {
      finish(event);
      const url = requestUrls.get(event.requestId) ?? '';
      // A request cancelled by the next navigation is not a failure of the page.
      if (!event.canceled && !ignored(url)) this.failedRequests.push({ url, error: event.errorText });
    });
    listen('Runtime.exceptionThrown', (event) => {
      const details = event.exceptionDetails;
      this.exceptions.push(details.exception?.description ?? details.text);
    });
    listen('Runtime.consoleAPICalled', (event) => {
      if (event.type !== 'error') return;
      this.consoleErrors.push(event.args.map((arg) => arg.value ?? arg.description ?? '').join(' '));
    });
    listen('Log.entryAdded', (event) => {
      if (event.entry.level === 'error') this.consoleErrors.push(event.entry.text);
    });
    // Answers GET /api/storage with a synthetic body when a page asks for it (see visit).
    listen('Fetch.requestPaused', (event) => this.#onPaused(event));
  }

  async #onPaused(event) {
    const requestId = event.requestId;
    try {
      if (this.storageStub && event.request.method === 'GET') {
        await this.send('Fetch.fulfillRequest', {
          requestId,
          responseCode: 200,
          responseHeaders: [{ name: 'Content-Type', value: 'application/json' }],
          body: Buffer.from(JSON.stringify(this.storageStub)).toString('base64'),
        });
      } else {
        await this.send('Fetch.continueRequest', { requestId });
      }
    } catch { /* the page navigated away */ }
  }

  /**
   * Intercepts GET /api/storage and answers it with the given body (null stops the interception).
   * Used for the replica states: a replica needs two database servers, and the page only renders
   * the JSON the status endpoint returns.
   */
  async stubStorageStatus(body) {
    this.storageStub = body;
    if (body) await this.send('Fetch.enable', { patterns: [{ urlPattern: '*/api/storage', requestStage: 'Request' }] });
    else await this.send('Fetch.disable');
  }

  async evaluate(expression) {
    const result = await this.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(`evaluate failed: ${result.exceptionDetails.exception?.description ?? result.exceptionDetails.text}`);
    return result.result.value;
  }

  /** Waits until no request was in flight for `quietMs`, at most `maxMs`. Returns whether it went idle. */
  async waitForIdle(quietMs = 500, maxMs = 15_000) {
    const deadline = Date.now() + maxMs;
    while (Date.now() < deadline) {
      if (this.inflight.size === 0 && Date.now() - this.lastNetworkActivity >= quietMs) return true;
      await sleep(50);
    }
    return false;
  }

  /** Waits until the expression is truthy in the page, at most `maxMs`. */
  async waitFor(expression, maxMs = 5000) {
    const deadline = Date.now() + maxMs;
    while (Date.now() < deadline) {
      if (await this.evaluate(expression)) return true;
      await sleep(100);
    }
    return false;
  }

  /** Clicks the first visible element matching `selector` whose text or aria-label contains `text`. */
  async click(selector, text = '') {
    const found = await this.evaluate(`(() => {
      const wanted = ${JSON.stringify(text)};
      const visible = (el) => el.getClientRects().length > 0;
      const el = [...document.querySelectorAll(${JSON.stringify(selector)})].find((candidate) =>
        visible(candidate) && ((candidate.textContent || '') + ' ' + (candidate.getAttribute('aria-label') || '')).includes(wanted));
      if (!el) return false;
      el.click();
      return true;
    })()`);
    return found;
  }

  /** Sets a select's value the way a user's choice does (value plus a change event). */
  async selectValue(selector, value) {
    return this.evaluate(`(() => {
      const el = document.querySelector(${JSON.stringify(selector)});
      if (!el) return false;
      el.value = ${JSON.stringify(value)};
      el.dispatchEvent(new Event('change', { bubbles: true }));
      return el.value === ${JSON.stringify(value)};
    })()`);
  }

  /** Sets a text input through the native setter so Angular's forms see the change. */
  async typeInto(selector, value) {
    return this.evaluate(`(() => {
      const el = document.querySelector(${JSON.stringify(selector)});
      if (!el) return false;
      const setter = Object.getOwnPropertyDescriptor(el.constructor.prototype, 'value').set;
      setter.call(el, ${JSON.stringify(value)});
      el.dispatchEvent(new Event('input', { bubbles: true }));
      return true;
    })()`);
  }

  async setViewport(width) {
    const height = HEIGHTS[width] ?? 900;
    await this.send('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });
  }

  /** Navigates to the route and waits for the load event and an idle network. */
  async navigate(route) {
    this.resetObservations();
    this.inflight.clear();
    this.lastNetworkActivity = Date.now();
    const loaded = new Promise((resolve) => {
      const handler = (params, sessionId) => { if (sessionId === this.sessionId) resolve(); };
      this.browser.on('Page.loadEventFired', handler);
      setTimeout(resolve, 15_000);
    });
    await this.send('Page.navigate', { url: `${this.baseUrl}${route}` });
    await loaded;
    return this.waitForIdle();
  }

  /** Writes a full-page PNG, capped at 10,000 CSS pixels so a long list cannot fill the disk. */
  async screenshot(file) {
    const metrics = await this.send('Page.getLayoutMetrics');
    const size = metrics.cssContentSize ?? metrics.contentSize;
    const clip = { x: 0, y: 0, width: Math.ceil(size.width), height: Math.min(Math.ceil(size.height), 10_000), scale: 1 };
    const { data } = await this.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: true, clip });
    fs.writeFileSync(file, Buffer.from(data, 'base64'));
  }

  async dispose() {
    try { await this.browser.send('Target.closeTarget', { targetId: this.targetId }); } catch { /* ignore */ }
    try { await this.browser.send('Target.disposeBrowserContext', { browserContextId: this.browserContextId }); } catch { /* ignore */ }
  }
}

// ------------------------------------------------------------------------------------------
// Page list
// ------------------------------------------------------------------------------------------

/**
 * Builds the list of pages for the administrator. Each entry has:
 *   slug     file-name stem and --pages filter target;
 *   route    the URL path, built from the seed ids (an entry whose ids are missing is skipped);
 *   expect   response statuses that are not failures for this page, as "STATUS /path-substring";
 *   before / after  async hooks around the visit (server-side state for the page);
 *   prepare  async (page) hook after the load, to open a dialog or pick a variant;
 *   onlyWidth  a predicate on the viewport width; the page is skipped where it is false;
 *   stub     a function (realStatus) returning the synthetic body for GET /api/storage.
 * Routes come from src/angular/src/app/app.routes.ts.
 */
function buildAdminPages(ids, api, options) {
  const pages = [];
  const add = (slug, route, extra = {}) => pages.push({ slug, route, ...extra });
  const needs = (route, ...keys) => (keys.every((key) => ids[key]) ? route : null);

  add('dashboard', '/dashboard');
  add('dashboard-narrow-sidebar', '/dashboard', { onlyWidth: (width) => width >= 992, prepare: (page) => page.click('button.shell-collapse-toggle') });
  add('dashboard-mobile-drawer', '/dashboard', { onlyWidth: (width) => width < 992, prepare: (page) => page.click('button[aria-label="Toggle sidebar"]') });
  add('forbidden', '/forbidden');
  add('login', '/login', { fresh: true, expect: ['401 /api/'] });

  add('zones-list', '/zones');
  add('zone-new', '/zones/new', {
    variants: ['Primary', 'Secondary', 'Stub', 'Forwarder', 'SecondaryForwarder', 'Catalog', 'SecondaryCatalog', 'CloudPrimary', 'CloudSecondary'],
    prepare: (page, variant) => page.selectValue('#zone-type', variant),
  });
  add('zone-edit', needs(`/zones/${ids.zoneId}/edit`, 'zoneId'));
  add('zone-edit-secondary', needs(`/zones/${ids.secondaryZoneId}/edit`, 'secondaryZoneId'));
  add('records-list', needs(`/zones/${ids.zoneId}/records`, 'zoneId'));
  add('record-new', needs(`/zones/${ids.zoneId}/records/new`, 'zoneId'), {
    variants: ['A', 'AAAA', 'CAA', 'APL', 'MX', 'TXT', 'SRV'],
    prepare: (page, variant) => page.selectValue('#record-type', variant),
  });
  for (const key of ['A', 'AAAA', 'AAAAMAPPED', 'CAA', 'CAAMAIL', 'APL', 'MX', 'TXT', 'SRV']) {
    add(`record-edit-${key.toLowerCase()}`, ids.records[key] && ids.zoneId ? `/zones/${ids.zoneId}/records/${ids.records[key]}/edit` : null);
  }
  add('dnssec-keys-signed', needs(`/zones/${ids.signedZoneId}/dnssec-keys`, 'signedZoneId'));
  add('dnssec-keys-signed-rollover-dialog', needs(`/zones/${ids.signedZoneId}/dnssec-keys`, 'signedZoneId'), {
    prepare: (page) => page.click('button', 'Start a rollover'),
  });
  add('dnssec-keys-nokey', needs(`/zones/${ids.nokeyZoneId}/dnssec-keys`, 'nokeyZoneId'));
  add('dnssec-key-new', needs(`/zones/${ids.signedZoneId}/dnssec-keys/new`, 'signedZoneId'));
  add('dnssec-key-edit', ids.signedZoneId && ids.signedKeyId ? `/zones/${ids.signedZoneId}/dnssec-keys/${ids.signedKeyId}/edit` : null);

  for (const [area, listRoute] of [
    ['dns-rewrites', '/dns-rewrites'], ['tsig-keys', '/tsig-keys'], ['cloud-provider-credentials', '/cloud-provider-credentials'],
    ['access-control-rules', '/access-control-rules'], ['query-log', '/query-log'], ['webhooks', '/webhooks'],
    ['client-static-address-mappings', '/client-static-address-mappings'],
  ]) {
    add(`${area}-list`, listRoute);
    if (area !== 'query-log') add(`${area}-new`, `${listRoute}/new`);
  }
  add('cluster', '/cluster');
  add('webhook-deliveries', needs(`/webhooks/${ids.webhookId}/deliveries`, 'webhookId'));
  add('webhook-edit', needs(`/webhooks/${ids.webhookId}/edit`, 'webhookId'));

  add('filter-lists-list', '/filter-lists');
  add('filter-list-new', '/filter-lists/new');
  add('clients-list', '/clients');
  add('client-new', '/clients/new');
  add('client-edit', needs(`/clients/${ids.clientId}/edit`, 'clientId'));
  add('client-addresses', needs(`/clients/${ids.clientId}/addresses`, 'clientId'));
  add('client-list-assignments', needs(`/clients/${ids.clientId}/list-assignments`, 'clientId'));
  add('client-schedules', needs(`/clients/${ids.clientId}/schedules`, 'clientId'));
  add('client-schedule-new', needs(`/clients/${ids.clientId}/schedules/new`, 'clientId'));
  add('client-schedule-edit', ids.clientId && ids.clientScheduleId ? `/clients/${ids.clientId}/schedules/${ids.clientScheduleId}/edit` : null);
  add('client-activity', needs(`/clients/${ids.clientId}/activity`, 'clientId'));

  add('client-groups-list', '/client-groups');
  add('client-group-new', '/client-groups/new');
  add('client-group-edit', needs(`/client-groups/${ids.groupId}/edit`, 'groupId'));
  add('client-group-networks', needs(`/client-groups/${ids.groupId}/networks`, 'groupId'));
  add('client-group-networks-delete-dialog', needs(`/client-groups/${ids.groupId}/networks`, 'groupId'), {
    // Opens the delete confirmation of the ClientID row, which names the row in its text.
    prepare: (page) => page.evaluate(`(() => {
      const rows = [...document.querySelectorAll('tr')];
      const row = rows.find((candidate) => candidate.textContent.includes('smoke-device-1'));
      const button = row?.querySelector('button[aria-label="Delete network"]');
      if (!button) return false;
      button.click();
      return true;
    })()`),
  });
  add('client-group-network-new', needs(`/client-groups/${ids.groupId}/networks/new`, 'groupId'));
  add('client-group-schedules', needs(`/client-groups/${ids.groupId}/schedules`, 'groupId'));
  add('client-group-schedule-new', needs(`/client-groups/${ids.groupId}/schedules/new`, 'groupId'));
  add('client-group-list-assignments', needs(`/client-groups/${ids.groupId}/list-assignments`, 'groupId'));

  add('forwarders-list', '/forwarders');
  add('forwarder-new', '/forwarders/new');
  add('forwarder-bulk', '/forwarders/new', {
    // The bulk form takes several lines at once; the field is the form's multi-line text area.
    prepare: async (page) => { await page.evaluate(`document.querySelector('textarea')?.focus()`); },
  });
  add('forwarder-edit', needs(`/forwarders/${ids.forwarderId}/edit`, 'forwarderId'));

  add('dhcp-services', '/dhcp-services');
  add('dhcp-scopes-list', '/dhcp-scopes');
  add('dhcp-scope-new-v4', '/dhcp-scopes/new');
  add('dhcp-scope-edit-v4', needs(`/dhcp-scopes/${ids.scopeId}/edit`, 'scopeId'));
  add('dhcp-scope-edit-v6', needs(`/dhcp-scopes/${ids.scope6Id}/edit`, 'scope6Id'));
  add('dhcp-reservations', needs(`/dhcp-scopes/${ids.scopeId}/reservations`, 'scopeId'));
  add('dhcp-reservation-new', needs(`/dhcp-scopes/${ids.scopeId}/reservations/new`, 'scopeId'));
  add('dhcp-reservation-edit', ids.scopeId && ids.reservationId ? `/dhcp-scopes/${ids.scopeId}/reservations/${ids.reservationId}/edit` : null);
  add('dhcp-tag-rules-list', '/dhcp-tag-rules');
  add('dhcp-tag-rule-new', '/dhcp-tag-rules/new');
  add('dhcp-tag-rule-edit', needs(`/dhcp-tag-rules/${ids.tagRuleId}/edit`, 'tagRuleId'));
  add('dhcp-leases', '/dhcp-leases');

  for (const name of SETTINGS_PAGES) add(`settings-${name}`, `/settings/${name}`);

  // Storage page states. "none" is the real status without a replica; "pending" schedules a real
  // switch to a SQLite file in the output directory and cancels it afterwards; the replica and
  // outcome states are synthetic (see BrowserPage.stubStorageStatus).
  add('settings-storage-none', '/settings/storage');
  add('settings-storage-pending', '/settings/storage', {
    before: () => api.ok('POST', '/api/storage/switch', { targetEngine: 'Sqlite', sqlite: { filePath: path.join(options.out, 'target.sqlite3') } }),
    after: () => api.request('DELETE', '/api/storage/switch'),
  });
  const replica = (overrides) => (real) => ({
    ...real,
    readReplica: {
      configured: true, eligible: true, lagSeconds: 0.4, schemaCurrent: true, lastCheckedAtUtc: new Date().toISOString(),
      reason: null, readsToReplica: 1200, readsToPrimary: 80, hosts: ['replica-1.example.net:5432'], readPreference: 'Secondary',
      ...overrides,
    },
    primary: { hosts: ['primary.example.net:5432'] },
  });
  add('settings-storage-replica-eligible', '/settings/storage', { stub: replica({}), synthetic: true });
  add('settings-storage-replica-degraded', '/settings/storage', {
    stub: replica({ eligible: false, lagSeconds: 42.5, schemaCurrent: false, reason: 'The replica is 42 seconds behind and its schema is older than the primary.' }),
    synthetic: true,
  });
  add('settings-storage-outcome-ok', '/settings/storage', {
    stub: (real) => ({ ...real, lastSwitchOutcome: { succeeded: true, fromEngine: 'Sqlite', toEngine: 'PostgreSql', atUtc: new Date().toISOString(), rowsCopied: 1234, message: 'Switched.' } }),
    synthetic: true,
  });
  add('settings-storage-outcome-failed', '/settings/storage', {
    stub: (real) => ({ ...real, lastSwitchOutcome: { succeeded: false, fromEngine: 'Sqlite', toEngine: 'MySql', atUtc: new Date().toISOString(), rowsCopied: 0, message: 'The target is not reachable.' } }),
    synthetic: true,
  });

  add('users-list', '/users');
  add('user-new', '/users/new');
  add('roles-list', '/roles');
  add('role-new', '/roles/new');
  add('api-tokens-list', '/api-tokens');
  add('api-token-new', '/api-tokens/new');
  add('account-security', '/account/security');
  add('account-profile', '/account/profile');
  add('backup', '/backup');
  add('services', '/services');
  return pages.filter((page) => page.route);
}

/** The child routes of /settings (app.routes.ts), each a page of its own. */
const SETTINGS_PAGES = [
  'recursive', 'transports', 'filtering', 'forwarding', 'rewrites', 'rate-limiting', 'outbound-query', 'client-subnet',
  'dns64', 'cluster', 'blocked-services', 'safe-search', 'event-broker', 'dns-cookies', 'query-logging', 'reverse-proxy',
  'appearance', 'acme', 'smtp', 'sso', 'dnssec-policy', 'dnssec-key-rollover', 'identity', 'web-listen', 'ptr-sync',
  'zone-signing', 'notify', 'zone-transfer', 'zone-transfer-client', 'secondary-zone-refresh', 'tsig', 'webhook-delivery',
  'pipeline', 'branding', 'admin-login-rate-limit', 'client-presence', 'mac-vendors', 'self-update', 'dhcp',
];

// ------------------------------------------------------------------------------------------
// Checks and sweep
// ------------------------------------------------------------------------------------------

/**
 * Evaluates the per-page checks after a page loaded and returns [{check, ok, message}].
 * `expect` lists "STATUS /path-substring" responses that are expected for this page.
 */
async function runChecks(page, idle, expect = []) {
  const results = [];
  const record = (check, ok, message = '') => results.push({ check, ok, message });

  record('no uncaught exception', page.exceptions.length === 0, page.exceptions.join(' | ').slice(0, 600));

  const consoleProblems = page.consoleErrors.filter((text) => !ALLOWED_CONSOLE_ERRORS.some((rule) => rule.pattern.test(text)));
  record('no console error', consoleProblems.length === 0, consoleProblems.join(' | ').slice(0, 600));

  const origin = new URL(page.baseUrl).origin;
  const badResponses = page.responses.filter((response) => {
    if (response.status < 400 || !response.url.startsWith(origin)) return false;
    const pathAndQuery = response.url.slice(origin.length);
    return !expect.some((entry) => {
      const [status, fragment] = entry.split(' ');
      return Number(status) === response.status && pathAndQuery.includes(fragment);
    });
  });
  record('API and asset responses below 400', badResponses.length === 0,
    badResponses.map((response) => `${response.status} ${response.url.slice(origin.length)}`).slice(0, 8).join(' | '));
  record('no failed request', page.failedRequests.length === 0,
    page.failedRequests.map((failure) => `${failure.error} ${failure.url.slice(origin.length)}`).slice(0, 5).join(' | '));

  const heading = await page.evaluate(`[...document.querySelectorAll('h1, h2, .card-header, c-card-header')].some((el) => el.getClientRects().length > 0 && el.textContent.trim().length > 0)`);
  record('visible heading', heading === true);

  const overflow = await page.evaluate(`(() => {
    const doc = document.documentElement;
    if (doc.scrollWidth <= window.innerWidth + 1) return null;
    const wide = [...document.querySelectorAll('body *')]
      .filter((el) => el.getBoundingClientRect().right > window.innerWidth + 1)
      .slice(0, 3)
      .map((el) => el.tagName.toLowerCase() + (el.className && typeof el.className === 'string' ? '.' + el.className.trim().split(/\\s+/).slice(0, 2).join('.') : ''));
    return { scrollWidth: doc.scrollWidth, innerWidth: window.innerWidth, wide };
  })()`);
  record('no horizontal overflow', overflow === null,
    overflow ? `scrollWidth ${overflow.scrollWidth} > innerWidth ${overflow.innerWidth}; wide: ${overflow.wide.join(', ')}` : '');

  record('network idle', idle === true, idle ? '' : 'a request was still in flight after 15 s');
  return results;
}

/** Signs in through the login form: native value setter plus an input event, then a click. */
async function signInThroughForm(page, userName, password) {
  await page.navigate('/login');
  const filled = (await page.waitFor(`!!document.querySelector('#login-username')`))
    && (await page.typeInto('#login-username', userName))
    && (await page.typeInto('#login-password', password));
  if (!filled) throw new Error('The login form did not render');
  await page.click('button[type="submit"], button', 'Sign in');
  const left = await page.waitFor(`!location.pathname.startsWith('/login')`, 15_000);
  if (!left) throw new Error(`Sign in as ${userName} did not leave /login`);
  await page.waitForIdle();
}

/**
 * Runs the sweep for one user: for each theme and width, visits every page, records the checks
 * and the screenshot. Returns nothing; results go into `report`.
 */
async function sweepUser(context) {
  const { browser, options, baseUrl, api, user, pages, report } = context;
  const page = await BrowserPage.create(browser, baseUrl);
  try {
    await page.setViewport(1400);
    await signInThroughForm(page, user.userName, user.password);
    for (const theme of options.themes) {
      await api.ok('PATCH', '/api/appearance-settings/me', { mode: theme === 'dark' ? 'Dark' : 'Light' }, user.token);
      for (const width of options.widths) {
        await page.setViewport(width);
        for (const entry of pages) {
          if (entry.onlyWidth && !entry.onlyWidth(width)) continue;
          const variants = entry.variants ?? [null];
          for (const variant of variants) {
            const slug = [user.prefix, entry.slug, variant ? variant.toLowerCase() : null].filter(Boolean).join('-');
            if (options.pages.length > 0 && !options.pages.some((part) => slug.includes(part))) continue;
            await visit({ ...context, page }, entry, variant, slug, width, theme);
          }
        }
      }
    }
  } finally {
    await page.dispose();
  }
}

/** Visits one page variant and records its checks. A thrown error becomes a failed check. */
async function visit(context, entry, variant, slug, width, theme) {
  const { page, options, api, user, report } = context;
  const identity = { page: slug, user: user.label, width, theme };
  const shotFile = path.join(options.out, 'shots', `${slug}-${width}-${theme}.png`);
  let freshPage = null;
  try {
    if (entry.before) await entry.before();
    let target = page;
    if (entry.fresh) {
      // The login page is visited without a session, in a context of its own.
      freshPage = await BrowserPage.create(context.browser, context.baseUrl);
      await freshPage.setViewport(width);
      target = freshPage;
    }
    if (entry.stub) {
      const real = await api.ok('GET', '/api/storage', undefined, user.token);
      await target.stubStorageStatus(entry.stub(real));
    }
    const idle = await target.navigate(entry.route);
    if (entry.prepare) {
      const worked = await entry.prepare(target, variant);
      if (worked === false) report.notes.push({ ...identity, note: 'prepare step found nothing to act on' });
      await target.waitForIdle(300, 5000);
    }
    await sleep(150);
    if (report.facts.timeZoneHasUtc === undefined) {
      report.facts.timeZoneHasUtc = await target.evaluate(`typeof Intl.supportedValuesOf === 'function' ? Intl.supportedValuesOf('timeZone').includes('UTC') : 'unsupported'`);
      report.facts.timeZoneCount = await target.evaluate(`Intl.supportedValuesOf('timeZone').length`);
    }
    const checks = await runChecks(target, idle, entry.expect ?? user.expect?.(entry) ?? []);
    for (const check of checks) report.checks.push({ ...identity, ...check });
    await target.screenshot(shotFile);
    report.screenshots.push({ ...identity, file: shotFile, synthetic: entry.synthetic === true });
    if (entry.slug === 'dashboard' && user.collectNav && !user.navLinks) {
      user.navLinks = await target.evaluate(`[...document.querySelectorAll('.sidebar-nav a.nav-link')].map((a) => new URL(a.href).pathname)`);
    }
  } catch (error) {
    report.checks.push({ ...identity, check: 'visit completed', ok: false, message: String(error.message ?? error).slice(0, 400) });
  } finally {
    if (entry.stub) { try { await (freshPage ?? page).stubStorageStatus(null); } catch { /* ignore */ } }
    if (entry.after) { try { await entry.after(); } catch { /* ignore */ } }
    if (freshPage) await freshPage.dispose();
  }
}

/**
 * Pages for a limited user: the dashboard (notice link rule), the self-update settings page (status
 * card), the forbidden page, and every nav entry the user can see. The nav entries are found
 * from the dashboard's sidebar, so the sweep opens exactly what the user is offered.
 */
function buildLimitedPages(user) {
  const pages = [
    { slug: 'dashboard', route: '/dashboard', expect: ['403 /api/'] },
    { slug: 'settings-self-update', route: '/settings/self-update', expect: ['403 /api/'] },
    { slug: 'forbidden', route: '/forbidden' },
  ];
  for (const link of user.navLinks ?? []) {
    if (link === '/dashboard') continue;
    pages.push({ slug: `nav-${link.replace(/^\//, '').replaceAll('/', '-')}`, route: link });
  }
  return pages;
}

// ------------------------------------------------------------------------------------------
// Report
// ------------------------------------------------------------------------------------------

/** Writes report.json and report.md and returns the number of failed checks. */
function writeReport(options, report) {
  const failures = report.checks.filter((check) => !check.ok);
  fs.writeFileSync(path.join(options.out, 'report.json'), JSON.stringify(report, null, 2));
  const lines = [];
  lines.push('# Admin console smoke report', '');
  lines.push(`Started ${report.startedAt}, finished ${new Date().toISOString()}.`, '');
  lines.push('## Summary', '');
  lines.push(`- Checks: ${report.checks.length} (failed: ${failures.length})`);
  lines.push(`- Page visits: ${report.screenshots.length}, distinct pages: ${new Set(report.screenshots.map((shot) => shot.page)).size}`);
  lines.push(`- Users: ${[...new Set(report.checks.map((check) => check.user))].join(', ')}`);
  lines.push(`- Widths: ${options.widths.join(', ')}; themes: ${options.themes.join(', ')}`);
  lines.push(`- Time zone list holds UTC: ${report.facts.timeZoneHasUtc} (${report.facts.timeZoneCount} names)`);
  lines.push(`- Seed steps failed: ${report.seed.filter((item) => !item.ok).length} of ${report.seed.length}`, '');
  lines.push('## Failed checks', '');
  if (failures.length === 0) lines.push('None.', '');
  for (const failure of failures) {
    lines.push(`- [${failure.user}] ${failure.page} @${failure.width} ${failure.theme}: ${failure.check}${failure.message ? ` -- ${failure.message}` : ''}`);
  }
  if (failures.length) lines.push('');
  lines.push('## Seed steps that failed', '');
  const seedFailures = report.seed.filter((item) => !item.ok);
  if (seedFailures.length === 0) lines.push('None.', '');
  for (const item of seedFailures) lines.push(`- ${item.step}: ${item.message}`);
  if (seedFailures.length) lines.push('');
  lines.push('## Notes', '');
  if (report.notes.length === 0) lines.push('None.', '');
  for (const note of report.notes) lines.push(`- [${note.user}] ${note.page} @${note.width} ${note.theme}: ${note.note}`);
  lines.push('', '## Screenshot index', '');
  lines.push('Screenshots marked synthetic show a storage status answered by the script (replica and outcome states).', '');
  for (const shot of report.screenshots) lines.push(`- ${shot.file}${shot.synthetic ? ' (synthetic)' : ''}`);
  fs.writeFileSync(path.join(options.out, 'report.md'), `${lines.join('\n')}\n`);
  return failures.length;
}

// ------------------------------------------------------------------------------------------
// Main
// ------------------------------------------------------------------------------------------

/** Runs the whole sweep and returns the exit code. */
async function main() {
  const options = parseArguments(process.argv.slice(2));
  if (options.list) {
    const pages = buildAdminPages({ records: {}, ...Object.fromEntries(['zoneId', 'secondaryZoneId', 'signedZoneId', 'signedKeyId', 'nokeyZoneId', 'clientId', 'clientScheduleId', 'groupId', 'forwarderId', 'scopeId', 'scope6Id', 'reservationId', 'tagRuleId', 'webhookId'].map((key) => [key, 'x'])) }, null, { out: '.' });
    for (const page of pages) console.log(page.slug, ...(page.variants ?? []));
    return 0;
  }
  if (!options.out) throw new Error('--out <directory> is required');
  fs.mkdirSync(options.out, { recursive: true });
  for (const sub of ['shots', 'chrome']) fs.rmSync(path.join(options.out, sub), { recursive: true, force: true });
  for (const file of fs.readdirSync(options.out)) {
    if (/^(smoke\.sqlite3.*|runtime-settings\.json|mac-vendors.*|target\.sqlite3.*|report\..*)$/.test(file)) fs.rmSync(path.join(options.out, file), { force: true });
  }
  fs.mkdirSync(path.join(options.out, 'shots'), { recursive: true });

  const report = { startedAt: new Date().toISOString(), checks: [], screenshots: [], seed: [], notes: [], facts: {} };
  let browser = null;
  let exitCode = 0;
  try {
    const baseUrl = await startHost(options);
    const api = createApi(baseUrl);
    api.token = await api.login(ADMIN_USER, ADMIN_PASSWORD);
    const ids = await seedData(api, options, report.seed);
    browser = await startChrome(options);

    const adminPages = buildAdminPages(ids, api, options);
    const admin = { label: 'admin', prefix: '', userName: ADMIN_USER, password: ADMIN_PASSWORD, token: api.token };
    const base = { browser, options, baseUrl, api, report };
    if (options.users.includes('admin')) await sweepUser({ ...base, user: admin, pages: adminPages });
    if (options.users.includes('limited')) {
      for (const limited of ids.limited) {
        const user = {
          label: limited.userName, prefix: limited.userName, userName: limited.userName, password: LIMITED_PASSWORD,
          token: await api.login(limited.userName, LIMITED_PASSWORD), collectNav: true,
        };
        // The nav entries are read from the dashboard, so the first pass visits only the dashboard
        // list; later passes use the full list.
        user.navLinks = await discoverNavLinks(browser, baseUrl, user);
        await sweepUser({ ...base, user, pages: buildLimitedPages(user) });
      }
    }
    if (report.screenshots.length === 0) {
      report.checks.push({ page: '(sweep)', user: '-', width: 0, theme: '-', check: 'at least one page visited', ok: false, message: 'no page matched' });
    }
    if (writeReport(options, report) > 0) exitCode = 1;
    console.log(fs.readFileSync(path.join(options.out, 'report.md'), 'utf8').split('## Screenshot index')[0]);
  } catch (error) {
    console.error(error instanceof StartupError ? `Startup failed: ${error.message}` : error);
    exitCode = error instanceof StartupError ? 2 : 1;
    try { writeReport(options, report); } catch { /* the report is best effort here */ }
  } finally {
    // Cancels a scheduled storage switch left by an interrupted page, then stops everything.
    try { browser?.close(); } catch { /* ignore */ }
    await stopAll();
  }
  return exitCode;
}

/** Signs a limited user in once, reads the sidebar's links and closes the page. */
async function discoverNavLinks(browser, baseUrl, user) {
  const page = await BrowserPage.create(browser, baseUrl);
  try {
    await page.setViewport(1400);
    await signInThroughForm(page, user.userName, user.password);
    await page.navigate('/dashboard');
    return await page.evaluate(`[...document.querySelectorAll('.sidebar-nav a.nav-link')].map((a) => new URL(a.href).pathname)`);
  } finally {
    await page.dispose();
  }
}

for (const signal of ['SIGINT', 'SIGTERM']) {
  process.on(signal, async () => {
    await stopAll();
    process.exit(130);
  });
}

main().then((code) => process.exit(code), async (error) => {
  console.error(error);
  await stopAll();
  process.exit(2);
});
