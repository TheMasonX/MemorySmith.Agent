import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { test } from 'node:test';

const adapterSource = fs.readFileSync(
  path.join(import.meta.dirname, '..', 'index.js'),
  'utf8',
);

test('playerCollect ignores collections by other players', () => {
  assert.match(
    adapterSource,
    /bot\.on\('playerCollect',[\s\S]*?if \(collector\.username !== bot\.username\) return;/,
  );
});