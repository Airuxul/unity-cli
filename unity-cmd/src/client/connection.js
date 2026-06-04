import fs from 'node:fs';
import path from 'node:path';
import { ping } from './command.js';
import {
  DEFAULT_EDITOR_PORT,
  DEFAULT_EDITOR_PLAY_PORT,
  DEFAULT_PLAYER_PORT,
  DEFAULT_TIMEOUT_MS,
  HOST_KIND,
  CONNECTION_RETRY_FLOOR_MS,
  CONNECTION_RETRY_INTERVAL_MS,
  CONNECTION_RETRY_MIN_MS,
  PING_MAX_ATTEMPTS,
  PING_RETRY_INTERVAL_MS,
  PROFILE_BY_HOST_KIND,
  PROFILES_DIR,
  RESOLVE_TARGET_HEALTH_TIMEOUT_MS,
  RESOLVE_TARGET_RETRY_INTERVAL_MS,
} from '../constants.js';
import { findInstanceForTarget, readConnectorState } from './connector-readiness.js';
import { CONNECTOR_STATE } from '../constants.js';

/** @param {string} [kind] */
const VALID_HOST_KINDS = new Set(Object.values(HOST_KIND));

export function normalizeHostKind(kind) {
  const k = (kind ?? HOST_KIND.Editor).toLowerCase();
  if (!VALID_HOST_KINDS.has(k)) {
    throw Object.assign(new Error(`Invalid host kind: ${kind}`), { error_code: 'INVALID_HOST_KIND' });
  }
  return k;
}

/** @param {string} expected @param {string} [actual] */
export function hostKindMatches(expected, actual) {
  if (!actual) return false;
  return normalizeHostKind(expected) === normalizeHostKind(actual);
}

export function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

export function defaultPortForHostKind(hostKind = HOST_KIND.Editor) {
  switch (normalizeHostKind(hostKind)) {
    case HOST_KIND.EditorPlay:
      return DEFAULT_EDITOR_PLAY_PORT;
    case HOST_KIND.Player:
      return DEFAULT_PLAYER_PORT;
    default:
      return DEFAULT_EDITOR_PORT;
  }
}

function profileFile(name) {
  return path.join(PROFILES_DIR, `${name.replace(/[\\/:*?"<>|]/g, '_')}.json`);
}

