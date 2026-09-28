---
name: progressive-disclosure-port-preservation
description: "Project a hierarchical flow model into a visible graph for any expand/collapse state as a quotient graph that lifts edges to collapsed ancestors while preserving every outcome kind and payload, verified by property tests."
---

# Skill: Outcome-Preserving Collapsed-Graph Projection

Use this skill when working on `src/web/src/flow/projection.ts` (`SPF-8`) or
any UI state that hides model elements (collapse, observability toggle,
data-dependency toggle). The invariant (`SPF-FR-14`): no outcome reachable in
the full model disappears, changes kind, or gains an unreachable target.

---

## Process

### Step 1: Compute the visible representative of every element

Walk ancestors from the root. The representative of element `e` is the
**outermost collapsed ancestor** of `e`, or `e` itself when every ancestor is
expanded. Elements whose representative is not themselves are hidden.

### Step 2: Lift every model edge

For edge `(a → b, kind, payload)` compute `(rep(a) → rep(b))`.

- If `rep(a) == rep(b)`, the edge is internal: drop it from the canvas and
  summarise it on the node (loop badge for `loop-back`, counts otherwise).
- Otherwise emit a projected edge keyed by
  `(rep(a), rep(b), kind, payloadClass)` and append the model edge ID to its
  `contributors`.

Load `references/payload-classes.md` for the payload-class table that decides which lifted edges may merge. If two edges differ in any payload-class field, keep them separate.

### Step 3: Apply visibility options

Hidden observability steps are bypassed: connect each predecessor to each
successor with the predecessor's edge kind. Data-dependency edges are removed
only when the toggle is off.

### Step 4: Verify with properties, not snapshots

For every single-group expansion state plus fully expanded:
- reachable `(outcome kind, terminal)` pairs from the Request entry are equal;
- every model edge crossing a visible boundary is in exactly one projected
  edge's `contributors`.

---

## Gotchas

- **Lift to the outermost collapsed ancestor**, not the nearest; nested
  collapsed groups otherwise leak inner nodes.
- **Never dedupe on `(source, target, kind)` alone**; two branch priorities
  into the same collapsed group must stay two edges.
- **Explicit-response edges all target one terminal**, so their union label is
  the only place status codes survive in the overview.
- **Keep projection pure.** Tests deep-freeze the model; mutation is a bug.
- **Composite nodes are synthetic groups.** Give them stable IDs derived from
  their first member so expansion state survives re-fetches.

---

## Validation

- [ ] `npm --prefix src/web run test -- run src/flow/projection.test.ts` selects and passes >0 tests.
- [ ] The property test enumerates every single-group expansion state.
- [ ] A collapsed retry shows a loop badge and no self-loop edge.
