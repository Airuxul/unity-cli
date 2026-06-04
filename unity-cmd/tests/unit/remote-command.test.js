import { test } from 'node:test';
import assert from 'node:assert/strict';
import { executeRemoteCommand, resolveProfileName } from '../../src/remote-command.js';
import {
  CONNECTOR_STATE,
  PLAY_MODE,
  MIN_CONNECTOR_BUILD,
  HOST_KIND,
  DEFAULT_EDITOR_PORT,
} from '../../src/constants.js';
import { checkMinConnectorBuild } from '../../src/runtime.js';

// ---------- helpers ----------------------------------------------------------

const TARGET = {
  host: '127.0.0.1',
  port: DEFAULT_EDITOR_PORT,
  connector_host: HOST_KIND.Editor,
  connector_build: MIN_CONNECTOR_BUILD,
};

const CATALOG_COMPILE = {
  catalog_version: 'v1',
  connector_build: MIN_CONNECTOR_BUILD,
  commands: [
    { name: 'compile', scope: 'editor', completion: 'compilation', allow_connection_retry: true },
    { name: 'echo', scope: 'any' },
  ],
  alias_to_command: { recompile: 'compile' },
  commands_by_name: {
    compile: { name: 'compile', scope: 'editor', completion: 'compilation', allow_connection_retry: true },
    echo:    { name: 'echo',    scope: 'any' },
  },
};

/** Deps where the editor is ready and the given command succeeds. */
function happyDeps(overrides = {}) {
  return {
    resolveTarget: async () => TARGET,
    checkMinConnectorBuild: () => null,
    loadCatalog: async () => CATALOG_COMPILE,
    waitForConnectorReady: async () => ({ ok: true }),
    waitForProfileReady: async () => TARGET,
    diagnoseReachability: async () => ({ reason: 'port_closed', hint: 'test' }),
    sendCommand: async (_t, cmd) => ({ ok: true, data: { compiled: true }, command: cmd }),
    ping: async () => ({ ok: true, data: { host: HOST_KIND.Editor } }),
    ...overrides,
  };
}

/** Deps where the editor reports the given connector_state (not ready). */
function busyEditorDeps(connectorState, playMode = PLAY_MODE.Edit) {
  return happyDeps({
    waitForConnectorReady: async () => ({
      ok: false,
      error: 'editor_not_ready',
      error_code: 'EDITOR_NOT_READY',
      hint: 'Unity Editor may be compiling or reloading.',
      connector_state: connectorState,
      play_mode: playMode,
    }),
  });
}

// ---------- resolveProfileName -----------------------------------------------

test('resolveProfileName: prefers --profile flag', () => {
  assert.equal(resolveProfileName({ profile: 'editor' }), 'editor');
});

test('resolveProfileName: falls back to UNITY_CMD_PROFILE env', () => {
  const prev = process.env.UNITY_CMD_PROFILE;
  process.env.UNITY_CMD_PROFILE = 'env-profile';
  try {
    assert.equal(resolveProfileName({}), 'env-profile');
  } finally {
    if (prev === undefined) delete process.env.UNITY_CMD_PROFILE;
    else process.env.UNITY_CMD_PROFILE = prev;
  }
});

test('resolveProfileName: returns null when nothing set', () => {
  const prev = process.env.UNITY_CMD_PROFILE;
  delete process.env.UNITY_CMD_PROFILE;
  try {
    assert.equal(resolveProfileName({}), null);
  } finally {
    if (prev !== undefined) process.env.UNITY_CMD_PROFILE = prev;
  }
});

// ---------- NO_PROFILE -------------------------------------------------------

test('no profile → exitCode 1, error_code NO_PROFILE', async () => {
  const prev = process.env.UNITY_CMD_PROFILE;
  delete process.env.UNITY_CMD_PROFILE;
  try {
    const r = await executeRemoteCommand('compile', {}, 20_000, happyDeps());
    assert.equal(r.exitCode, 1);
    assert.equal(r.json.error_code, 'NO_PROFILE');
  } finally {
    if (prev !== undefined) process.env.UNITY_CMD_PROFILE = prev;
  }
});

