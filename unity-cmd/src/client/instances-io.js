import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { INSTANCES_DIR } from '../constants.js';

export function hashProjectPath(projectPath) {
  const normalized = String(projectPath ?? '').replace(/\\/g, '/').toLowerCase();
  return crypto.createHash('md5').update(normalized, 'utf8').digest('hex').slice(0, 16);
}

export function readInstanceFile(filePath) {
  try {
    if (!fs.existsSync(filePath)) return null;
    return JSON.parse(fs.readFileSync(filePath, 'utf8'));
  } catch {
    return null;
  }
}

export function findInstanceByPort(port) {
  if (!port || !fs.existsSync(INSTANCES_DIR)) return null;
  for (const name of fs.readdirSync(INSTANCES_DIR)) {
    if (!name.endsWith('.json')) continue;
    const inst = readInstanceFile(path.join(INSTANCES_DIR, name));
    if (inst?.port === port) return inst;
  }
  return null;
}

export function normalizeProjectPath(projectPath) {
  return String(projectPath ?? '')
    .replace(/\\/g, '/')
    .replace(/\/+$/, '')
    .toLowerCase();
}

export function findInstanceByProject(projectPath) {
  const want = normalizeProjectPath(projectPath);
  if (!want || !fs.existsSync(INSTANCES_DIR)) return null;

  let best = null;
  let bestTs = 0;

  for (const name of fs.readdirSync(INSTANCES_DIR)) {
    if (!name.endsWith('.json')) continue;
    const inst = readInstanceFile(path.join(INSTANCES_DIR, name));
    if (!inst?.projectPath) continue;
    if (normalizeProjectPath(inst.projectPath) !== want) continue;
    const ts = Number(inst.timestamp) || 0;
    if (ts >= bestTs) {
      bestTs = ts;
      best = inst;
    }
  }

  return best;
}
