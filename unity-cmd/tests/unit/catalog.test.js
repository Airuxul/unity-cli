import { test } from 'node:test';
import assert from 'node:assert/strict';
import { resolveRemoteCommand } from '../../src/catalog.js';

const MOCK_CATALOG = {
  catalog_version: 'test',
  commands: [
    {
      name: 'compile',
      is_job: true,
      completion: 'compilation',
      aliases: ['recompile', 'reload', 'editor.recompile'],
      default_timeout_ms: 120000,
      allow_connection_retry: true,
    },
    {
      name: 'editor.play',
      is_job: true,
      default_timeout_ms: 60000,
      allow_connection_retry: true,
    },
    {
      name: 'ping',
      is_job: false,
      allow_connection_retry: false,
    },
    {
      name: 'refresh',
      is_job: false,
      allow_connection_retry: false,
    },
  ],
  commands_by_name: {},
  alias_to_command: {
    recompile: 'compile',
    reload: 'compile',
    'editor.recompile': 'compile',
  },
};

for (const entry of MOCK_CATALOG.commands) {
  MOCK_CATALOG.commands_by_name[entry.name] = entry;
}

test('recompile aliases map to compile job', () => {
  for (const alias of ['recompile', 'reload', 'editor.recompile']) {
    const r = resolveRemoteCommand(alias, {}, MOCK_CATALOG);
    assert.equal(r.command, 'compile');
    assert.equal(r.allowConnectionRetry, true);
    assert.equal(r.minTimeoutMs, 120_000);
  }
});

test('compile gets long timeout and retry', () => {
  const r = resolveRemoteCommand('compile', {}, MOCK_CATALOG);
  assert.equal(r.command, 'compile');
  assert.equal(r.allowConnectionRetry, true);
  assert.equal(r.minTimeoutMs, 120_000);
});

test('ping is unchanged', () => {
  const r = resolveRemoteCommand('ping', {}, MOCK_CATALOG);
  assert.equal(r.command, 'ping');
  assert.equal(r.allowConnectionRetry, false);
  assert.equal(r.minTimeoutMs, null);
});

test('editor.play gets retry and 60s min timeout', () => {
  const r = resolveRemoteCommand('editor.play', {}, MOCK_CATALOG);
  assert.equal(r.allowConnectionRetry, true);
  assert.equal(r.minTimeoutMs, 60_000);
});

test('refresh with compile flag uses compile timeout', () => {
  const r = resolveRemoteCommand('refresh', { compile: true }, MOCK_CATALOG);
  assert.equal(r.command, 'refresh');
  assert.equal(r.allowConnectionRetry, true);
  assert.equal(r.minTimeoutMs, 120_000);
});

test('bootstrap aliases without catalog entries', () => {
  const empty = {
    commands: [],
    commands_by_name: {},
    alias_to_command: {},
  };
  const r = resolveRemoteCommand('console', {}, empty);
  assert.equal(r.command, 'editor.console');
  const logs = resolveRemoteCommand('logs', {}, empty);
  assert.equal(logs.command, 'editor.console');
  assert.equal(resolveRemoteCommand('exec', {}, empty).command, 'editor.exec');
  assert.equal(resolveRemoteCommand('profiler', {}, empty).command, 'editor.profiler');
});
