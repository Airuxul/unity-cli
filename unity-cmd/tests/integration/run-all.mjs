import fs from 'node:fs';
import path from 'node:path';
import { ping } from '../../src/client/command.js';
import { loadProfile, hostKindMatches } from '../../src/client/connection.js';
import { HOST_KIND, INTEGRATION_PLAYER_PROBE_MS, PROFILE_BY_HOST_KIND } from '../../src/constants.js';
import { formatProfileCreateExample } from '../../src/constants.js';
import { spawn } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const RUNNER = fileURLToPath(new URL('./runner.mjs', import.meta.url));

async function main() {
  await runScenario('editor-lifecycle', process.env.UNITY_CMD_PROFILE ?? 'editor');

  if (process.env.UNITY_CMD_INTEGRATION_STRESS === '1') {
    await runScenario('editor-reliability-stress', process.env.UNITY_CMD_PROFILE ?? 'editor');
  } else {
    console.log(
      '[integration] skip editor-reliability-stress: set UNITY_CMD_INTEGRATION_STRESS=1 to run',
    );
  }

  await maybeRunCompileRecompileScenario();

  await maybeRunGameDemoScenario();

  const playerProfileName =
    process.env.UNITY_CMD_PLAYER_PROFILE ?? PROFILE_BY_HOST_KIND[HOST_KIND.Player];
  await maybeRunPlayerScenario(playerProfileName);
}

async function maybeRunCompileRecompileScenario() {
  const workspace = process.env.UNITY_CMD_WORKSPACE?.trim();
  if (!workspace) {
    console.log(
      '[integration] skip compile-recompile-cycle: set UNITY_CMD_WORKSPACE to Unity project root',
    );
    return;
  }

  const assetsDir = path.join(workspace, 'Assets');
  if (!fs.existsSync(assetsDir)) {
    console.log(
      '[integration] skip compile-recompile-cycle: missing Assets/ under UNITY_CMD_WORKSPACE',
    );
    return;
  }

  await runScenario('compile-recompile-cycle', process.env.UNITY_CMD_PROFILE ?? 'editor');
}

async function maybeRunGameDemoScenario() {
  const workspace = process.env.UNITY_CMD_WORKSPACE?.trim();
  if (!workspace) {
    console.log(
      '[integration] skip gamedemo-scene-switch-play: set UNITY_CMD_WORKSPACE to GameDemo project root',
    );
    return;
  }

  const statUp = path.join(workspace, 'Assets/Scenes/StatUp.unity');
  const boot = path.join(workspace, 'Assets/Scenes/Boot.unity');
  if (!fs.existsSync(statUp) || !fs.existsSync(boot)) {
    console.log(
      '[integration] skip gamedemo-scene-switch-play: missing Assets/Scenes/StatUp.unity or Boot.unity',
    );
    return;
  }

  await runScenario('gamedemo-scene-switch-play', process.env.UNITY_CMD_PROFILE ?? 'editor');
}

async function maybeRunPlayerScenario(playerProfileName) {
  const playerProfile = loadProfile(playerProfileName);
  if (!playerProfile?.host || !playerProfile?.port) {
    console.log(
      `[integration] skip player-runtime: profile '${playerProfileName}' missing or invalid.`,
    );
    console.log(`[integration] hint: ${formatProfileCreateExample(playerProfileName, HOST_KIND.Player)}`);
    return;
  }

  const probe = await probePlayerConnector(playerProfile);
  if (!probe.ok) {
    console.log(`[integration] skip player-runtime: ${probe.reason}`);
    if (probe.reason === 'not_listening') {
      console.log(
        `[integration] hint: start a Development Build with connector on ${playerProfile.host}:${playerProfile.port}`,
      );
    } else if (probe.reason === 'host_mismatch') {
      console.log(
        `[integration] hint: endpoint responded but host is '${probe.actualHost}' (expected '${HOST_KIND.Player}')`,
      );
    }
    return;
  }

  await runScenario('player-runtime', playerProfileName);
}

async function probePlayerConnector(profile) {
  const target = { host: profile.host, port: profile.port };
  try {
    const res = await ping(target, {
      timeoutMs: INTEGRATION_PLAYER_PROBE_MS,
      retryOnDisconnect: false,
      maxAttempts: 1,
    });
    if (!res.ok) {
      return { ok: false, reason: 'not_listening' };
    }
    if (!hostKindMatches(HOST_KIND.Player, res.data?.host)) {
      return { ok: false, reason: 'host_mismatch', actualHost: res.data?.host ?? null };
    }
    return { ok: true };
  } catch {
    return { ok: false, reason: 'not_listening' };
  }
}

function runScenario(scenario, profile) {
  return new Promise((resolve, reject) => {
    console.log(`[integration] run scenario=${scenario} profile=${profile}`);
    const child = spawn(process.execPath, [RUNNER], {
      env: {
        ...process.env,
        UNITY_CMD_SCENARIO: scenario,
        UNITY_CMD_PROFILE: profile,
      },
      stdio: 'inherit',
    });
    child.on('error', reject);
    child.on('close', (code) => {
      if (code === 0) return resolve();
      reject(new Error(`scenario '${scenario}' failed with exit code ${code}`));
    });
  });
}

main().catch((err) => {
  console.error(err.message ?? err);
  process.exit(1);
});
