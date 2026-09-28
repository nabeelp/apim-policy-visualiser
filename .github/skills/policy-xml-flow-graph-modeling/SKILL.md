---
name: policy-xml-flow-graph-modeling
description: "Convert APIM effective-policy XML into a source-mapped, fragment-grouped, APIM-semantics-aware hierarchical execution model (decisions with merge/no-match, terminals, retry loops, stage exception lanes) without compiling or evaluating policy expressions."
---

# Skill: Policy XML Execution Model Building

Use this skill when changing the backend pipeline that turns ARM effective
policy XML into the schemaVersion 2 model served by
`GET /api/policy/effective-flow` (`SPF-1`–`SPF-7`):
`PolicySourceLoader` → `FragmentRegionBuilder` → `PolicyExpressionAnalyzer` →
`PolicyFlowModelBuilder` (+ `ApimSemanticRules`, `PolicyStateAnalyzer`).

Load `references/apim-execution-rules.md` before touching edge construction or
the rule table — it is the normative mapping from tags/attributes to edges.

---

## Process

### Step 1: Load with source positions and comments

Use `XmlReader` with `IXmlLineInfo`, `DtdProcessing.Prohibit`,
`XmlResolver = null`, and **comments retained**. Record 1-based start/end
line/column spans for every element, comment and expression-bearing attribute.
Undo ARM's extra escaping only for values containing `@(`/`@{`. Keep the
original text unmodified; the UI slices source by span.

**Output:** source-mapped tree + original text + line count.

### Step 2: Build fragment-occurrence regions

Pair sibling comments `include-fragment: Begin {name} policy fragment scope` /
`... End ...`. Each occurrence is `{name}#{n}` (document order per name) and is
never merged with another occurrence. Label precedence: preceding single-line
sibling comment minus `Step N:` → `Fragment: {title}` line in the first inner
comment → humanised name. Record `labelProvenance`.

**Output:** nested region list; diagnostics for unmatched/crossed markers.

### Step 3: Build structural elements

Walk each stage in order. Wrap region siblings in `group` elements. Emit
`step`, `decision`/`clause`/`merge`, `loop`/`loop-test`, `terminal` and
`opaque` elements per `references/apim-execution-rules.md`. Fold configuration
children (`value`, `dimension`, `audiences`, …) into their parent. Classify
each element into exactly one category.

**Output:** elements with stable IDs derived from structural path.

### Step 4: Connect control flow

Each builder call returns the element's **entry** and its **open exits** (the
continuations that must be joined to whatever follows). Terminals and
raises-error exits are closed exits. Join open exits of item *i* to the entry
of item *i+1*; at block end, propagate open exits upward. This single rule
produces merges, bypasses, loop-test joins and loop-exit resolution without
special cases.

**Output:** typed edges; aggregated `exits` on every ancestor.

### Step 5: Apply APIM rules and state analysis

Add entries, stage links, stage-exception edges and terminals; attach
`apim-rule` facts with `ruleId`. Build the variable index and dependency
classes; add configuration-dependent badges and cache data-dependency edges.

**Output:** complete schemaVersion 2 model.

### Step 6: Validate integrity before returning

Every edge endpoint and `parentId` resolves; IDs are unique; every source
element appears exactly once; every fact has a provenance.

---

## Gotchas

- **Comments are data, never code.** A commented-out `<include-fragment>` must
  never become an element. Never re-parse comment text as XML.
- **A C# `return` is not a pipeline return.** Only `<return-response>` ends the
  pipeline. Expression-local `try/catch` is not an On-error transfer.
- **`forward-request` HTTP errors go to Outbound** unless
  `fail-on-error-status-code="true"`; transport failures always raise.
- **Occurrence identity beats deduplication.** The same fragment in outbound
  and on-error is two nodes; merging them falsely connects the two paths.
- **Loop-back stays inside the loop.** It targets the first body element, not
  the retry's predecessor, and never re-enters Inbound.
- **Data-dependency edges are not control flow.** Exclude them from every
  reachability or counting helper.
- **Never claim unreachable.** Configuration-dependent branches get a badge;
  nothing is hidden or removed.
- **Stable IDs matter.** The frontend keys expansion state and tests on IDs;
  derive them from structural path, not from counters shared across stages.

---

## Validation

- [ ] `dotnet test ... --filter FullyQualifiedName~PolicyFlowModelBuilderTests` selects and passes >0 tests.
- [ ] A `choose` without `otherwise` has a `no-match` edge to its merge.
- [ ] A nested `return-response` has one `explicit-response` edge and no sequence edge onward.
- [ ] A retry whose body contains a `choose` joins both arms to the loop test.
- [ ] Building the synthetic fixture twice yields identical IDs.
- [ ] Every edge endpoint and `parentId` resolves.

If an edge is missing, check the open-exit propagation in Step 4 first; most
defects come from dropping an exit when a block ends.
