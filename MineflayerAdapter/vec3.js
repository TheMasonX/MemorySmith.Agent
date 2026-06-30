/**
 * vec3.js — Mineflayer-compatible Vec3 shim for the MemorySmith.Agent adapter.
 *
 * Mineflayer 4.x internally relies on prismarine-vector's Vec3 class for
 * block positions, entity positions, and direction calculations. Methods like
 * bot.dig(block) call block.position.minus(otherVec) — plain {x,y,z} objects
 * crash with "point.minus is not a function".
 *
 * This module exports a single function `toVec3(x, y, z)` that creates a
 * plain JS object implementing the FULL prismarine-vector Vec3 API surface.
 * Values are stored RAW (unfloored) so arithmetic methods (offset, plus,
 * minus, etc.) produce correct fractional results. Call .floored() to get
 * a new Vec3 with integer coordinates — critical for Mineflayer's internal
 * dig/place geometry which calls block.position.offset(0.5, 0.5, 0.5)
 * to aim at block centers instead of corners.
 *
 * Sprint 56 (TSK-0262): Fixed .floored() to return a NEW object instead of
 * `this`, and stopped flooring in the constructor. This matches the real
 * prismarine-vector Vec3 contract. Previously, .floored() returning `this`
 * caused prismarine-world's block.position = pos.floored() to leak the shim
 * into Mineflayer's internal geometry calculations, corrupting aim points.
 *
 * Vec3 API methods verified against prismarine-vector 2.x:
 *   https://github.com/PrismarineJS/prismarine-vector
 *
 * Sprint 41: Moved from inline function in index.js to standalone module
 * with the complete Vec3 API (46 methods total).
 */

