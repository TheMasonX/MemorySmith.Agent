import test from 'node:test';
import assert from 'node:assert/strict';
import { emitGameModeEvent } from '../gameModeState.js';

test('emitGameModeEvent sends the current game mode when the bot is already in creative mode', () => {
  const emitted = [];
  const bot = { game: { gameMode: 'creative' } };

  const emittedAny = emitGameModeEvent(bot, (event, data) => emitted.push([event, data]), () => {});

  assert.equal(emittedAny, true);
  assert.deepEqual(emitted, [['gameMode', { mode: 'creative' }]]);
});

test('emitGameModeEvent skips sending when no game mode is available', () => {
  const emitted = [];
  const bot = { game: {} };

  const emittedAny = emitGameModeEvent(bot, (event, data) => emitted.push([event, data]), () => {});

  assert.equal(emittedAny, false);
  assert.deepEqual(emitted, []);
});