// ---------- NO_INSTANCE ------------------------------------------------------

test('unreachable target → exitCode 1, error_code NO_INSTANCE', async () => {
  const deps = happyDeps({ resolveTarget: async () => null });
  const r = await executeRemoteCommand('compile', { profile: 'editor' }, 20_000, deps);
  assert.equal(r.exitCode, 1);
  assert.equal(r.json.error_code, 'NO_INSTANCE');
});

// ---------- CONNECTOR_OUTDATED -----------------------------------------------

test('outdated connector_build → CONNECTOR_OUTDATED', async () => {
  const deps = happyDeps({
    resolveTarget: async () => ({
      ...TARGET,
      connector_build: MIN_CONNECTOR_BUILD - 1,
    }),
    checkMinConnectorBuild,
  });
  const r = await executeRemoteCommand('compile', { profile: 'editor' }, 20_000, deps);
  assert.equal(r.exitCode, 1);
  assert.equal(r.json.error_code, 'CONNECTOR_OUTDATED');
  assert.equal(r.json.connector_build, MIN_CONNECTOR_BUILD - 1);
});

test('current connector_build → passes version check', async () => {
  const deps = happyDeps({ checkMinConnectorBuild });
  const r = await executeRemoteCommand('compile', { profile: 'editor' }, 20_000, deps);
  assert.equal(r.exitCode, 0);
  assert.equal(r.json.ok, true);
});

// ---------- ping / list (no editor-ready check) ------------------------------

test('ping command → exitCode 0 when health ok', async () => {
  const r = await executeRemoteCommand('ping', { profile: 'editor' }, 20_000, happyDeps());
  assert.equal(r.exitCode, 0);
  assert.equal(r.json.ok, true);
});

test('ping command → exitCode 1 when health fails', async () => {
  const deps = happyDeps({
    ping: async () => ({ ok: false, status: 503, error: 'busy' }),
  });
  const r = await executeRemoteCommand('ping', { profile: 'editor' }, 20_000, deps);
  assert.equal(r.exitCode, 1);
  assert.equal(r.json.ok, false);
});

test('list command → returns catalog JSON', async () => {
  const r = await executeRemoteCommand('list', { profile: 'editor' }, 20_000, happyDeps());
  assert.equal(r.exitCode, 0);
  assert.equal(r.json.ok, true);
  assert.ok(Array.isArray(r.json.commands));
});

// ---------- EDITOR_NOT_READY — compiling -------------------------------------

test('editor compiling → exitCode 1, error_code EDITOR_NOT_READY, connector_state=compiling', async () => {
  const deps = busyEditorDeps(CONNECTOR_STATE.Compiling);
  const r = await executeRemoteCommand('compile', { profile: 'editor' }, 20_000, deps);
  assert.equal(r.exitCode, 1);
  assert.equal(r.json.error_code, 'EDITOR_NOT_READY');
  assert.equal(r.json.connector_state, CONNECTOR_STATE.Compiling);
});

test('editor reloading after compile error → error_code EDITOR_NOT_READY, connector_state=reloading', async () => {
  const deps = busyEditorDeps(CONNECTOR_STATE.Reloading);
  const r = await executeRemoteCommand('compile', { profile: 'editor' }, 20_000, deps);
  assert.equal(r.exitCode, 1);
  assert.equal(r.json.error_code, 'EDITOR_NOT_READY');
  assert.equal(r.json.connector_state, CONNECTOR_STATE.Reloading);
});

