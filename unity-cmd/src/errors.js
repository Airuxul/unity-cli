/** @typedef {'NO_INSTANCE' | 'NO_PROFILE' | 'CONNECTOR_OUTDATED' | 'CONNECTION_FAILED' | 'CATALOG_FETCH_FAILED' | 'SCOPE_MISMATCH' | 'COMMAND_FAILED' | 'COMMAND_NOT_FOUND' | 'HTTP_TIMEOUT' | 'UNKNOWN'} ErrorCode */

/**
 * @param {string} message
 * @param {ErrorCode} errorCode
 * @param {string} [hint]
 * @param {Record<string, unknown>} [extra]
 */
export function cliError(message, errorCode, hint, extra = {}) {
  return {
    ok: false,
    error: message,
    error_code: errorCode,
    hint: hint ?? null,
    ...extra,
  };
}

/**
 * @param {Record<string, unknown>} res
 * @param {{ command?: string, status?: number, hint?: string }} [context]
 */
export function enrichFailure(res, context = {}) {
  if (res?.ok !== false && res?.ok !== undefined && res.ok) return res;
  if (res?.error_code) return res;

  const error = String(res?.error ?? res?.message ?? 'unknown_error');
  const status = res?.status ?? context.status;

  let error_code = 'COMMAND_FAILED';
  let hint = null;

  if (error === 'connector_returned_202' || status === 202) {
    error_code = 'COMMAND_FAILED';
    hint =
      context.hint ??
      'Connector must complete commands in one POST (CONN-10). Recompile com.air.unity-connector.';
  } else if (error === 'command_not_found') {
    error_code = 'COMMAND_NOT_FOUND';
    hint =
      'Command not in ledger. Run unity-cmd wait after domain reload, then re-issue the command.';
  } else if (error === 'failed' || error === 'orphaned') {
    error_code = 'COMMAND_FAILED';
    hint = 'Run unity-cmd --profile editor console --type error,warning';
  } else if (status === 404) {
    hint = 'Run unity-cmd --profile <name> list --refresh-catalog';
  } else if (status >= 500) {
    hint = 'Check Unity Editor console and try unity-cmd --profile editor wait';
  }

  return {
    ...res,
    error,
    error_code,
    hint: context.hint ?? hint,
  };
}

/**
 * @param {unknown} err
 * @param {{ command?: string }} [context]
 */
export function enrichThrown(err, context = {}) {
  const message = err instanceof Error ? err.message : String(err);
  let error_code = 'UNKNOWN';
  let hint = null;

  if (err?.name === 'AbortError' || message.includes('aborted')) {
    error_code = 'HTTP_TIMEOUT';
    hint = 'Increase --timeout or UNITY_CMD_TIMEOUT_MS.';
  } else if (message.includes('fetch failed') || message.includes('ECONNREFUSED')) {
    error_code = 'CONNECTION_FAILED';
    hint =
      'Endpoint unreachable. Run unity-cmd wait, then ping. Re-issue the command after Editor is ready.';
  } else if (message.includes('command catalog')) {
    error_code = 'CATALOG_FETCH_FAILED';
    hint = 'Try --profile <name> ping then list --refresh-catalog';
  }

  return cliError(message, error_code, hint, { command: context.command ?? null });
}
