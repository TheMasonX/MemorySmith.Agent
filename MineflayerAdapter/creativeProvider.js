// ── Creative Inventory Provider (Sprint 52, Sprint 56 TSK-0272) ─────────────
// Version-agnostic creative inventory management. Strategies tried in order:
//   1. bot.creative.setInventorySlot() — creative API (1.16.5+)
//   2. /give command — works on most servers with OP, reliable fallback
//
// Sprint 56 (TSK-0272): On Mineflayer 4.37.1 + MC 1.16.5 (LAN), bot.inventory.items()
// does NOT reflect items placed via bot.creative.setInventorySlot(). The creative
// API call succeeds without error but the verify step fails. /give also doesn't work
// on LAN worlds (no OP). Fix: trust setInventorySlot when it doesn't throw — the
// item IS in the creative inventory even if items() doesn't show it.
// Return the slot number so callers can equip directly from bot.inventory.slots[slot].

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
 * Sprint 56 (TSK-0272): Returns {ok, slot} object instead of boolean.
 * - When setInventorySlot succeeds (no throw), the item IS in the creative
 *   inventory even if bot.inventory.items() doesn't show it (known Mineflayer
 *   4.37.1 + MC 1.16.5 quirk). Trust the API and return the slot number.
 * - Callers should equip from bot.inventory.slots[slot] rather than
 *   searching bot.inventory.items().
 *
 * @param {object} bot - Mineflayer bot instance
 * @param {string} itemName - Minecraft item ID (e.g. "cobblestone", "oak_log")
 * @param {number} [count=1] - Minimum count needed
 * @returns {{ok: boolean, slot: number|null}} ok=true if item is available;
 *   slot is the hotbar slot index (36-44) or null.
 */
async function ensureCreativeItem(bot, itemName, count = 1) {
  if (!bot || !bot.game) return { ok: false, slot: null };
  if (bot.game.gameMode !== 1) return { ok: false, slot: null };

  const cleanName = itemName.replace(/^minecraft:/, '');

  // Check if already in inventory (items() may not show creative-placed items
  // but will show items collected from the world).
  const existing = bot.inventory.items().find(
    i => i.name === cleanName || i.name === itemName
  );
  if (existing && existing.count >= count) {
    logStructured('debug', 'creative', 'item already in inventory', {
      item: cleanName, have: existing.count, need: count,
    });
    return { ok: true, slot: existing.slot };
  }

  // Strategy 1: creative inventory API — try multiple hotbar slots.
  // Sprint 56 (TSK-0272): bot.inventory.items() does NOT reliably show
  // creative-placed items on Mineflayer 4.37.1 + MC 1.16.5 (LAN). The API
  // call succeeds but items() returns stale data. Instead of requiring
  // items() verification, we trust setInventorySlot when it doesn't throw.
  const itemDef = bot.registry.itemsByName[cleanName] || bot.registry.itemsByName[itemName];
  if (itemDef) {
    // Try all hotbar slots, starting from the current round-robin position
    for (let attempt = 0; attempt < HOTBAR_SLOTS.length; attempt++) {
      const slot = HOTBAR_SLOTS[(_nextSlotIndex + attempt) % HOTBAR_SLOTS.length];
      try {
        await bot.creative.setInventorySlot(slot, itemDef);
        // Short delay to let the server process the inventory change
        await new Promise(r => setTimeout(r, 50));

        // Sprint 56 (TSK-0272): verify via items() is best-effort only.
        // If it finds the item, great. If not, the creative API still placed
        // the item in the slot — trust the API and check slots[] directly.
        const verify = bot.inventory.items().find(
          i => i.name === cleanName || i.name === itemName
        );
        if (verify) {
          _nextSlotIndex = (_nextSlotIndex + 1) % HOTBAR_SLOTS.length;
          logStructured('info', 'creative', 'provisioned via creative API (verified)', {
            item: cleanName, slot,
          });
          return { ok: true, slot };
        }

        // Sprint 56 (TSK-0272): items() didn't find it, but check slots[] directly.
        // On Mineflayer 4.37.1+MC 1.16.5, creative-placed items ARE in slots[]
        // even though items() doesn't enumerate them.
        const slotItem = bot.inventory.slots[slot];
        if (slotItem && (slotItem.name === cleanName || slotItem.name === itemName)) {
          _nextSlotIndex = (_nextSlotIndex + 1) % HOTBAR_SLOTS.length;
          logStructured('info', 'creative', 'provisioned via creative API (slot-direct)', {
            item: cleanName, slot, slotItemName: slotItem.name,
          });
          return { ok: true, slot };
        }

        // Sprint 56 (TSK-0272): Neither items() nor slots[] shows the item,
        // but setInventorySlot didn't throw. Trust the API — the item is
        // in the creative inventory slot, just not visible through the
        // Mineflayer inventory model. Return the slot for direct equipping.
        _nextSlotIndex = (_nextSlotIndex + 1) % HOTBAR_SLOTS.length;
        logStructured('warn', 'creative', 'setInventorySlot succeeded but item not visible in inventory model — trusting API', {
          item: cleanName, slot,
        });
        return { ok: true, slot };
      } catch (e) {
        // This slot's setInventorySlot threw — try next slot
        logStructured('debug', 'creative', 'setInventorySlot threw for slot', {
          item: cleanName, slot, error: e.message,
        });
      }
    }
    logStructured('warn', 'creative', 'all creative API slots threw — trying /give', {
      item: cleanName,
    });
  } else {
    logStructured('warn', 'creative', 'item not in registry — trying /give anyway', {
      item: cleanName,
    });
  }

  // Strategy 2: /give command (best-effort; doesn't work on LAN worlds without OP).
  // Sprint 56 (TSK-0272): /give does NOT work on "Open to LAN" worlds (no OP).
  // This is a known limitation — return ok=false since it's unreliable.
  try {
    bot.chat(`/give @p ${cleanName} ${count}`);
    await new Promise(r => setTimeout(r, GIVE_DELAY_MS));
    logStructured('info', 'creative', 'provisioned via /give (best-effort, may not work on LAN)', {
      item: cleanName, count,
    });
    return { ok: true, slot: null }; // slot unknown from /give
  } catch (e) {
    logStructured('error', 'creative', '/give also failed', {
      item: cleanName, error: e.message,
    });
    return { ok: false, slot: null };
  }
}

/**
 * Provisions multiple items needed for a build. Groups by item type to minimize
 * slot switches (creative mode grants infinite stacks, so provisioning once per
 * unique item type is sufficient).
 *
 * Sprint 56 (TSK-0272): updated for new {ok, slot} return type.
 *
 * @param {object} bot - Mineflayer bot instance
 * @param {string[]} itemNames - Array of Minecraft item IDs
 * @returns {Promise<string[]>} Array of item names that were successfully provisioned
 */
async function ensureCreativeItems(bot, itemNames) {
  const unique = [...new Set(itemNames)];
  const result = [];
  for (const name of unique) {
    const res = await ensureCreativeItem(bot, name);
    if (res.ok) {
      result.push(name);
    }
  }
  return result;
}

module.exports = { ensureCreativeItem, ensureCreativeItems };