test('editor stopping → error_code EDITOR_NOT_READY, connector_state=stopped', async () => {
  const deps = busyEditorDeps(CONNECTOR_STATE.Stopped);
  const r = await executeRemoteCommand('echo', { profile: 'editor' }, 20_000, deps);
  assert.equal(r.exitCode, 1);
  assert.equal(r.json.error_code, 'EDITOR_NOT_READY');
  assert.equal(r.json.connector_state, CONNECTOR_STATE.Stopped);
});

// ---------- compile-fail → recover sequence (using deps injection) -----------

test('compile-fail then recover: second call succeeds', async () => {
  // First attempt: editor is reloading (compile error produced a domain reload)
  const failDeps = busyEditorDeps(CONNECTOR_STATE.Reloading);
  const fail = await executeRemoteCommand('compile', { profile: 'editor' }, 20_000, failDeps);
  assert.equal(fail.exitCode, 1);
  assert.equal(fail.json.error_code, 'EDITOR_NOT_READY');
  assert.equal(fail.json.connector_state, CONNECTOR_STATE.Reloading);

  // User fixes code; second attempt: editor is ready
  const successDeps = happyDeps();
  const success = await executeRemoteCommand('compile', { profile: 'editor' }, 20_000, successDeps);
  assert.equal(success.exitCode, 0);
  assert.equal(success.json.ok, true);
});

test('recompile while playing: play mode must not block commands', async () => {
  // Editor is ready and in play mode — compile should succeed (Editor host, not play host)
  const deps = happyDeps({
    waitForConnectorReady: async () => ({
      ok: true,
      play_mode: PLAY_MODE.Playing,
    }),
  });
  const r = await executeRemoteCommand('compile', { profile: 'editor' }, 20_000, deps);
  assert.equal(r.exitCode, 0);
  assert.equal(r.json.ok, true);
});

test('uses unified waitForConnectorReady hook when provided', async () => {
  let called = false;
  const deps = happyDeps({
    waitForConnectorReady: async () => {
      called = true;
      return { ok: true };
    },
  });
  const r = await executeRemoteCommand('compile', { profile: 'editor' }, 20_000, deps);
  assert.equal(r.exitCode, 0);
  assert.equal(called, true);
});

// ---------- sendCommand failures ---------------------------------------------

test('command returns ok=false → exitCode 1, enriched failure', async () => {
  const deps = happyDeps({
    sendCommand: async () => ({
      ok: false,
      status: 500,
      error: 'compile_error',
      data: null,
    }),
  });
  const r = await executeRemoteCommand('compile', { profile: 'editor' }, 20_000, deps);
  assert.equal(r.exitCode, 1);
  assert.equal(r.json.ok, false);
  assert.ok(r.json.error_code);
});

test('sendCommand throws connection error → exitCode 1, CONNECTION_FAILED', async () => {
  const deps = happyDeps({
    sendCommand: async () => { throw new TypeError('fetch failed'); },
  });
  const r = await executeRemoteCommand('compile', { profile: 'editor' }, 20_000, deps);
  assert.equal(r.exitCode, 1);
  assert.equal(r.json.error_code, 'CONNECTION_FAILED');
});

// ---------- SCOPE_MISMATCH ---------------------------------------------------

test('editor-scoped command on player host → SCOPE_MISMATCH', async () => {
  const deps = happyDeps({
    resolveTarget: async () => ({
      ...TARGET,
      connector_host: HOST_KIND.Player,
    }),
    loadCatalog: async () => ({
      ...CATALOG_COMPILE,
      commands: [
        ...CATALOG_COMPILE.commands,
        { name: 'screenshot', scope: 'editor', allow_connection_retry: true },
      ],
      commands_by_name: {
        ...CATALOG_COMPILE.commands_by_name,
        screenshot: { name: 'screenshot', scope: 'editor', allow_connection_retry: true },
      },
    }),
  });
  const r = await executeRemoteCommand('screenshot', { profile: 'package-play' }, 20_000, deps);
  assert.equal(r.exitCode, 1);
  assert.equal(r.json.error_code, 'SCOPE_MISMATCH');
});
