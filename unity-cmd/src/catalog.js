import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { fetchCatalog } from './client/command.js';

export const LOCAL_META = new Set(['ping', 'list', 'help']);

/** Used when Unity catalog has no alias map (pre-build-4 connector). */
const BOOTSTRAP_ALIAS_TO_COMMAND = {
  recompile: 'compile',
  reload: 'compile',
  'reload-scripts': 'compile',
  'editor.recompile': 'compile',
  console: 'editor.console',
  logs: 'editor.console',
  menu: 'editor.menu',
  screenshot: 'editor.screenshot',
  exec: 'editor.exec',
  profiler: 'editor.profiler',
  manage: 'editor.manage',
  reserialize: 'editor.reserialize',
};

const CACHE_DIR = path.join(os.homedir(), '.unity-cmd', 'cache');

export function cachePathForTarget(target) {
  const key = (
    target.project_path ??
    target.project ??
    `${target.host}:${target.port}`
  ).replace(/[\\/:*?"<>|]/g, '_');
  return path.join(CACHE_DIR, `catalog-${key}.json`);
}

function indexCatalog(raw) {
  const commands = raw?.commands ?? [];
  const commandsByName = Object.create(null);
  for (const entry of commands) {
    if (entry?.name) commandsByName[entry.name] = entry;
  }
  return {
    catalog_version: raw?.catalog_version ?? null,
    commands,
    commands_by_name: commandsByName,
    alias_to_command: raw?.alias_to_command ?? {},
  };
}

export async function loadCatalog(target, options = {}) {
  const { forceRefresh = false, timeoutMs } = options;
  const cachePath = cachePathForTarget(target);

  if (!forceRefresh && fs.existsSync(cachePath)) {
    try {
      const cached = JSON.parse(fs.readFileSync(cachePath, 'utf8'));
      if (cached?.commands?.length) return indexCatalog(cached);
    } catch {
      // refresh below
    }
  }

  const res = await fetchCatalog(target, { timeoutMs });
  if (!res.ok) {
    throw new Error('Failed to fetch command catalog from Unity Editor.');
  }

  const catalog = indexCatalog({
    catalog_version: res.catalog_version,
    commands: res.commands,
    alias_to_command: res.alias_to_command,
  });

  fs.mkdirSync(CACHE_DIR, { recursive: true });
  fs.writeFileSync(cachePath, JSON.stringify(catalog, null, 2));
  return catalog;
}

/**
 * Resolve CLI command name to remote command + polling options using Unity catalog.
 * @param {string} command
 * @param {Record<string, unknown>} flags
 * @param {ReturnType<typeof indexCatalog>} catalog
 */
export function resolveRemoteCommand(command, flags = {}, catalog) {
  const aliasMap = {
    ...BOOTSTRAP_ALIAS_TO_COMMAND,
    ...(catalog?.alias_to_command ?? {}),
  };
  const byName = catalog?.commands_by_name ?? {};

  let canonical = command;
  const lower = command?.toLowerCase?.() ?? command;
  if (aliasMap[command]) canonical = aliasMap[command];
  else if (aliasMap[lower]) canonical = aliasMap[lower];

  const entry = byName[canonical];
  const refreshWithCompile =
    canonical === 'refresh' &&
    (flags.compile === true || flags.compile === 'true');
  const compileEntry = byName.compile;

  if (refreshWithCompile) {
    return {
      command: canonical,
      allowConnectionRetry: true,
      minTimeoutMs: compileEntry?.default_timeout_ms ?? 120_000,
    };
  }

  const minTimeoutMs =
    entry?.default_timeout_ms > 0 ? entry.default_timeout_ms : null;

  return {
    command: canonical,
    allowConnectionRetry:
      entry?.allow_connection_retry === true || entry?.is_job === true,
    minTimeoutMs,
  };
}
