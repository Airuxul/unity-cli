import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  isEditorInstanceBusy,
  isEditorInstanceReady,
  instanceMatchesTarget,
  normalizeHealthState,
  readPlayMode,
  readCommandsReady,
  readConnectorState,
  resolveWaitProjectPath,
} from '../../src/client/connector-readiness.js';
import {
  CONNECTOR_BUSY_STATES,
  CONNECTOR_STATE,
  CONNECTOR_FIELD,
  PLAY_MODE,
  DEFAULT_EDITOR_PORT,
} from '../../src/constants.js';

// ---------- helpers ----------------------------------------------------------

/** Build a minimal health/instance snapshot. */
function inst(overrides = {}) {
  return {
    [CONNECTOR_FIELD.ConnectorState]: CONNECTOR_STATE.Ready,
    [CONNECTOR_FIELD.CommandsReady]: true,
    [CONNECTOR_FIELD.ListenerRunning]: true,
    [CONNECTOR_FIELD.PlayMode]: PLAY_MODE.Edit,
    port: DEFAULT_EDITOR_PORT,
    timestamp: Date.now(),
    ...overrides,
  };
}

// ---------- CONNECTOR_BUSY_STATES --------------------------------------------

test('CONNECTOR_BUSY_STATES includes all blocking states', () => {
  assert.equal(CONNECTOR_BUSY_STATES.has(CONNECTOR_STATE.Compiling), true);
  assert.equal(CONNECTOR_BUSY_STATES.has(CONNECTOR_STATE.Reloading), true);
  assert.equal(CONNECTOR_BUSY_STATES.has(CONNECTOR_STATE.Refreshing), true);
  assert.equal(CONNECTOR_BUSY_STATES.has(CONNECTOR_STATE.EnteringPlayMode), true);
  assert.equal(CONNECTOR_BUSY_STATES.has(CONNECTOR_STATE.Stopped), true);
});

test('CONNECTOR_BUSY_STATES does not include play-mode states', () => {
  assert.equal(CONNECTOR_BUSY_STATES.has(PLAY_MODE.Playing), false);
  assert.equal(CONNECTOR_BUSY_STATES.has(PLAY_MODE.Paused), false);
  assert.equal(CONNECTOR_BUSY_STATES.has(CONNECTOR_STATE.Ready), false);
});

// ---------- readConnectorState -----------------------------------------------

test('readConnectorState returns connector_state field', () => {
  assert.equal(readConnectorState(inst()), CONNECTOR_STATE.Ready);
  assert.equal(readConnectorState(inst({ connector_state: CONNECTOR_STATE.Compiling })), CONNECTOR_STATE.Compiling);
});

test('readConnectorState returns null when instance is null', () => {
  assert.equal(readConnectorState(null), null);
});

// ---------- readPlayMode -----------------------------------------------------

test('readPlayMode returns play_mode field', () => {
  assert.equal(readPlayMode(inst({ play_mode: PLAY_MODE.Playing })), PLAY_MODE.Playing);
  assert.equal(readPlayMode(inst({ play_mode: PLAY_MODE.Paused })), PLAY_MODE.Paused);
  assert.equal(readPlayMode(inst({ play_mode: PLAY_MODE.Edit })), PLAY_MODE.Edit);
});

test('readPlayMode defaults to edit when instance is null', () => {
  assert.equal(readPlayMode(null), PLAY_MODE.Edit);
});

// ---------- readCommandsReady ------------------------------------------------

test('readCommandsReady returns true only when commands_ready=true', () => {
  assert.equal(readCommandsReady(inst()), true);
  assert.equal(readCommandsReady(inst({ commands_ready: false })), false);
  assert.equal(readCommandsReady(inst({ commands_ready: undefined })), false);
});

test('normalizeHealthState maps health payload to unified state shape', () => {
  const state = normalizeHealthState({
    [CONNECTOR_FIELD.ConnectorState]: CONNECTOR_STATE.Compiling,
    [CONNECTOR_FIELD.PlayMode]: PLAY_MODE.Playing,
    [CONNECTOR_FIELD.CommandsReady]: false,
    [CONNECTOR_FIELD.ListenerRunning]: true,
    [CONNECTOR_FIELD.BlockingReasons]: ['compiling'],
    [CONNECTOR_FIELD.SessionId]: 'session-1',
    [CONNECTOR_FIELD.Generation]: 42,
  });
  assert.equal(state.connector_state, CONNECTOR_STATE.Compiling);
  assert.equal(state.play_mode, PLAY_MODE.Playing);
  assert.equal(state.commands_ready, false);
  assert.equal(state.listener_running, true);
  assert.deepEqual(state.blocking_reasons, ['compiling']);
  assert.equal(state.session_id, 'session-1');
  assert.equal(state.generation, 42);
});

// ---------- isEditorInstanceBusy — normal states ----------------------------

test('ready instance is not busy', () => {
  assert.equal(isEditorInstanceBusy(inst()), false);
});

test('null instance is busy', () => {
  assert.equal(isEditorInstanceBusy(null), true);
});

test('listener_running=false is busy regardless of connector_state', () => {
  assert.equal(
    isEditorInstanceBusy(inst({ listener_running: false })),
    true,
  );
  assert.equal(
    isEditorInstanceBusy(inst({ connector_state: CONNECTOR_STATE.Ready, listener_running: false })),
    true,
  );
});