export function toVec3(x, y, z) {
  const self = { x, y, z };

  // ── Coordinate access ─────────────────────────────────────────────────────

  /** Return x, y, or z by index (0=x, 1=y, 2=z). */
  self.at = function (i) { return i === 0 ? self.x : i === 1 ? self.y : self.z; };

  /** Return {x, z} (2D projection). */
  self.xz = function () { return { x: self.x, z: self.z }; };

  /** Return {x, y} (2D projection). */
  self.xy = function () { return { x: self.x, y: self.y }; };

  /** Return {y, z} (2D projection). */
  self.yz = function () { return { y: self.y, z: self.z }; };

  /** Return {x, z, y} (swizzle). */
  self.xzy = function () { return { x: self.x, z: self.z, y: self.y }; };

  /** Return [x, y, z]. */
  self.toArray = function () { return [self.x, self.y, self.z]; };

  // ── Scalar queries ────────────────────────────────────────────────────────

  /** True when all coordinates are zero. */
  self.isZero = function () { return self.x === 0 && self.y === 0 && self.z === 0; };

  /** Product x * y * z. */
  self.volume = function () { return self.x * self.y * self.z; };

  /** Euclidean magnitude sqrt(x² + y² + z²). */
  self.norm = function () { return Math.sqrt(self.x * self.x + self.y * self.y + self.z * self.z); };

  /** Dot product with another Vec3. */
  self.dot = function (other) { return self.x * other.x + self.y * other.y + self.z * other.z; };

  /** Alias for dot(). */
  self.innerProduct = function (other) { return self.dot(other); };

  /** Squared Euclidean distance to another Vec3. */
  self.distanceSquared = function (other) {
    const dx = self.x - other.x, dy = self.y - other.y, dz = self.z - other.z;
    return dx * dx + dy * dy + dz * dz;
  };

  /** Euclidean distance to another Vec3. */
  self.distanceTo = function (other) { return Math.sqrt(self.distanceSquared(other)); };

  /** Distance in the XZ plane (ignoring Y). */
  self.xzDistanceTo = function (other) {
    const dx = self.x - other.x, dz = self.z - other.z;
    return Math.sqrt(dx * dx + dz * dz);
  };

  /** Distance in the XY plane (ignoring Z). */
  self.xyDistanceTo = function (other) {
    const dx = self.x - other.x, dy = self.y - other.y;
    return Math.sqrt(dx * dx + dy * dy);
  };

  /** Distance in the YZ plane (ignoring X). */
  self.yzDistanceTo = function (other) {
    const dy = self.y - other.y, dz = self.z - other.z;
    return Math.sqrt(dy * dy + dz * dz);
  };

  /** Manhattan (taxicab) distance |dx| + |dy| + |dz|. */
  self.manhattanDistanceTo = function (other) {
    return Math.abs(self.x - other.x) + Math.abs(self.y - other.y) + Math.abs(self.z - other.z);
  };

  // ── Immutable operations (return new Vec3) ────────────────────────────────

  /** Offset by (dx, dy, dz) — returns a NEW Vec3. */
  self.offset = function (dx, dy, dz) { return toVec3(self.x + dx, self.y + dy, self.z + dz); };

  /** Alias for offset(). */
  self.translate = function (dx, dy, dz) { return self.offset(dx, dy, dz); };

  /** Add another Vec3 — returns a NEW Vec3. */
  self.plus = function (other) { return toVec3(self.x + other.x, self.y + other.y, self.z + other.z); };

  /** Alias for plus(). */
  self.add = function (other) { return self.plus(other); };

  /** Subtract another Vec3 — returns a NEW Vec3. */
  self.minus = function (other) { return toVec3(self.x - other.x, self.y - other.y, self.z - other.z); };

  /** Alias for minus(). */
  self.subtract = function (other) { return self.minus(other); };

  /**
   * Multiply by a scalar or component-wise by another Vec3.
   * When given a number, scales all axes uniformly.
   * When given a Vec3-like {x,y,z}, multiplies each component.
   * Returns a NEW Vec3.
   */
  self.multiply = function (s) {
    if (typeof s === 'number') return toVec3(self.x * s, self.y * s, self.z * s);
    return toVec3(self.x * s.x, self.y * s.y, self.z * s.z);
  };

  /** Scale by scalar — returns a NEW Vec3. Alias for multiply(scalar). */
  self.scaled = function (s) { return self.multiply(s); };

  /** Divide by scalar — returns a NEW Vec3. */
  self.divide = function (s) { return toVec3(self.x / s, self.y / s, self.z / s); };

  /** Component-wise absolute value — returns a NEW Vec3. */
  self.abs = function () { return toVec3(Math.abs(self.x), Math.abs(self.y), Math.abs(self.z)); };

  /** Component-wise modulus by scalar — returns a NEW Vec3. */
  self.modulus = function (s) { return toVec3(self.x % s, self.y % s, self.z % s); };

  /** Round each component — returns a NEW Vec3. */
  self.rounded = function () { return toVec3(Math.round(self.x), Math.round(self.y), Math.round(self.z)); };

  /**
   * Floor each component — returns a NEW Vec3.
   * IMPORTANT (Sprint 56 TSK-0262): Returns a NEW object, NOT `this`.
   * prismarine-world calls block.position = pos.floored() — if we return
   * `this`, every subsequent offset/plus/minus on that block leaks through
   * this shim and floors to integers, corrupting Mineflayer's aim geometry.
   */
  self.floored = function () {
    return toVec3(Math.floor(self.x), Math.floor(self.y), Math.floor(self.z));
  };

  /** Component-wise min with another Vec3 — returns a NEW Vec3. */
  self.min = function (other) {
    return toVec3(
      Math.min(self.x, other.x),
      Math.min(self.y, other.y),
      Math.min(self.z, other.z),
    );
  };

  /** Component-wise max with another Vec3 — returns a NEW Vec3. */
  self.max = function (other) {
    return toVec3(
      Math.max(self.x, other.x),
      Math.max(self.y, other.y),
      Math.max(self.z, other.z),
    );
  };

  /** Cross product with another Vec3 — returns a NEW Vec3. */
  self.cross = function (other) {
    return toVec3(
      self.y * other.z - self.z * other.y,
      self.z * other.x - self.x * other.z,
      self.x * other.y - self.y * other.x,
    );
  };

  /** Unit vector (normalized) — returns a NEW Vec3. Zero vector returns (0,0,0). */
  self.unit = function () {
    const n = self.norm();
    return n === 0 ? toVec3(0, 0, 0) : toVec3(self.x / n, self.y / n, self.z / n);
  };

  /** Shallow clone — returns a NEW Vec3 with same coordinates. */
  self.clone = function () { return toVec3(self.x, self.y, self.z); };

  // ── Mutable operations (modify in place, return this) ─────────────────────

  /** Set coordinates in place. Returns this for chaining. */
  self.set = function (x, y, z) { self.x = x; self.y = y; self.z = z; return self; };

  /** Alias for set(). */
  self.update = function (x, y, z) { return self.set(x, y, z); };

  /** Round coordinates in place. Returns this for chaining. */
  self.round = function () { self.x = Math.round(self.x); self.y = Math.round(self.y); self.z = Math.round(self.z); return self; };

  /** Floor coordinates in place. Returns this for chaining. */
  self.floor = function () { self.x = Math.floor(self.x); self.y = Math.floor(self.y); self.z = Math.floor(self.z); return self; };

  /** Scale coordinates in place by scalar. Returns this for chaining. */
  self.scale = function (s) { self.x *= s; self.y *= s; self.z *= s; return self; };

  /** Normalize in place to unit length. Returns this for chaining. Zero vector is unchanged. */
  self.normalize = function () {
    const n = self.norm();
    if (n !== 0) { self.x /= n; self.y /= n; self.z /= n; }
    return self;
  };

  // ── Equality / string ─────────────────────────────────────────────────────

  /** Value equality (compares x, y, z). Returns false for null/undefined. */
  self.equals = function (other) {
    return other !== null && other !== undefined
      && self.x === other.x && self.y === other.y && self.z === other.z;
  };

  /** Human-readable string "(x, y, z)". */
  self.toString = function () { return `(${self.x}, ${self.y}, ${self.z})`; };

  return self;
}
