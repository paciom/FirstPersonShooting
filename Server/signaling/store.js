// Key-value entity store for the accounts system.
//
// One interface, two backends:
//  - TableStore: Azure Table Storage. Rides on the game's existing storage
//    account for ~$0/month; every operation the accounts system needs is a
//    point read/write by (partitionKey, rowKey), which is exactly what Table
//    Storage is good at. Auth is either TABLES_ENDPOINT + managed identity
//    (production — no secret anywhere) or TABLES_CONNECTION_STRING (handy
//    against a real account from a dev machine).
//  - FileStore: a JSON file next to the server, for local development. Inside
//    a container this is EPHEMERAL — accounts vanish on restart — so booting
//    without a connection string logs a loud warning.
//
// Uniqueness (usernames, emails) is enforced through insert-if-absent on
// index entities: insert() returns false when the row already exists, and
// the caller treats that as "taken".
//
// list(pk, limit) is the one range read: a partition's rows in row-key order.
// It exists for the leaderboards (leaderboard.js), which bake the sort order
// into the row key because Table Storage cannot ORDER BY anything else.

const fs = require("fs");
const path = require("path");

/** Azure Table Storage backend. */
class TableStore {
  constructor(endpoint, connectionString, tableName) {
    // Lazy require: local dev without the dependencies installed still works
    // on the file store.
    const { TableClient } = require("@azure/data-tables");
    if (endpoint) {
      // Container Apps managed identity (or az-logged-in dev shell).
      const { DefaultAzureCredential } = require("@azure/identity");
      this.client = new TableClient(endpoint, tableName, new DefaultAzureCredential());
    } else {
      this.client = TableClient.fromConnectionString(connectionString, tableName);
    }
    this.kind = `azure-table:${tableName}`;
  }

  async init() {
    try {
      await this.client.createTable();
    } catch (err) {
      // Already exists — every boot after the first lands here.
      if (err.statusCode !== 409) throw err;
    }
  }

  /** Insert-if-absent. False means the row already existed. */
  async insert(pk, rk, props) {
    try {
      await this.client.createEntity({ partitionKey: pk, rowKey: rk, ...props });
      return true;
    } catch (err) {
      if (err.statusCode === 409) return false;
      throw err;
    }
  }

  async get(pk, rk) {
    try {
      const entity = await this.client.getEntity(pk, rk);
      const { partitionKey, rowKey, etag, timestamp, ...props } = entity;
      return props;
    } catch (err) {
      if (err.statusCode === 404) return null;
      throw err;
    }
  }

  /** Merge props into the row, creating it if needed. */
  async merge(pk, rk, props) {
    await this.client.upsertEntity(
      { partitionKey: pk, rowKey: rk, ...props },
      "Merge"
    );
  }

  /** Idempotent delete. */
  async remove(pk, rk) {
    try {
      await this.client.deleteEntity(pk, rk);
    } catch (err) {
      if (err.statusCode !== 404) throw err;
    }
  }

  /**
   * The first `limit` rows of one partition in ROW-KEY order. Table Storage
   * has no ORDER BY, but it always returns a partition sorted by row key, so
   * a caller that encodes its sort order into the row key gets a sorted
   * read for free (the leaderboard does exactly that).
   */
  async list(pk, limit) {
    const rows = [];
    const filter = `PartitionKey eq '${pk.replace(/'/g, "''")}'`;
    const iter = this.client.listEntities({
      queryOptions: { filter },
    }).byPage({ maxPageSize: Math.min(limit, 1000) });
    for await (const page of iter) {
      for (const entity of page) {
        const { partitionKey, rowKey, etag, timestamp, ...props } = entity;
        rows.push({ rowKey, ...props });
        if (rows.length >= limit) return rows;
      }
    }
    return rows;
  }
}

/** JSON-file backend for local development. */
class FileStore {
  constructor(filePath) {
    this.filePath = filePath;
    this.rows = new Map();
    this.kind = `file:${filePath}`;
    this._writeQueued = false;
  }

  async init() {
    try {
      const raw = fs.readFileSync(this.filePath, "utf8");
      for (const [key, props] of Object.entries(JSON.parse(raw)))
        this.rows.set(key, props);
    } catch (err) {
      if (err.code !== "ENOENT") throw err;
    }
  }

  _key(pk, rk) {
    return `${pk}\n${rk}`;
  }

  // Debounced write-behind: registration does several inserts in a row and
  // one file write covers them all. Temp-then-rename keeps a crash from
  // truncating the file.
  _save() {
    if (this._writeQueued) return;
    this._writeQueued = true;
    setTimeout(() => {
      this._writeQueued = false;
      const obj = {};
      for (const [key, props] of this.rows) obj[key] = props;
      const tmp = this.filePath + ".tmp";
      try {
        fs.mkdirSync(path.dirname(this.filePath), { recursive: true });
        fs.writeFileSync(tmp, JSON.stringify(obj));
        fs.renameSync(tmp, this.filePath);
      } catch (err) {
        console.error("account store write failed:", err.message);
      }
    }, 250).unref();
  }

  async insert(pk, rk, props) {
    const key = this._key(pk, rk);
    if (this.rows.has(key)) return false;
    this.rows.set(key, { ...props });
    this._save();
    return true;
  }

  async get(pk, rk) {
    const props = this.rows.get(this._key(pk, rk));
    return props ? { ...props } : null;
  }

  async merge(pk, rk, props) {
    const key = this._key(pk, rk);
    this.rows.set(key, { ...(this.rows.get(key) || {}), ...props });
    this._save();
  }

  async remove(pk, rk) {
    this.rows.delete(this._key(pk, rk));
    this._save();
  }

  async list(pk, limit) {
    const prefix = pk + "\n";
    const rows = [];
    for (const [key, props] of this.rows)
      if (key.startsWith(prefix))
        rows.push({ rowKey: key.slice(prefix.length), ...props });
    rows.sort((a, b) => (a.rowKey < b.rowKey ? -1 : a.rowKey > b.rowKey ? 1 : 0));
    return rows.slice(0, limit);
  }
}

/** Pick the backend from the environment and initialize it. */
async function createStore() {
  const endpoint = process.env.TABLES_ENDPOINT || "";
  const conn = process.env.TABLES_CONNECTION_STRING || "";
  let store;
  if (endpoint || conn) {
    store = new TableStore(endpoint, conn,
      process.env.TABLES_TABLE_NAME || "PhotonAccounts");
  } else {
    store = new FileStore(path.join(__dirname, "data", "accounts.json"));
    console.warn(
      "accounts: no TABLES_ENDPOINT / TABLES_CONNECTION_STRING — using the" +
        " local file store (fine for development, EPHEMERAL inside a container)"
    );
  }
  await store.init();
  console.log(`accounts: store ready (${store.kind})`);
  return store;
}

module.exports = { createStore };
