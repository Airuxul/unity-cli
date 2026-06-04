import os from 'node:os';
import path from 'node:path';

/**
 * Shared protocol constants for unity-cmd.
 * Keep in sync with com.air.unity-connector:
 * - Runtime/Host/HostPorts.cs
 * - Runtime/State/ConnectorPipelineState.cs
 * - Runtime/State/PlayModeState.cs
 * - Runtime/Connector/Http/ConnectorMainThreadScheduler.cs (HTTP errors)
 */

export const MIN_CONNECTOR_BUILD = 40;

// --- Timeouts (ms) ---
export const DEFAULT_TIMEOUT_MS = 20_000;
export const HEALTH_CONFIRM_CAP_MS = 3_000;
export const HEALTH_CONFIRM_READY_CAP_MS = 5_000;
export const POLL_INTERVAL_MS = 200;
export const PROFILE_WAIT_INTERVAL_MS = 300;
export const SEND_COMMAND_RETRY_INTERVAL_MS = 300;
export const PING_RETRY_INTERVAL_MS = 3_000;
export const PING_MAX_ATTEMPTS = 3;
export const STABLE_TICKS_REQUIRED = 2;
export const RESOLVE_TARGET_RETRY_INTERVAL_MS = 300;
/** Per-attempt cap for /health during resolveTarget (avoids multi-minute hangs). */
export const RESOLVE_TARGET_HEALTH_TIMEOUT_MS = 1_500;

/** Editor ready wait caps applied before sendCommand (see remote-command.js). */
export const EDITOR_READY_WAIT_CAP_MS = 7_000;
export const EDITOR_READY_WAIT_CAP_NO_RETRY_MS = 6_000;
export const EDITOR_READY_WAIT_FLOOR_MS = 1_000;
export const INTEGRATION_PLAYER_PROBE_MS = 3_000;
export const INTEGRATION_ATTACH_TIMEOUT_MS = DEFAULT_TIMEOUT_MS;
export const INTEGRATION_STEP_SLEEP_MS = PROFILE_WAIT_INTERVAL_MS;

/** Shared deadline floor (resolveTarget, catalog, sendCommand budget). */
export const COMMAND_BUDGET_FLOOR_MS = 1_000;
/** Fast retry for transient connection errors and health confirm pings. */
export const CONNECTION_RETRY_INTERVAL_MS = 400;
export const CONNECTION_RETRY_MIN_MS = 500;
export const CONNECTION_RETRY_FLOOR_MS = 100;
export const HEALTH_CONFIRM_PING_RETRY_MS = CONNECTION_RETRY_INTERVAL_MS;

// --- Ports (HostPorts.cs) ---
export const DEFAULT_EDITOR_PORT = 6547;
export const DEFAULT_EDITOR_PLAY_PORT = 6794;
export const DEFAULT_PLAYER_PORT = 6795;

// --- Host kinds ---
export const HOST_KIND = {
  Editor: 'editor',
  EditorPlay: 'editor_play',
  Player: 'player',
};

export const PROFILE_BY_HOST_KIND = {
  [HOST_KIND.Editor]: 'editor',
  [HOST_KIND.EditorPlay]: 'editor-play',
  [HOST_KIND.Player]: 'package-play',
};

// --- Connector pipeline state (ConnectorPipelineState) ---
export const CONNECTOR_STATE = {
  Ready: 'ready',
  Reloading: 'reloading',
  Compiling: 'compiling',
  Refreshing: 'refreshing',
  Stopped: 'stopped',
  EnteringPlayMode: 'entering_playmode',
};

export const CONNECTOR_BUSY_STATES = new Set([
  CONNECTOR_STATE.Compiling,
  CONNECTOR_STATE.Reloading,
  CONNECTOR_STATE.Refreshing,
  CONNECTOR_STATE.EnteringPlayMode,
  CONNECTOR_STATE.Stopped,
]);

// --- Unity Play Mode (PlayModeState) ---
export const PLAY_MODE = {
  Edit: 'edit',
  Playing: 'playing',
  Paused: 'paused',
};

// --- HTTP 503 error tokens (ConnectorMainThreadScheduler) ---
export const CONNECTOR_HTTP_ERROR = {
  Busy: 'busy',
  Reloading: 'reloading',
};

export const RETRYABLE_POST_ERRORS = new Set([
  CONNECTOR_HTTP_ERROR.Reloading,
  CONNECTOR_HTTP_ERROR.Busy,
]);

export const CONNECTOR_HTTP_ERRORS = {
  SERVER_BUSY: { status: 503, error: CONNECTOR_HTTP_ERROR.Busy },
  DOMAIN_RELOADING: { status: 503, error: CONNECTOR_HTTP_ERROR.Reloading },
};

// --- Instance / health JSON fields ---
export const CONNECTOR_FIELD = {
  ConnectorState: 'connector_state',
  PlayMode: 'play_mode',
  CommandsReady: 'commands_ready',
  BlockingReasons: 'blocking_reasons',
  ListenerRunning: 'listener_running',
  SessionId: 'session_id',
  Generation: 'generation',
};

// --- Paths under ~/.unity-cmd ---
export const UNITY_CMD_HOME = path.join(os.homedir(), '.unity-cmd');
export const INSTANCES_DIR = path.join(UNITY_CMD_HOME, 'instances');
export const PROFILES_DIR = path.join(UNITY_CMD_HOME, 'profiles');
export const CACHE_DIR = path.join(UNITY_CMD_HOME, 'cache');
export const CATALOG_TTL_MS = 24 * 60 * 60 * 1000;

export const DEFAULT_PORT_BY_HOST_KIND = {
  [HOST_KIND.Editor]: DEFAULT_EDITOR_PORT,
  [HOST_KIND.EditorPlay]: DEFAULT_EDITOR_PLAY_PORT,
  [HOST_KIND.Player]: DEFAULT_PLAYER_PORT,
};

export function formatDefaultPortsLine() {
  return `editor ${DEFAULT_EDITOR_PORT}, editor_play ${DEFAULT_EDITOR_PLAY_PORT}, player ${DEFAULT_PLAYER_PORT}`;
}

/** @param {string} name @param {string} [hostKind] @param {string} [host] */
export function formatProfileCreateExample(name, hostKind = HOST_KIND.Editor, host = '127.0.0.1') {
  const port = DEFAULT_PORT_BY_HOST_KIND[hostKind] ?? DEFAULT_EDITOR_PORT;
  return `unity-cmd profile create ${name} --host ${host} --port ${port} --host-kind ${hostKind}`;
}
