/**
 * stopState.js — Emergency stop state management.
 *
 * Extracted from index.js Sprint 52 modularization (TSK-0166).
 * Provides an immediate-abort mechanism: the C# "stop" command bypasses the
 * command queue and calls handleStop() directly. Long-running loops (mine while,
 * findFlatArea column scan) check isStopRequested() at each iteration.
 *
 * Usage:
 *   import { createStopState } from './stopState.js';
 *   const stop = createStopState(bot, sendEvent);
 *   // In action handlers: if (stop.isStopRequested()) break;
 *   // In WS message handler: stop.handleStop();
 *   // At action start: stop.clearStop();
 */

/**
 * Creates the emergency stop state bound to a specific bot instance.
 * @param {import('mineflayer').Bot} bot
 * @param {Function} sendEvent - adapter's sendEvent function
 */
export function createStopState(bot, sendEvent) {
  let _stopRequested = false;

  /** Immediately aborts the current operation. */
  function handleStop() {
    console.log('[stop] emergency stop — clearing queue, stopping pathfinder');
    _stopRequested = true;
    try { bot.pathfinder.setGoal(null); } catch { /* ignore — bot may not be connected */ }
    sendEvent('stopComplete', {});
    console.log('[stop] done');
  }

  /** Resets the stop flag at the start of each action. */
  function clearStop() {
    _stopRequested = false;
  }

  /** @returns {boolean} true if a stop was requested. */
  function isStopRequested() {
    return _stopRequested;
  }

  return { handleStop, clearStop, isStopRequested };
}