function sanitizeProfileName(name) {
  const trimmed = String(name ?? '').trim();
  if (!trimmed) throw new Error('profile name is required');
  return trimmed.replace(/[\\/:*?"<>|]/g, '_');
}

/** @returns {object|null} */
export function loadProfile(name) {
  const file = profileFile(name);
  if (!fs.existsSync(file)) return null;
  try {
    return JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch {
    return null;
  }
}

/** @param {string} name @param {{ host: string, port: number, connector_host: string }} target */
export function saveProfile(name, target) {
  fs.mkdirSync(PROFILES_DIR, { recursive: true });
  const payload = {
    name: sanitizeProfileName(name),
    host: target.host,
    port: target.port,
    connector_host: normalizeHostKind(target.connector_host ?? HOST_KIND.Editor),
    updated_at: new Date().toISOString(),
  };
  fs.writeFileSync(profileFile(name), JSON.stringify(payload, null, 2));
  return payload;
}

/** @param {string} name @param {{ host?: string, port?: number, connector_host?: string }} patch */
export function updateProfile(name, patch) {
  const existing = loadProfile(name);
  if (!existing) {
    throw Object.assign(new Error(`Profile not found: ${name}`), { error_code: 'PROFILE_NOT_FOUND' });
  }
  return saveProfile(name, {
    host: patch.host ?? existing.host,
    port: patch.port ?? existing.port,
    connector_host: patch.connector_host ?? existing.connector_host,
  });
}

export function deleteProfile(name) {
  const file = profileFile(name);
  if (!fs.existsSync(file)) {
    throw Object.assign(new Error(`Profile not found: ${name}`), { error_code: 'PROFILE_NOT_FOUND' });
  }
  fs.unlinkSync(file);
  return { name: sanitizeProfileName(name), deleted: true };
}

export function listProfiles() {
  if (!fs.existsSync(PROFILES_DIR)) return [];
  return fs
    .readdirSync(PROFILES_DIR)
    .filter((f) => f.endsWith('.json'))
    .map((f) => {
      try {
        return JSON.parse(fs.readFileSync(path.join(PROFILES_DIR, f), 'utf8'));
      } catch {
        return null;
      }
    })
    .filter(Boolean);
}

export function profileNameForHostKind(hostKind) {
  return PROFILE_BY_HOST_KIND[normalizeHostKind(hostKind)] ?? normalizeHostKind(hostKind);
}

export function isTransientError(err) {
  if (!err) return false;
  const msg = String(err.message ?? err).toLowerCase();
  if (err.name === 'AbortError') return true;
  return (
    msg.includes('abort') ||
    msg.includes('econnrefused') ||
    msg.includes('econnreset') ||
    msg.includes('fetch failed') ||
    msg.includes('network') ||
    msg.includes('timed out')
  );
}

export async function withRetry(fn, { timeoutMs = DEFAULT_TIMEOUT_MS, intervalMs = CONNECTION_RETRY_INTERVAL_MS } = {}) {
  const deadline = Date.now() + timeoutMs;
  let lastError;
  while (Date.now() < deadline) {
    try {
      return await fn(Math.max(CONNECTION_RETRY_MIN_MS, deadline - Date.now()));
    } catch (err) {
      lastError = err;
      if (!isTransientError(err)) throw err;
      await sleep(Math.min(intervalMs, Math.max(CONNECTION_RETRY_FLOOR_MS, deadline - Date.now())));
    }
  }
  throw lastError ?? new Error('connection_retry_exhausted');
}

async function probeHealth(host, port, timeoutMs) {
  const perAttempt = Math.min(
    RESOLVE_TARGET_HEALTH_TIMEOUT_MS,
    Math.max(CONNECTION_RETRY_MIN_MS, timeoutMs),
  );
  const maxAttempts = Math.min(
    PING_MAX_ATTEMPTS,
    Math.max(1, Math.ceil(timeoutMs / perAttempt)),
  );
  return ping(
    { host, port },
    {
      timeoutMs: perAttempt,
      retryOnDisconnect: true,
      maxAttempts,
      retryIntervalMs: PING_RETRY_INTERVAL_MS,
    },
  );
}

/**
 * Resolve target from a saved profile only.
 * @param {{ profile: string, timeoutMs?: number, verify?: boolean }} options
 */
export async function resolveTarget({
  profile,
  timeoutMs = DEFAULT_TIMEOUT_MS,
  verify = true,
  projectPath = process.cwd(),
} = {}) {
  if (!profile) return null;

  const saved = loadProfile(profile);
  if (!saved?.host || !saved?.port) return null;

  const expectedKind = normalizeHostKind(saved.connector_host ?? HOST_KIND.Editor);
  const base = {
    profile: sanitizeProfileName(profile),
    host: saved.host,
    port: saved.port,
    connector_host: expectedKind,
  };

  if (!verify) return base;

  const port = saved.port;
  const inst = findInstanceForTarget(
    { host: saved.host, port, connector_host: expectedKind },
    projectPath,
  );
  const phase = inst?.supervisor_phase;
  const likelyRestarting =
    inst &&
    (inst.listener_running === false ||
      readConnectorState(inst) === CONNECTOR_STATE.Reloading ||
      phase === 'Draining' ||
      phase === 'Starting' ||
      inst.http_status === 'stopped');
  if (likelyRestarting) {
    const restartDeadline = Date.now() + timeoutMs;
    while (Date.now() < restartDeadline) {
      try {
        const remaining = Math.max(CONNECTION_RETRY_MIN_MS, restartDeadline - Date.now());
        const res = await probeHealth(saved.host, port, Math.min(RESOLVE_TARGET_HEALTH_TIMEOUT_MS, remaining));
        if (res.ok && hostKindMatches(expectedKind, res.data?.host)) {
          return {
            ...base,
            connector_host: res.data?.host ?? expectedKind,
            connector_build: res.data?.connector_build,
          };
        }
      } catch {
        // heartbeat may lag behind a live listener during play/reload
      }
      await sleep(RESOLVE_TARGET_RETRY_INTERVAL_MS);
    }
    return null;
  }

  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      const remaining = Math.max(CONNECTION_RETRY_MIN_MS, deadline - Date.now());
      const res = await probeHealth(saved.host, port, remaining);
      if (res.ok && hostKindMatches(expectedKind, res.data?.host)) {
        return {
          ...base,
          connector_host: res.data?.host ?? expectedKind,
          connector_build: res.data?.connector_build,
        };
      }
    } catch {
      // retry until deadline
    }
    await sleep(RESOLVE_TARGET_RETRY_INTERVAL_MS);
  }
  return null;
}
