/**
 * logger.cjs — Structured JSON-line file logger for the MineflayerAdapter.
 *
 * Converted from ESM (.js) to CommonJS (.cjs) so that both the ESM index.js
 * and the CJS creativeProvider.cjs can import it without ERR_REQUIRE_ESM.
 *
 * Sprint 60 (TSK-0391): Fixed cross-format import regression. The previous
 * logger.js used ESM export, which crashed creativeProvider.cjs's require().
 *
 * Usage:
 *   const { logStructured } = require('./logger.cjs');
 *   logStructured('info', 'mine', 'block mined', { block: 'oak_log', count: 5 });
 */

const { appendFileSync, mkdirSync, existsSync } = require('node:fs');

const LOG_DIR = process.env.LOG_DIR ?? './logs';
try { if (!existsSync(LOG_DIR)) mkdirSync(LOG_DIR, { recursive: true }); } catch (err) { try { console.error('[adapter logger] mkdir failed:', err && err.message); } catch {} }

/**
 * Writes a structured JSON line to the daily adapter log file.
 * @param {'debug'|'info'|'warn'|'error'} level
 * @param {string} category - action category (mine, wander, findFlatArea, craft, smelt, dispatch)
 * @param {string} message - human-readable summary
 * @param {Object} [data] - structured context (merged into the JSON entry)
 */
function logStructured(level, category, message, data = {}) {
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
  } catch (err) {
    // Best-effort — never crash the bot on log I/O failure. Log to console as fallback.
    try { console.error('[adapter logger] write failed:', err && err.message); } catch {}
  }
}

module.exports = { logStructured };