test('supervisor Draining/Starting phases are busy', () => {
  assert.equal(isEditorInstanceBusy(inst({ supervisor_phase: 'Draining' })), true);
  assert.equal(isEditorInstanceBusy(inst({ supervisor_phase: 'Starting' })), true);
});

test('compile_errors on instance heartbeat is busy', () => {
  assert.equal(isEditorInstanceBusy(inst({ compile_errors: true })), true);
});

test('instanceMatchesTarget checks port and optional project path', () => {
  const target = { port: DEFAULT_EDITOR_PORT };
  assert.equal(
    instanceMatchesTarget(target, inst({ port: DEFAULT_EDITOR_PORT }), null),
    true,
  );
  assert.equal(
    instanceMatchesTarget(target, inst({ port: 6794 }), null),
    false,
  );
  assert.equal(
    instanceMatchesTarget(
      target,
      inst({ port: DEFAULT_EDITOR_PORT, projectPath: 'C:/Project/GameDemo' }),
      'C:\\Project\\GameDemo\\',
    ),
    true,
  );
});

test('resolveWaitProjectPath prefers UNITY_CMD_WORKSPACE over cwd', () => {
  const prev = process.env.UNITY_CMD_WORKSPACE;
  process.env.UNITY_CMD_WORKSPACE = 'C:/Project/GameDemo';
  try {
    assert.equal(resolveWaitProjectPath(), 'C:/Project/GameDemo');
  } finally {
    if (prev === undefined) delete process.env.UNITY_CMD_WORKSPACE;
    else process.env.UNITY_CMD_WORKSPACE = prev;
  }
});

// ---------- isEditorInstanceBusy — compile-fail / reloading -----------------

test('compiling state is busy', () => {
  assert.equal(
    isEditorInstanceBusy(inst({
      connector_state: CONNECTOR_STATE.Compiling,
      commands_ready: false,
    })),
    true,
  );
});

test('reloading state is busy (domain-reload after compile error)', () => {
  assert.equal(
    isEditorInstanceBusy(inst({
      connector_state: CONNECTOR_STATE.Reloading,
      commands_ready: false,
    })),
    true,
  );
});

test('refreshing state is busy', () => {
  assert.equal(
    isEditorInstanceBusy(inst({
      connector_state: CONNECTOR_STATE.Refreshing,
      commands_ready: false,
    })),
    true,
  );
});

test('stopped state is busy', () => {
  assert.equal(
    isEditorInstanceBusy(inst({
      connector_state: CONNECTOR_STATE.Stopped,
      commands_ready: false,
      listener_running: false,
    })),
    true,
  );
});

test('entering_playmode is busy', () => {
  assert.equal(
    isEditorInstanceBusy(inst({
      connector_state: CONNECTOR_STATE.EnteringPlayMode,
      commands_ready: false,
    })),
    true,
  );
});

// ---------- isEditorInstanceBusy — recovery sequence ------------------------

test('state machine: compile-fail then recover to ready', () => {
  // Phase 1: compile starts — connector moves to compiling
  const compiling = inst({
    connector_state: CONNECTOR_STATE.Compiling,
    commands_ready: false,
  });
  assert.equal(isEditorInstanceBusy(compiling), true);
  assert.equal(isEditorInstanceReady(compiling), false);

  // Phase 2: compile fails, domain reload begins
  const reloading = inst({
    connector_state: CONNECTOR_STATE.Reloading,
    commands_ready: false,
  });
  assert.equal(isEditorInstanceBusy(reloading), true);
  assert.equal(isEditorInstanceReady(reloading), false);

  // Phase 3: user fixes code, second compile runs
  const recompiling = inst({
    connector_state: CONNECTOR_STATE.Compiling,
    commands_ready: false,
  });
  assert.equal(isEditorInstanceBusy(recompiling), true);
  assert.equal(isEditorInstanceReady(recompiling), false);

  // Phase 4: compile succeeds, connector is ready
  const recovered = inst({
    connector_state: CONNECTOR_STATE.Ready,
    commands_ready: true,
    listener_running: true,
  });
  assert.equal(isEditorInstanceBusy(recovered), false);
  assert.equal(isEditorInstanceReady(recovered), true);
});

// ---------- isEditorInstanceBusy — play mode does not block ------------------

test('playing while ready is NOT busy (play mode orthogonal to connector state)', () => {
  const playing = inst({
    connector_state: CONNECTOR_STATE.Ready,
    play_mode: PLAY_MODE.Playing,
    commands_ready: true,
    listener_running: true,
  });
  assert.equal(isEditorInstanceBusy(playing), false);
  assert.equal(isEditorInstanceReady(playing), true);
});

test('paused while ready is NOT busy', () => {
  const paused = inst({
    connector_state: CONNECTOR_STATE.Ready,
    play_mode: PLAY_MODE.Paused,
    commands_ready: true,
    listener_running: true,
  });
  assert.equal(isEditorInstanceBusy(paused), false);
  assert.equal(isEditorInstanceReady(paused), true);
});

// ---------- commands_ready=false even when connector_state is ready ----------

test('ready state but commands_ready=false is still busy', () => {
  const notReady = inst({
    connector_state: CONNECTOR_STATE.Ready,
    commands_ready: false,
  });
  assert.equal(isEditorInstanceBusy(notReady), true);
  assert.equal(isEditorInstanceReady(notReady), false);
});
