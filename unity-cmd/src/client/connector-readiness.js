import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { loadProfile, normalizeHostKind, resolveTarget, sleep } from './connection.js';
import { cacheMatchesInstance, readEditorHttpCache } from './editor-http-cache.js';
import { ping } from './command.js';
import {
  DEFAULT_TIMEOUT_MS,
  CONNECTOR_BUSY_STATES,
  CONNECTOR_FIELD,
  CONNECTOR_STATE,
  EDITOR_HTTP_CACHE_STATUS,
  HEALTH_CONFIRM_CAP_MS,
  HEALTH_CONFIRM_READY_CAP_MS,
  HEALTH_CONFIRM_PING_RETRY_MS,
  HOST_KIND,
  INSTANCES_DIR,
  PLAY_MODE,
  POLL_INTERVAL_MS,
  PROFILE_WAIT_INTERVAL_MS,
  PING_MAX_ATTEMPTS,
  STABLE_TICKS_REQUIRED,
} from '../constants.js';

export function hashProjectPath(projectPath) {
  const normalized = String(projectPath ?? '').replace(/\\/g, '/').toLowerCase();
  return crypto.createHash('md5').update(normalized, 'utf8').digest('hex').slice(0, 16);
}

export function readInstanceFile(filePath) {
  try {
    if (!fs.existsSync(filePath)) return null;
    return JSON.parse(fs.readFileSync(filePath, 'utf8'));
  } catch {
    return null;
  }
}

export function findInstanceByPort(port) {
  if (!port || !fs.existsSync(INSTANCES_DIR)) return null;
  for (const name of fs.readdirSync(INSTANCES_DIR)) {
    if (!name.endsWith('.json')) continue;
    const inst = readInstanceFile(path.join(INSTANCES_DIR, name));
    if (inst?.port === port) return inst;
  }
  return null;
}

export function readConnectorState(inst) {
  return inst?.[CONNECTOR_FIELD.ConnectorState] ?? null;
}

export function readPlayMode(inst) {
  return inst?.[CONNECTOR_FIELD.PlayMode] ?? PLAY_MODE.Edit;
}

export function readCommandsReady(inst) {
  return inst?.[CONNECTOR_FIELD.CommandsReady] === true;
}

export function isEditorInstanceBusy(inst) {
  if (!inst || inst[CONNECTOR_FIELD.ListenerRunning] === false) return true;
  const cs = readConnectorState(inst);
  if (cs === CONNECTOR_STATE.Stopped || CONNECTOR_BUSY_STATES.has(cs)) return true;
  return !readCommandsReady(inst);
}

export function isEditorInstanceReady(inst) {
  return Boolean(inst) && !isEditorInstanceBusy(inst) && readCommandsReady(inst);
}

/**
 * Normalize connector state payload from HTTP /health.
 * @param {object|null|undefined} data
 */
export function normalizeHealthState(data) {
  return {
    connector_state: data?.[CONNECTOR_FIELD.ConnectorState] ?? null,
    play_mode: data?.[CONNECTOR_FIELD.PlayMode] ?? PLAY_MODE.Edit,
    commands_ready: data?.[CONNECTOR_FIELD.CommandsReady] === true,
    listener_running: data?.[CONNECTOR_FIELD.ListenerRunning] !== false,
    blocking_reasons: Array.isArray(data?.[CONNECTOR_FIELD.BlockingReasons])
      ? data[CONNECTOR_FIELD.BlockingReasons]
      : [],
    session_id: data?.[CONNECTOR_FIELD.SessionId] ?? null,
    generation: data?.[CONNECTOR_FIELD.Generation] ?? null,
    raw: data ?? null,
  };
}

export async function confirmEditorHealth(target, inst, { timeoutMs = HEALTH_CONFIRM_CAP_MS } = {}) {
  let res;
  try {
    res = await ping(target, {
      timeoutMs,
      retryOnDisconnect: true,
      maxAttempts: PING_MAX_ATTEMPTS,
      retryIntervalMs: HEALTH_CONFIRM_PING_RETRY_MS,
    });
  } catch {
    return { ok: false, reason: 'health_unreachable' };
  }
  if (!res.ok || !res.data) return { ok: false, reason: 'health_unreachable' };

  if (res.data[CONNECTOR_FIELD.CommandsReady] !== true) {
    const reasons = res.data[CONNECTOR_FIELD.BlockingReasons];
    return {
      ok: false,
      reason: Array.isArray(reasons) && reasons.length > 0 ? reasons.join(',') : 'not_ready',
    };
  }
  if (res.data[CONNECTOR_FIELD.ListenerRunning] === false) return { ok: false, reason: 'listener_down' };
  if (
    inst[CONNECTOR_FIELD.SessionId] &&
    res.data[CONNECTOR_FIELD.SessionId] &&
    res.data[CONNECTOR_FIELD.SessionId] !== inst[CONNECTOR_FIELD.SessionId]
  ) {
    return { ok: false, reason: 'session_mismatch' };
  }
  if (
    inst[CONNECTOR_FIELD.Generation] != null &&
    res.data[CONNECTOR_FIELD.Generation] != null &&
    res.data[CONNECTOR_FIELD.Generation] !== inst[CONNECTOR_FIELD.Generation]
  ) {
    return { ok: false, reason: 'generation_mismatch' };
  }
  return { ok: true, data: res.data };
}

