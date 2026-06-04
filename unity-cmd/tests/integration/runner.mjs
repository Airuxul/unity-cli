import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { waitForProfileReady } from '../../src/client/connector-readiness.js';
import { HOST_KIND, PROFILE_BY_HOST_KIND, INTEGRATION_ATTACH_TIMEOUT_MS, INTEGRATION_STEP_SLEEP_MS } from '../../src/constants.js';
import { formatProfileCreateExample } from '../../src/constants.js';
import { flattenScenarioSteps, runStep } from './lib/steps.mjs';

function sleep(ms) {
  return new Promise((r) => setTimeout(r, ms));
}

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const OUT_DIR = path.join(__dirname, 'out');
const PLAY_PROFILER_DIR = path.join(OUT_DIR, 'profiler-play');
process.env.UNITY_CMD_CACHE_DIR ??= path.join(os.homedir(), '.unity-cmd', 'cache');
const scenarioName =
  process.env.UNITY_CMD_SCENARIO && process.env.UNITY_CMD_SCENARIO.trim().length > 0
    ? process.env.UNITY_CMD_SCENARIO.trim()
    : 'editor-lifecycle';
const SCENARIO = path.join(__dirname, 'scenarios', `${scenarioName}.json`);
const ATTACH_TIMEOUT_MS = INTEGRATION_ATTACH_TIMEOUT_MS;
const RUN_TIMEOUT_MS = Number(process.env.UNITY_CMD_RUN_TIMEOUT_MS ?? 15 * 60_000);

async function main() {
  const watchdog = setTimeout(() => {
    console.error(`[integration] timed out after ${RUN_TIMEOUT_MS}ms`);
    process.exit(1);
  }, RUN_TIMEOUT_MS);

  if (!fs.existsSync(SCENARIO)) {
    console.error(`[integration] Unknown scenario: ${scenarioName}`);
    console.error(`  Expected file: ${SCENARIO}`);
    process.exit(1);
  }

  console.log(`[integration] scenario=${scenarioName}`);
  const profileName = process.env.UNITY_CMD_PROFILE ?? defaultProfileForScenario(scenarioName);
  const instance = await waitForProfileReady({
    profile: profileName,
    timeoutMs: ATTACH_TIMEOUT_MS,
    logProgress: false,
  });

  if (!instance?.host || !instance?.port) {
    logSkip();
    writeReport({ skipped: true, reason: 'no_instance' });
    process.exit(0);
  }

  const scenario = JSON.parse(fs.readFileSync(SCENARIO, 'utf8'));
  const expandedSteps = flattenScenarioSteps(scenario.steps);
  const results = [];
  let failed = false;
  let inPlay = false;

  for (const step of expandedSteps) {
    const timeoutMs = step.timeoutMs ?? INTEGRATION_ATTACH_TIMEOUT_MS;
    const stepStarted = Date.now();
    try {
      const result = await Promise.race([
        runStep(step, profileName, timeoutMs),
        timeoutAfter(step.name, timeoutMs),
      ]);
      if (shouldExportPlayProfiler(step, inPlay, result)) {
        exportPlayProfilerResult(scenarioName, step.name, result.output);
      }
      if (step.command === 'play' && result.status === 'passed') inPlay = true;
      if ((step.command === 'stop' || step.command === 'play.stop') && result.status === 'passed') inPlay = false;
      results.push(result);
      if (result.status !== 'passed') failed = true;
      console.log(`[${result.status}] ${result.name} (${result.elapsedMs}ms)`);
      if (result.error) console.error(`  ${result.error}`);
    } catch (err) {
      failed = true;
      const isTimeout = String(err.message).includes('exceeded');
      const result = {
        name: step.name,
        status: isTimeout ? 'timeout' : 'failed',
        elapsedMs: Date.now() - stepStarted,
        error: err.message,
      };
      results.push(result);
      console.log(`[${result.status}] ${step.name}`);
      console.error(`  ${err.message}`);
    }

    if (failed) break;
    await sleep(INTEGRATION_STEP_SLEEP_MS);
  }

  writeReport({ skipped: false, instance: { host: instance.host, port: instance.port }, results });
  clearTimeout(watchdog);
  process.exit(failed ? 1 : 0);
}

function timeoutAfter(name, ms) {
  return new Promise((_, reject) =>
    setTimeout(() => reject(new Error(`step ${name} exceeded ${ms}ms`)), ms),
  );
}

function defaultProfileForScenario(name) {
  return name === 'player-runtime' ? PROFILE_BY_HOST_KIND[HOST_KIND.Player] : 'editor';
}

function logSkip() {
  const profile = process.env.UNITY_CMD_PROFILE ?? defaultProfileForScenario(scenarioName);
  console.error(`[integration] 未检测到可用的 Unity profile (${profile})。`);
  console.error('  - 请在外部工程中安装 com.air.unity-connector 并用 Unity 打开该项目');
  console.error(`  - 先创建 profile: ${formatProfileCreateExample('editor')}`);
  console.error('  - Editor: set UNITY_CMD_SCENARIO=editor-lifecycle && npm run test:integration');
  console.error('  - Player: set UNITY_CMD_SCENARIO=player-runtime && UNITY_CMD_PROFILE=package-play');
  console.error('  - 截图默认写到 UNITY_CMD_CACHE_DIR（默认 ~/.unity-cmd/cache），无需 UNITY_CMD_WORKSPACE');
  console.error('  - 跳过集成测试（非失败）');
}

function writeReport(report) {
  fs.mkdirSync(OUT_DIR, { recursive: true });
  fs.writeFileSync(path.join(OUT_DIR, 'report.json'), JSON.stringify(report, null, 2));
}

function shouldExportPlayProfiler(step, inPlay, result) {
  return (
    inPlay &&
    step?.command === 'profiler' &&
    result?.status === 'passed' &&
    result?.output &&
    typeof result.output === 'object'
  );
}

function exportPlayProfilerResult(scenario, stepName, output) {
  fs.mkdirSync(PLAY_PROFILER_DIR, { recursive: true });
  const safeScenario = sanitizeFileName(scenario);
  const safeStepName = sanitizeFileName(stepName);
  const file = path.join(PLAY_PROFILER_DIR, `${safeScenario}-${safeStepName}.json`);
  fs.writeFileSync(file, JSON.stringify(output, null, 2));
}

function sanitizeFileName(input) {
  return String(input ?? 'unknown').replace(/[^a-z0-9._-]+/gi, '_');
}

main().catch((err) => {
  console.error(err);
  process.exit(1);
});
