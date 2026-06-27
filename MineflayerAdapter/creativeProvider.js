// ── Creative Inventory Provider (Sprint 52) ────────────────────────────────
// Version-agnostic creative inventory management. Abstracts away the
// differences between Minecraft versions:
//   - 1.16.5: bot.creative.setInventorySlot() API
//   - 1.21+:  same API, plus /give as fallback
//
// The /give command approach (previous implementation) required OP permissions
// and was unreliable across versions. The creative inventory API works on all
// supported versions without needing server operator status.

const { logStructured } = require('./logger');

// ── Constants ───────────────────────────────────────────────────────────────

/** Hotbar slot to use for creative item selection (0-indexed slot 36 = hotbar 1). */
const CREATIVE_HOTBAR_SLOT = 36;

// ── Public API ──────────────────────────────────────────────────────────────

/**
 * Ensures the bot has at least `count` of `itemName` in its creative inventory.
 * Selects the item into the hotbar so it's ready for placement.
 *
 * @param {object} bot - Mineflayer bot instance
 * @param {string} itemName - Minecraft item ID (e.g. "cobblestone", "oak_log")
 * @param {number} [count=1] - Minimum count needed (creative stacks are infinite)
 * @returns {boolean} true if the item was successfully provisioned and equipped
 */
async function ensureCreativeItem(bot, itemName, count = 1) {
  if (!bot || !bot.game) return false;
  if (bot.game.gameMode !== 1) return false; // not creative mode

  // Check if already in inventory
  const existing = bot.inventory.items().find(
    i => i.name === itemName || i.name === `minecraft:${itemName}`
  );
  if (existing && existing.count >= count) {
    logStructured('debug', 'creative', 'item already in inventory', {
      item: itemName, have: existing.count, need: count,
    });
    return true;
  }

  // Look up item in registry
  const cleanName = itemName.replace(/^minecraft:/, '');
  const itemDef = bot.registry.itemsByName[cleanName] || bot.registry.itemsByName[itemName];
  if (!itemDef) {
    logStructured('warn', 'creative', 'unknown item — cannot provision', {
      item: itemName,
    });
    return false;
  }

  try {
    await bot.creative.setInventorySlot(CREATIVE_HOTBAR_SLOT, itemDef);
    // Verify it's now in inventory
    const verify = bot.inventory.items().find(
      i => i.name === cleanName || i.name === itemName
    );
    if (verify) {
      logStructured('info', 'creative', 'provisioned item', {
        item: cleanName, slot: CREATIVE_HOTBAR_SLOT,
      });
      return true;
    }
    logStructured('warn', 'creative', 'setInventorySlot succeeded but item not found', {
      item: cleanName,
    });
    return false;
  } catch (e) {
    logStructured('warn', 'creative', 'setInventorySlot failed — version may not support API', {
      item: cleanName, error: e.message,
    });
    // Fallback: try /give command (works on modern servers with OP)
    try {
      bot.chat(`/give @p ${cleanName} ${count}`);
      logStructured('info', 'creative', 'fallback /give sent', {
        item: cleanName, count,
      });
      return true; // best-effort — can't verify inventory immediately
    } catch (e2) {
      logStructured('error', 'creative', '/give fallback also failed', {
        item: cleanName, error: e2.message,
      });
      return false;
    }
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
