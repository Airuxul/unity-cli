import { COMMAND_BUDGET_FLOOR_MS, DEFAULT_TIMEOUT_MS, MIN_CONNECTOR_BUILD } from './constants.js';
import { cliError } from './errors.js';

export function resolveTimeoutMs(overrideMs) {
  if (overrideMs != null && overrideMs > 0) return overrideMs;
  const env = process.env.UNITY_CMD_TIMEOUT_MS;
  if (env) {
    const parsed = Number.parseInt(env, 10);
    if (!Number.isNaN(parsed) && parsed > 0) return parsed;
  }
  return DEFAULT_TIMEOUT_MS;
}

export function createCommandBudget(totalMs) {
  const startedAt = Date.now();
  return {
    remaining(floorMs = COMMAND_BUDGET_FLOOR_MS) {
      return Math.max(floorMs, totalMs - (Date.now() - startedAt));
    },
    elapsed() {
      return Date.now() - startedAt;
    },
  };
}

/** @param {number|string|null|undefined} build */
export function checkMinConnectorBuild(build) {
  if (build == null || build === '') return null;
  const n = Number(build);
  if (Number.isNaN(n) || n >= MIN_CONNECTOR_BUILD) return null;
  return cliError(
    `Connector build ${n} is below required minimum ${MIN_CONNECTOR_BUILD}.`,
    'CONNECTOR_OUTDATED',
    'Update com.air.unity-connector in the project and recompile scripts in Unity Editor.',
    { connector_build: n, min_connector_build: MIN_CONNECTOR_BUILD },
  );
}
