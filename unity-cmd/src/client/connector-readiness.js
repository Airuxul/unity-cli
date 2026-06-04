import { loadProfile, normalizeHostKind, resolveTarget, sleep } from './connection.js';
import { ping } from './command.js';
import {
  findInstanceByPort,
  findInstanceByProject,
  normalizeProjectPath,
} from './instances-io.js';
import {
  DEFAULT_TIMEOUT_MS,
  CONNECTOR_BUSY_STATES,
  CONNECTOR_FIELD,
  CONNECTOR_STATE,
  HEALTH_CONFIRM_CAP_MS,
  HEALTH_CONFIRM_READY_CAP_MS,
  HEALTH_CONFIRM_PING_RETRY_MS,
  HOST_KIND,
  PLAY_MODE,
  POLL_INTERVAL_MS,
  PROFILE_WAIT_INTERVAL_MS,
  PING_MAX_ATTEMPTS,
  STABLE_TICKS_REQUIRED,
} from '../constants.js';

export {
  hashProjectPath,
  readInstanceFile,
  findInstanceByPort,
  findInstanceByProject,
  normalizeProjectPath,
} from './instances-io.js';

/** Unity project root for instance heartbeat matching (integration sets UNITY_CMD_WORKSPACE). */
export function resolveWaitProjectPath() {
  const fromEnv = process.env.UNITY_CMD_WORKSPACE?.trim();
  return fromEnv && fromEnv.length > 0 ? fromEnv : process.cwd();
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
  if (inst.compile_errors === true) return true;
  const phase = inst.supervisor_phase;
  if (phase === 'Draining' || phase === 'Starting' || phase === 'BackoffForeign') return true;
  const cs = readConnectorState(inst);
  if (cs === CONNECTOR_STATE.Stopped || CONNECTOR_BUSY_STATES.has(cs)) return true;
  return !readCommandsReady(inst);
}

export function isEditorInstanceReady(inst) {
  return Boolean(inst) && !isEditorInstanceBusy(inst) && readCommandsReady(inst);
}

/** Instances heartbeat is the CLI readiness SSOT (editor-http.json is debug-only). */
export function instanceMatchesTarget(target, inst, projectPath) {
  if (!inst) return false;
  if (inst.port != null && target?.port != null && inst.port !== target.port) return false;
  if (projectPath && inst.projectPath) {
    return normalizeProjectPath(inst.projectPath) === normalizeProjectPath(projectPath);
  }
  return true;
}

export function findInstanceForTarget(target, projectPath) {
  const byPort = findInstanceByPort(target?.port);
  if (byPort && instanceMatchesTarget(target, byPort, projectPath)) return byPort;
  if (projectPath) {
    const byProject = findInstanceByProject(projectPath);
    if (byProject && instanceMatchesTarget(target, byProject, projectPath)) return byProject;
  }
  return byPort;
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
    inst?.[CONNECTOR_FIELD.SessionId] &&
    res.data[CONNECTOR_FIELD.SessionId] &&
    res.data[CONNECTOR_FIELD.SessionId] !== inst[CONNECTOR_FIELD.SessionId]
  ) {
    return { ok: false, reason: 'session_mismatch' };
  }
  if (
    inst?.[CONNECTOR_FIELD.Generation] != null &&
    res.data?.[CONNECTOR_FIELD.Generation] != null &&
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

function buildNotReadyFailure(inst) {
  const connectorState = readConnectorState(inst);
  const phase = inst?.supervisor_phase ?? null;
  let hint =
    'Unity Editor may be compiling or reloading. Run: unity-cmd --profile editor wait --timeout=120000';

  if (inst?.compile_errors === true) {
    hint =
      'Unity reported compile errors. Fix Console errors, recompile, then unity-cmd wait.';
  } else if (
    connectorState === CONNECTOR_STATE.Reloading ||
    phase === 'Draining' ||
    phase === 'Starting' ||
    inst?.http_status === 'stopped'
  ) {
    hint =
      'Editor HTTP is restarting (domain reload). Run: unity-cmd --profile editor wait --timeout=120000';
  }

  return {
    ok: false,
    error: 'editor_not_ready',
    error_code: 'EDITOR_NOT_READY',
    hint,
    connector_state: connectorState,
    play_mode: readPlayMode(inst),
    supervisor_phase: phase,
    compile_errors: inst?.compile_errors === true,
  };
}

/** Wait until local Editor instances heartbeat reports commands ready. */
async function waitForLocalStateReady(target, { timeoutMs = DEFAULT_TIMEOUT_MS, projectPath } = {}) {
  if (target?.connector_host && target.connector_host !== HOST_KIND.Editor) {
    return { ok: true };
  }

  const deadline = Date.now() + timeoutMs;
  let lastTimestamp = 0;
  let lastGeneration = null;
  let stableTicks = 0;

  while (Date.now() < deadline) {
    const inst = findInstanceForTarget(target, projectPath);

    if (inst?.[CONNECTOR_FIELD.Generation] != null) {
      if (lastGeneration != null && inst[CONNECTOR_FIELD.Generation] !== lastGeneration) {
        stableTicks = 0;
        lastTimestamp = 0;
      }
      lastGeneration = inst[CONNECTOR_FIELD.Generation];
    }

    if (inst) {
      if (!instanceMatchesTarget(target, inst, projectPath)) {
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
    }

    await sleep(POLL_INTERVAL_MS);
  }

  const inst = findInstanceForTarget(target, projectPath);
  const recovered = await tryRecoverStaleReloadingHeartbeat(target, inst, deadline);
  if (recovered) return recovered;

  return buildNotReadyFailure(inst);
}

/**
 * Unified connector readiness wait for all host kinds:
 * - editor: instances.json SSOT + /health confirm
 * - editor_play/player: remote /health polling
 */
export async function waitForConnectorReady(target, { timeoutMs = DEFAULT_TIMEOUT_MS, projectPath } = {}) {
  if (target?.connector_host === HOST_KIND.Editor || !target?.connector_host) {
    return waitForLocalStateReady(target, { timeoutMs, projectPath });
  }
  return waitForRemoteStateReady(target, { timeoutMs });
}

export async function waitForProfileReady({
  profile,
  timeoutMs = DEFAULT_TIMEOUT_MS,
  logProgress = true,
  projectPath = resolveWaitProjectPath(),
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
        {
          timeoutMs: Math.min(remainingMs, Math.floor(timeoutMs * 0.75)),
          projectPath,
        },
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
      projectPath,
    });
    if (verified?.host) return verified;
    await sleep(PROFILE_WAIT_INTERVAL_MS);
  }

  return resolveTarget({
    profile: profileName,
    timeoutMs: HEALTH_CONFIRM_CAP_MS,
    verify: true,
    projectPath,
  });
}
