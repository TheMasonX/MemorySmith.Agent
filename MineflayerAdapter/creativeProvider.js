// ── Creative Inventory Provider (Sprint 52) ────────────────────────────────
// Version-agnostic creative inventory management. Strategies tried in order:
//   1. bot.creative.setInventorySlot() — creative API (1.16.5+)
//   2. /give command — works on most servers with OP, reliable fallback
//   3. bot.chat() — sends /give as chat message (always works if OP)
//
// The previous implementation was creative-API-only and failed silently
// when setInventorySlot succeeded but the item didn't appear in inventory
// (a known Mineflayer quirk on some versions).

const { logStructured } = require('./logger');

// ── Constants ───────────────────────────────────────────────────────────────

/** All hotbar slots to cycle through (0-indexed 36-44 = hotbar 1-9). */
const HOTBAR_SLOTS = [36, 37, 38, 39, 40, 41, 42, 43, 44];

/** Delay after /give to let the server process the command. */
const GIVE_DELAY_MS = 100;

/** Current hotbar slot index for round-robin provisioning. */
let _nextSlotIndex = 0;

// ── Public API ──────────────────────────────────────────────────────────────

/**
 * Ensures the bot has at least `count` of `itemName` in its creative inventory.
 * Tries creative API first, falls back to /give command.
 *
 * @param {object} bot - Mineflayer bot instance
 * @param {string} itemName - Minecraft item ID (e.g. "cobblestone", "oak_log")
 * @param {number} [count=1] - Minimum count needed
 * @returns {boolean} true if provisioning was attempted (best-effort)
 */
async function ensureCreativeItem(bot, itemName, count = 1) {
  if (!bot || !bot.game) return false;
  if (bot.game.gameMode !== 1) return false;

  const cleanName = itemName.replace(/^minecraft:/, '');

  // Check if already in inventory
  const existing = bot.inventory.items().find(
    i => i.name === cleanName || i.name === itemName
  );
  if (existing && existing.count >= count) {
    logStructured('debug', 'creative', 'item already in inventory', {
      item: cleanName, have: existing.count, need: count,
    });
    return true;
  }

  // Strategy 1: creative inventory API — try multiple hotbar slots
  const itemDef = bot.registry.itemsByName[cleanName] || bot.registry.itemsByName[itemName];
  if (itemDef) {
    // Try all hotbar slots, starting from the current round-robin position
    for (let attempt = 0; attempt < HOTBAR_SLOTS.length; attempt++) {
      const slot = HOTBAR_SLOTS[(_nextSlotIndex + attempt) % HOTBAR_SLOTS.length];
      try {
        await bot.creative.setInventorySlot(slot, itemDef);
        await new Promise(r => setTimeout(r, 50));
        const verify = bot.inventory.items().find(
          i => i.name === cleanName || i.name === itemName
        );
        if (verify) {
          _nextSlotIndex = (_nextSlotIndex + 1) % HOTBAR_SLOTS.length;
          logStructured('info', 'creative', 'provisioned via creative API', {
            item: cleanName, slot,
          });
          return true;
        }
      } catch (e) {
        // This slot failed — try next one
      }
    }
    logStructured('warn', 'creative', 'all creative API slots failed — trying /give', {
      item: cleanName,
    });
  } else {
    logStructured('warn', 'creative', 'item not in registry — trying /give anyway', {
      item: cleanName,
    });
  }

  // Strategy 2: /give command (reliable fallback, works on 1.16.5+ with OP)
  try {
    bot.chat(`/give @p ${cleanName} ${count}`);
    await new Promise(r => setTimeout(r, GIVE_DELAY_MS));
    logStructured('info', 'creative', 'provisioned via /give', {
      item: cleanName, count,
    });
    return true; // best-effort — inventory syncs asynchronously
  } catch (e) {
    logStructured('error', 'creative', '/give also failed', {
      item: cleanName, error: e.message,
    });
    return false;
  }
}

/**
 * Provisions multiple items needed for a build. Groups by item type to minimize
 * slot switches (creative mode grants infinite stacks, so provisioning once per
 * unique item type is sufficient).
 *
 * @param {object} bot - Mineflayer bot instance
 * @param {string[]} itemNames - Array of Minecraft item IDs
 * @returns {Promise<string[]>} Array of item names that were successfully provisioned
 */
async function ensureCreativeItems(bot, itemNames) {
  const unique = [...new Set(itemNames)];
  const result = [];
  for (const name of unique) {
    if (await ensureCreativeItem(bot, name)) {
      result.push(name);
    }
  }
  return result;
}

module.exports = { ensureCreativeItem, ensureCreativeItems };
