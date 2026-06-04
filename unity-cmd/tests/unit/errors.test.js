import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cliError, enrichFailure, enrichThrown } from '../../src/errors.js';

test('cliError includes code and hint', () => {
  const e = cliError('msg', 'NO_INSTANCE', 'open unity');
  assert.equal(e.ok, false);
  assert.equal(e.error_code, 'NO_INSTANCE');
  assert.equal(e.hint, 'open unity');
});

test('enrichFailure maps HTTP 202 to COMMAND_FAILED', () => {
  const e = enrichFailure({ ok: false, status: 202, error: 'connector_returned_202' });
  assert.equal(e.error_code, 'COMMAND_FAILED');
  assert.match(e.hint, /CONN-10|POST/i);
});

test('enrichThrown maps connection refused', () => {
  const e = enrichThrown(new TypeError('fetch failed'), { command: 'ping' });
  assert.equal(e.error_code, 'CONNECTION_FAILED');
  assert.match(e.hint, /wait/i);
});
