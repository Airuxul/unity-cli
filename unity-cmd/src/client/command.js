import { requestJson } from './http.js';
import { resolveTimeoutMs } from '../runtime.js';
import { enrichFailure } from '../errors.js';
import { sleep } from './connection.js';
import {
  CONNECTOR_HTTP_ERROR,
  PING_MAX_ATTEMPTS,
  PING_RETRY_INTERVAL_MS,
  RETRYABLE_POST_ERRORS,
  SEND_COMMAND_RETRY_INTERVAL_MS,
} from '../constants.js';

export async function sendCommand(target, command, parameters = {}, options = {}) {
  options.command = command;
  const commandTimeoutMs = resolveTimeoutMs(options.timeoutMs);
  const deadline = Date.now() + commandTimeoutMs;
  const baseUrl = `http://${target.host}:${target.port}`;
  const body = {
    command,
    parameters,
    request_id: options.requestId,
  };

  const retry = options.allowConnectionRetry !== false;
  let lastResult = null;

  while (Date.now() < deadline) {
    const postTimeoutMs = Math.max(500, deadline - Date.now());

    let status;
    let data;
    try {
      ({ status, data } = await requestJson(`${baseUrl}/command`, {
        method: 'POST',
        body,
        timeoutMs: postTimeoutMs,
        retryOnDisconnect: retry,
      }));
    } catch (err) {
      if (!retry) throw err;
      await sleep(SEND_COMMAND_RETRY_INTERVAL_MS);
      continue;
    }

    if (status === 202) {
      return enrichFailure(
        {
          ok: false,
          status: 202,
          error: 'connector_returned_202',
          command_id: data?.command_id,
          request_id: data?.request_id,
        },
        {
          command: options.command,
          hint:
            'HTTP 202 is not supported. Recompile com.air.unity-connector and use unity-cmd wait before commands.',
        },
      );
    }

    const result = {
      ok: data?.ok ?? (status >= 200 && status < 300),
      status,
      data: data?.data,
      error: data?.error,
      request_id: data?.request_id,
      command_id: data?.command_id,
    };
    lastResult = result;

    if (
      retry &&
      status === 503 &&
      RETRYABLE_POST_ERRORS.has(String(data?.error ?? ''))
    ) {
      await sleep(SEND_COMMAND_RETRY_INTERVAL_MS);
      continue;
    }

    if (!result.ok && result.error === 'hold_without_pending_http') {
      return enrichFailure(result, {
        command: options.command,
        hint: 'Connector HTTP hold is misconfigured. Recompile com.air.unity-connector.',
      });
    }

    return result.ok ? result : enrichFailure(result, { status, command: options.command });
  }

  if (lastResult) {
    return enrichFailure(lastResult, { status: lastResult.status, command: options.command });
  }

  return enrichFailure(
    { ok: false, status: 503, error: CONNECTOR_HTTP_ERROR.Reloading },
    { status: 503, command: options.command },
  );
}

async function pingOnce(target, options = {}) {
  const timeoutMs = resolveTimeoutMs(options.timeoutMs);
  const baseUrl = `http://${target.host}:${target.port}`;
  const { status, data } = await requestJson(`${baseUrl}/health`, {
    timeoutMs,
    retryOnDisconnect: false,
  });
  return { ok: status === 200 && data?.ok, status, data };
}

/** @param {{ timeoutMs?: number, retryOnDisconnect?: boolean, maxAttempts?: number, retryIntervalMs?: number }} options */
export async function ping(target, options = {}) {
  const maxAttempts =
    options.maxAttempts ??
    (options.retryOnDisconnect === false ? 1 : PING_MAX_ATTEMPTS);
  const intervalMs = options.retryIntervalMs ?? PING_RETRY_INTERVAL_MS;

  let last;
  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    last = await pingOnce(target, options);
    if (last.ok) return last;
    if (attempt < maxAttempts) await sleep(intervalMs);
  }
  return last;
}

export async function fetchCatalog(target, options = {}) {
  const timeoutMs = resolveTimeoutMs(options.timeoutMs);
  const baseUrl = `http://${target.host}:${target.port}`;
  const { status, data } = await requestJson(`${baseUrl}/list`, {
    method: 'POST',
    body: {},
    timeoutMs,
    retryOnDisconnect: options.retryOnDisconnect !== false,
  });
  return {
    ok: status === 200 && data?.ok !== false,
    status,
    catalog_version: data?.catalog_version ?? null,
    connector_build: data?.connector_build ?? null,
    commands: data?.commands ?? [],
    alias_to_command: data?.alias_to_command ?? {},
  };
}

/** Single GET /commands/{id} (read-only ledger); not used for POST completion. */
export async function fetchCommandStatus(target, commandId, options = {}) {
  const timeoutMs = resolveTimeoutMs(options.timeoutMs);
  const baseUrl = `http://${target.host}:${target.port}`;
  const { status, data } = await requestJson(`${baseUrl}/commands/${commandId}`, {
    timeoutMs,
    retryOnDisconnect: options.retryOnDisconnect !== false,
  });
  if (status === 404) {
    return {
      ok: false,
      status: 404,
      error: 'command_not_found',
      error_code: 'COMMAND_NOT_FOUND',
      data,
    };
  }
  const terminal = data?.status === 'succeeded';
  const failed = data?.status === 'failed' || data?.status === 'orphaned';
  return {
    ok: terminal,
    status,
    data,
    error: failed ? (data?.error ?? data?.status) : null,
    error_code: failed ? 'COMMAND_FAILED' : null,
  };
}
