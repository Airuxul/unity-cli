import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { flattenScenarioSteps, runStep } from '../integration/lib/steps.mjs';

test('flattenScenarioSteps expands repeat blocks with stable names', () => {
  const flat = flattenScenarioSteps([
    { name: 'a', command: 'ping' },
    {
      name: 'stress',
      repeat: 2,
      steps: [
        { name: 'play', command: 'play' },
        { name: 'stop', command: 'stop' },
      ],
    },
    { name: 'z', command: 'state' },
  ]);
  assert.equal(flat.length, 6);
  assert.equal(flat[0].name, 'a');
  assert.equal(flat[1].name, 'stress_1_play');
  assert.equal(flat[2].name, 'stress_1_stop');
  assert.equal(flat[3].name, 'stress_2_play');
  assert.equal(flat[4].name, 'stress_2_stop');
  assert.equal(flat[5].name, 'z');
});

test('runStep sleepMs passes without CLI', async () => {
  const t0 = Date.now();
  const result = await runStep({ name: 'wait', sleepMs: 50 }, {}, 5000);
  assert.equal(result.status, 'passed');
  assert.ok(result.elapsedMs >= 45);
});

test('runStep writeFile creates the file with given content', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'unity-cmd-step-wf-'));
  const filePath = path.join(dir, 'sub', 'test.cs');
  try {
    const result = await runStep(
      { name: 'write', writeFile: { path: filePath, content: '// hello\n' } },
      {},
      5000,
    );
    assert.equal(result.status, 'passed');
    assert.equal(fs.readFileSync(filePath, 'utf8'), '// hello\n');
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('runStep writeFile fails when path is missing', async () => {
  const result = await runStep(
    { name: 'write_no_path', writeFile: { content: '// x' } },
    {},
    5000,
  );
  assert.equal(result.status, 'failed');
  assert.match(result.error, /path is required/);
});

test('runStep deleteFile removes file and meta', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'unity-cmd-step-df-'));
  const filePath = path.join(dir, 'bad.cs');
  const metaPath = `${filePath}.meta`;
  fs.writeFileSync(filePath, 'x');
  fs.writeFileSync(metaPath, 'y');
  try {
    const result = await runStep(
      { name: 'delete', deleteFile: { path: filePath } },
      {},
      5000,
    );
    assert.equal(result.status, 'passed');
    assert.equal(fs.existsSync(filePath), false);
    assert.equal(fs.existsSync(metaPath), false);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});

test('runStep deleteFile passes even when file does not exist', async () => {
  const result = await runStep(
    { name: 'delete_missing', deleteFile: { path: '/tmp/nonexistent-unity-cmd-test.cs' } },
    {},
    5000,
  );
  assert.equal(result.status, 'passed');
});

test('runStep assertFile checks size', async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'unity-cmd-step-'));
  const project = path.join(dir, 'Proj');
  const shots = path.join(project, 'Screenshots');
  fs.mkdirSync(shots, { recursive: true });
  const file = path.join(shots, 'test.png');
  fs.writeFileSync(file, Buffer.alloc(800));

  const ok = await runStep(
    {
      name: 'assert_png',
      assertFile: { relativePath: 'Screenshots/test.png', minBytes: 400, projectRoot: project },
    },
    {},
    5000,
  );
  assert.equal(ok.status, 'passed');

  const bad = await runStep(
    {
      name: 'assert_small',
      assertFile: { relativePath: 'Screenshots/test.png', minBytes: 900, projectRoot: project },
    },
    {},
    5000,
  );
  assert.equal(bad.status, 'failed');

  fs.rmSync(dir, { recursive: true, force: true });
});
