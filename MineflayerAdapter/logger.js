/**
 * logger.js — Structured JSON-line file logger for the MineflayerAdapter.
 *
 * Extracted from index.js Sprint 52 modularization (TSK-0166).
 * Writes JSON lines to a daily rolling log file alongside the C# host's Serilog
 * output. Console stays concise; the file captures full structured context for
 * post-hoc diagnostics.
 *
 * Usage:
 *   import { logStructured } from './logger.js';
 *   logStructured('info', 'mine', 'block mined', { block: 'oak_log', count: 5 });
 */

import { appendFileSync, mkdirSync, existsSync } from 'node:fs';

const LOG_DIR = process.env.LOG_DIR ?? './logs';
try { if (!existsSync(LOG_DIR)) mkdirSync(LOG_DIR, { recursive: true }); } catch { /* best-effort */ }

/**
 * Writes a structured JSON line to the daily adapter log file.
 * @param {'debug'|'info'|'warn'|'error'} level
 * @param {string} category - action category (mine, wander, findFlatArea, craft, smelt, dispatch)
 * @param {string} message - human-readable summary
 * @param {Object} [data] - structured context (merged into the JSON entry)
 */
export function logStructured(level, category, message, data = {}) {
  const entry = JSON.stringify({
    t: new Date().toISOString(),
    l: level,
    c: category,
    m: message,
    ...data,
  });
  const dateStr = new Date().toISOString().split('T')[0];
  try {
    appendFileSync(`${LOG_DIR}/adapter-${dateStr}.log`, entry + '\n');
  } catch { /* best-effort — never crash the bot on log I/O failure */ }
}
