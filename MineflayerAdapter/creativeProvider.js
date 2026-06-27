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

/** Hotbar slot for creative item selection (0-indexed slot 36 = hotbar 1). */
const CREATIVE_HOTBAR_SLOT = 36;

/** Delay after /give to let the server process the command. */
const GIVE_DELAY_MS = 100;

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

  // Strategy 1: creative inventory API
  const itemDef = bot.registry.itemsByName[cleanName] || bot.registry.itemsByName[itemName];
  if (itemDef) {
    try {
      await bot.creative.setInventorySlot(CREATIVE_HOTBAR_SLOT, itemDef);
      // Small delay to let the server sync inventory
      await new Promise(r => setTimeout(r, 50));
      const verify = bot.inventory.items().find(
        i => i.name === cleanName || i.name === itemName
      );
      if (verify) {
        logStructured('info', 'creative', 'provisioned via creative API', {
          item: cleanName, slot: CREATIVE_HOTBAR_SLOT,
        });
        return true;
      }
      logStructured('warn', 'creative', 'creative API succeeded but item not in inventory — trying /give', {
        item: cleanName,
      });
    } catch (e) {
      logStructured('warn', 'creative', 'creative API failed — trying /give', {
        item: cleanName, error: e.message,
      });
    }
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