async function waitForRemoteStateReady(target, { timeoutMs = DEFAULT_TIMEOUT_MS } = {}) {
  const deadline = Date.now() + timeoutMs;
  let last = null;
  while (Date.now() < deadline) {
    const remaining = Math.max(0, deadline - Date.now());
    const res = await ping(target, {
      timeoutMs: Math.min(HEALTH_CONFIRM_CAP_MS, remaining || HEALTH_CONFIRM_CAP_MS),
      retryOnDisconnect: true,
      maxAttempts: PING_MAX_ATTEMPTS,
      retryIntervalMs: HEALTH_CONFIRM_PING_RETRY_MS,
    });
    const state = normalizeHealthState(res?.data);
    const ready =
      res?.ok === true &&
      state.listener_running &&
      (state.commands_ready || target?.connector_host !== HOST_KIND.Editor) &&
      !CONNECTOR_BUSY_STATES.has(state.connector_state);
    if (ready) return { ok: true, health: res.data, state, source: 'health' };
    last = { res, state };
    await sleep(POLL_INTERVAL_MS);
  }

  return {
    ok: false,
    error: 'connector_not_ready',
    error_code: 'EDITOR_NOT_READY',
    hint: 'Connector is not ready yet. Wait and retry.',
    connector_state: last?.state?.connector_state ?? null,
    play_mode: last?.state?.play_mode ?? PLAY_MODE.Edit,
  };
}

async function tryRecoverStaleReloadingHeartbeat(target, inst, deadline) {
  if (
    !inst ||
    readConnectorState(inst) !== CONNECTOR_STATE.Reloading ||
    inst[CONNECTOR_FIELD.ListenerRunning] === false
  ) {
    return null;
  }
  const remaining = deadline - Date.now();
  if (remaining <= 0) return null;
  const health = await confirmEditorHealth(target, inst, {
    timeoutMs: Math.min(HEALTH_CONFIRM_CAP_MS, remaining),
  });
  return health.ok ? { ok: true, instance: inst, health: health.data, stale_heartbeat: true } : null;
}

async function confirmReadyViaHealth(target, inst, deadline, capMs) {
  const health = await confirmEditorHealth(target, inst, {
    timeoutMs: Math.min(capMs, Math.max(0, deadline - Date.now())),
  });
  return health.ok ? { ok: true, instance: inst, health: health.data } : null;
}

