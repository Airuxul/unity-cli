import { test } from 'node:test';
import assert from 'node:assert/strict';
import { sendCommand } from '../../src/client/command.js';

const originalFetch = global.fetch;

test('sendCommand: sync POST returns 200 without GET poll', async () => {
  const urls = [];
  global.fetch = async (url, init) => {
    urls.push(String(url));
    assert.equal(init?.method, 'POST');
    return {
      ok: true,
      status: 200,
      text: async () =>
        JSON.stringify({
          ok: true,
          data: { compiled: true },
          command_id: 'cmd-sync',
          request_id: 'r1',
        }),
    };
  };

  const res = await sendCommand(
    { host: '127.0.0.1', port: 6547, connector_host: 'editor' },
    'compile',
    {},
    { timeoutMs: 5_000, allowConnectionRetry: false },
  );

  assert.equal(res.ok, true);
  assert.equal(urls.length, 1);
  assert.ok(urls[0].endsWith('/command'));
  global.fetch = originalFetch;
});

test('sendCommand: HTTP 202 is rejected', async () => {
  global.fetch = async () => ({
    ok: true,
    status: 202,
    text: async () =>
      JSON.stringify({ ok: true, command_id: 'cmd-bad', request_id: 'r1' }),
  });

  const res = await sendCommand(
    { host: '127.0.0.1', port: 6547 },
    'compile',
    {},
    { timeoutMs: 5_000, allowConnectionRetry: false },
  );

  assert.equal(res.ok, false);
  assert.match(String(res.error), /202/);
  global.fetch = originalFetch;
});

test('sendCommand retries POST 503 reloading until 200', async () => {
  let postAttempts = 0;
  global.fetch = async (url, init) => {
    if (init?.method === 'POST') {
      postAttempts += 1;
      if (postAttempts < 3) {
        return {
          ok: true,
          status: 503,
          text: async () => JSON.stringify({ ok: false, error: 'reloading' }),
        };
      }
      return {
        ok: true,
        status: 200,
        text: async () =>
          JSON.stringify({
            ok: true,
            data: { compiled: true },
            command_id: 'cmd-after-reload',
          }),
      };
    }
    throw new Error('unexpected non-POST');
  };

  const res = await sendCommand(
    { host: '127.0.0.1', port: 6547 },
    'compile',
    {},
    { timeoutMs: 10_000, allowConnectionRetry: true },
  );

  assert.equal(res.ok, true);
  assert.equal(postAttempts, 3);
  global.fetch = originalFetch;
});
