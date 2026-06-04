import { sendCommand, ping } from './client/command.js';
import { loadProfile, resolveTarget } from './client/connection.js';
import { waitForConnectorReady, waitForProfileReady } from './client/connector-readiness.js';
import { buildReachabilityDiagnostics } from './client/instance-diagnostics.js';
import { createCommandBudget, checkMinConnectorBuild } from './runtime.js';
import {
  loadCatalog,
  resolveRemoteCommand,
  checkScopeCompatibility,
  cachePathForTarget,
  isCatalogExpired,
} from './catalog.js';
import { coerceParameters } from './params.js';
import { cliError, enrichFailure, enrichThrown } from './errors.js';
import { cliResult } from './cli-result.js';
import {
  CATALOG_TTL_MS,
  EDITOR_READY_WAIT_CAP_MS,
  EDITOR_READY_WAIT_CAP_NO_RETRY_MS,
  EDITOR_READY_WAIT_FLOOR_MS,
  formatProfileCreateExample,
} from './constants.js';

const defaultDeps = {
  ping,
  resolveTarget,
  waitForConnectorReady,
  waitForProfileReady,
  loadCatalog,
  sendCommand,
  checkMinConnectorBuild,
  diagnoseReachability: buildReachabilityDiagnostics,
};

export function resolveProfileName(flags) {
  return flags.profile ?? process.env.UNITY_CMD_PROFILE ?? null;
}

/**
 * Run a remote command (ping, list, or connector command). Testable via `deps` overrides.
 * @param {string} command
 * @param {object} flags
 * @param {number} timeoutMs
 * @param {typeof defaultDeps} [deps]
 * @returns {Promise<import('./cli-result.js').CliResult>}
 */
export async function executeRemoteCommand(command, flags, timeoutMs, deps = defaultDeps) {
  const profileName = resolveProfileName(flags);
  if (!profileName) {
    return cliResult(1, {
      json: cliError(
        'Profile is required. Use --profile <name> or set UNITY_CMD_PROFILE.',
        'NO_PROFILE',
        `Create one: ${formatProfileCreateExample('editor')}`,
      ),
    });
  }

  const budget = createCommandBudget(timeoutMs);
  const target = await deps.resolveTarget({
    profile: profileName,
    timeoutMs: budget.remaining(),
  });
  if (!target) {
    const saved = loadProfile(profileName);
    let diagnostics = null;
    if (saved) {
      try {
        diagnostics = await deps.diagnoseReachability({
          profile: profileName,
          host: saved.host,
          port: saved.port,
          connector_host: saved.connector_host,
          projectPath: process.cwd(),
        });
      } catch {
        diagnostics = null;
      }
    }
    return cliResult(1, {
      json: cliError(
        `Profile "${profileName}" is unreachable or /health host mismatch.`,
        'NO_INSTANCE',
        diagnostics?.hint ??
          `Check the profile file or run: unity-cmd profile show ${profileName}`,
        { profile: profileName, diagnostics },
      ),
    });
  }

  const outdated = deps.checkMinConnectorBuild(target.connector_build);
  if (outdated) {
    return cliResult(1, { json: outdated });
  }

  if (command === 'ping') {
    const res = await deps.ping(target, {
      timeoutMs: budget.remaining(),
      retryOnDisconnect: true,
    });
    return cliResult(res.ok ? 0 : 1, {
      json: res.ok ? { ...res, target, profile: profileName } : enrichFailure(res),
    });
  }

  if (command === 'list') {
    try {
      const catalog = await deps.loadCatalog(target, {
        timeoutMs: budget.remaining(),
        forceRefresh: flags['refresh-catalog'] === true,
      });
      return cliResult(0, {
        json: {
          ok: true,
          profile: profileName,
          target: { host: target.host, port: target.port, connector_host: target.connector_host },
          cache_path: cachePathForTarget(target),
          updated_at: catalog.updated_at,
          expires_at: catalog.expires_at,
          catalog_ttl_ms: CATALOG_TTL_MS,
          catalog_expired: isCatalogExpired(catalog),
          catalog_version: catalog.catalog_version,
          connector_build: catalog.connector_build,
          commands: catalog.commands,
          alias_to_command: catalog.alias_to_command,
        },
      });
    } catch (err) {
      return cliResult(1, { json: enrichThrown(err) });
    }
  }

  let parameters = coerceParameters({ ...flags });
  delete parameters.profile;
  delete parameters['refresh-catalog'];

  let catalog;
  try {
    catalog = await deps.loadCatalog(target, {
      timeoutMs: budget.remaining(),
      forceRefresh: flags['refresh-catalog'] === true,
    });
  } catch (err) {
    return cliResult(1, { json: enrichThrown(err, { command }) });
  }

  const resolved = resolveRemoteCommand(command, parameters, catalog);
  const entry = catalog?.commands_by_name?.[resolved.command];
  if (entry?.scope && entry.scope !== 'any') {
    const scopeError = checkScopeCompatibility(
      resolved.command,
      entry.scope,
      target.connector_host,
    );
    if (scopeError) {
      return cliResult(1, {
        json: cliError(scopeError, 'SCOPE_MISMATCH', null, { command: resolved.command }),
      });
    }
  }

  try {
    const allowsRetry = resolved.allowConnectionRetry === true;
    const readyCapMs = allowsRetry
      ? Math.min(EDITOR_READY_WAIT_CAP_MS, Math.floor(timeoutMs * 0.35))
      : Math.min(EDITOR_READY_WAIT_CAP_NO_RETRY_MS, Math.floor(timeoutMs * 0.3));

    const ready = await deps.waitForConnectorReady(target, {
      timeoutMs: Math.min(
        budget.remaining(),
        Math.max(EDITOR_READY_WAIT_FLOOR_MS, readyCapMs),
      ),
    });
    if (!ready.ok) {
      return cliResult(1, {
        json: cliError(ready.error, ready.error_code, ready.hint, {
          command: resolved.command,
          port: target.port,
          connector_state: ready.connector_state,
          play_mode: ready.play_mode,
        }),
      });
    }

    const res = await deps.sendCommand(target, resolved.command, parameters, {
      timeoutMs: budget.remaining(),
      allowConnectionRetry: resolved.allowConnectionRetry,
    });
    const out = res.ok
      ? res
      : enrichFailure(res, {
          command: resolved.command,
          status: res.status,
        });
    return cliResult(res.ok ? 0 : 1, { json: out });
  } catch (err) {
    return cliResult(1, { json: enrichThrown(err, { command: resolved.command }) });
  }
}