/** Wait until local Editor state source reports commands ready. */
async function waitForLocalStateReady(target, { timeoutMs = DEFAULT_TIMEOUT_MS } = {}) {
  if (target?.connector_host && target.connector_host !== HOST_KIND.Editor) {
    return { ok: true };
  }

  const deadline = Date.now() + timeoutMs;
  let lastTimestamp = 0;
  let lastGeneration = null;
  let stableTicks = 0;

  while (Date.now() < deadline) {
    const inst = findInstanceByPort(target.port);
    const cache = readEditorHttpCache();

    if (inst?.[CONNECTOR_FIELD.Generation] != null) {
      if (lastGeneration != null && inst[CONNECTOR_FIELD.Generation] !== lastGeneration) {
        stableTicks = 0;
        lastTimestamp = 0;
      }
      lastGeneration = inst[CONNECTOR_FIELD.Generation];
    }

    if (inst) {
      if (!cacheMatchesInstance(target, inst)) {
        stableTicks = 0;
        lastTimestamp = 0;
        await sleep(POLL_INTERVAL_MS);
        continue;
      }

      if (isEditorInstanceBusy(inst)) {
        const confirmed = await confirmReadyViaHealth(target, inst, deadline, HEALTH_CONFIRM_CAP_MS);
        if (confirmed) return confirmed;
        const recovered = await tryRecoverStaleReloadingHeartbeat(target, inst, deadline);
        if (recovered) return recovered;
        stableTicks = 0;
        await sleep(POLL_INTERVAL_MS);
        continue;
      }

      const ts = Number(inst.timestamp) || 0;
      if (ts > lastTimestamp) {
        lastTimestamp = ts;
        stableTicks += 1;
      }

      if (stableTicks >= STABLE_TICKS_REQUIRED && isEditorInstanceReady(inst)) {
        const confirmed = await confirmReadyViaHealth(
          target,
          inst,
          deadline,
          HEALTH_CONFIRM_READY_CAP_MS,
        );
        if (confirmed) return confirmed;
        stableTicks = 0;
        lastTimestamp = 0;
        await sleep(POLL_INTERVAL_MS);
        continue;
      }
    } else if (
      cache?.port === target.port &&
      cache.status === EDITOR_HTTP_CACHE_STATUS.Running &&
      cache.session_id
    ) {
      const confirmed = await confirmReadyViaHealth(
        target,
        { [CONNECTOR_FIELD.SessionId]: cache.session_id, [CONNECTOR_FIELD.Generation]: cache.generation },
        deadline,
        HEALTH_CONFIRM_CAP_MS,
      );
      if (confirmed) return { ...confirmed, instance: null, from_cache: true };
    }

    await sleep(POLL_INTERVAL_MS);
  }

  const inst = findInstanceByPort(target.port);
  const recovered = await tryRecoverStaleReloadingHeartbeat(target, inst, deadline);
  if (recovered) return recovered;

  const cache = readEditorHttpCache();
  const connectorState = readConnectorState(inst);
  const hint =
    connectorState === CONNECTOR_STATE.Reloading ||
    cache?.status === EDITOR_HTTP_CACHE_STATUS.Stopped
      ? 'Editor HTTP is restarting after a domain reload. Retry in a few seconds or focus the Editor.'
      : 'Unity Editor may be compiling or reloading. Wait and retry, or focus the Editor window.';

  return {
    ok: false,
    error: 'editor_not_ready',
    error_code: 'EDITOR_NOT_READY',
    hint,
    connector_state: connectorState,
    play_mode: readPlayMode(inst),
    cache_status: cache?.status ?? null,
  };
}

/**
 * Unified connector readiness wait for all host kinds:
 * - editor: local state source (instance-file + cache + health confirm)
 * - editor_play/player: remote state source (health polling)
 */
export async function waitForConnectorReady(target, { timeoutMs = DEFAULT_TIMEOUT_MS } = {}) {
  if (target?.connector_host === HOST_KIND.Editor || !target?.connector_host) {
    return waitForLocalStateReady(target, { timeoutMs });
  }
  return waitForRemoteStateReady(target, { timeoutMs });
}

export async function waitForProfileReady({
  profile,
  timeoutMs = DEFAULT_TIMEOUT_MS,
  logProgress = true,
} = {}) {
  const profileName = profile ?? process.env.UNITY_CMD_PROFILE ?? null;
  if (!profileName) return null;

  const saved = loadProfile(profileName);
  if (!saved?.host || !saved?.port) return null;

  const hostKind = normalizeHostKind(saved.connector_host ?? HOST_KIND.Editor);
  const deadline = Date.now() + timeoutMs;
  let attempts = 0;

  while (Date.now() < deadline) {
    attempts += 1;
    if (logProgress && (attempts === 1 || attempts % 5 === 0)) {
      const remaining = Math.max(0, deadline - Date.now());
      console.log(
        `[connection] waiting profile=${profileName}, attempt=${attempts}, remaining=${remaining}ms`,
      );
    }

    const remainingMs = deadline - Date.now();
    if (remainingMs <= 0) break;

    if (hostKind === HOST_KIND.Editor) {
      const ready = await waitForLocalStateReady(
        { host: saved.host, port: saved.port, connector_host: HOST_KIND.Editor },
        { timeoutMs: Math.min(remainingMs, Math.floor(timeoutMs * 0.75)) },
      );
      if (!ready.ok) {
        await sleep(PROFILE_WAIT_INTERVAL_MS);
        continue;
      }
    }

    const verified = await resolveTarget({
      profile: profileName,
      timeoutMs: Math.min(HEALTH_CONFIRM_READY_CAP_MS, remainingMs),
      verify: true,
    });
    if (verified?.host) return verified;
    await sleep(PROFILE_WAIT_INTERVAL_MS);
  }

  return resolveTarget({ profile: profileName, timeoutMs: HEALTH_CONFIRM_CAP_MS, verify: true });
}

