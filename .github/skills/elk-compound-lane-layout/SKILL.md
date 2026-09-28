---
name: elk-compound-lane-layout
description: "Lay out a React Flow graph with bundled elkjs as independent compound lanes placed into a deterministic Inbound-Backend-Outbound column grid with a spanning On-error lane underneath."
---

# Skill: ELK Compound Lane Layout

Use this skill when working on `src/web/src/flow/layout.ts` (`SPF-9`) or when
the rendered positions of lanes, groups or terminals change.

---

## Process

### Step 1: Build one ELK graph per lane

For each stage lane, build a hierarchical ELK graph from the projected nodes
whose ancestor chain leads to that lane. Load `references/elk-options.md` for the exact ELK option set and node-size table. Change an option only when a layout test shows a concrete defect.

### Step 2: Size leaf nodes before layout

Use fixed sizes per node kind (e.g. step 220×56, decision 120×72, merge 16×16)
plus a label-length term. jsdom cannot measure DOM, so sizes must be computed
in code.

### Step 3: Place lanes on a grid

- x: entries column, then Inbound, Backend, Outbound left to right with a gap,
  then the terminals column.
- y: normal lanes top-aligned; On-error lane below the tallest normal lane.
- Widen the On-error lane to span from Inbound's left to Outbound's right.

### Step 4: Convert to React Flow

Emit parents before children. Child positions are relative to their parent
(`parentId` + `extent: "parent"`). Cross-lane edges are rendered by React Flow
with `smoothstep` routing; do not ask ELK to route them.

---

## Gotchas

- **One ELK run cannot express a spanning lane.** Placing On-error with
  hierarchy constraints alone produces a fourth column.
- **Use `elkjs/lib/elk.bundled.js`**; the default import tries to spawn a web
  worker from a URL, which breaks the offline rule and fails in jsdom.
- **Determinism:** ELK is deterministic for identical input order; sort nodes
  and edges by model order before building the graph.
- **React Flow parent order:** a child listed before its parent is silently
  positioned at the origin.
- **Layout is async.** Guard against a stale layout resolving after a newer
  expansion change (sequence number).

---

## Validation

- [ ] `npm --prefix src/web run test -- run src/flow/layout.test.ts` selects and passes >0 tests.
- [ ] Lane x order is Inbound < Backend < Outbound; On-error top is below all three.
- [ ] Every child's bounds lie within its parent's bounds.
