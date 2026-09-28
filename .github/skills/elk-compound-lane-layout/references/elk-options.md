# ELK Layout Options and Node Sizes

> Load when: configuring ELK options, node sizes or lane grid spacing in `layout.ts`.

## ELK options per lane graph

```js
{
  "elk.algorithm": "layered",
  "elk.direction": "RIGHT",
  "elk.hierarchyHandling": "INCLUDE_CHILDREN",
  "elk.layered.considerModelOrder.strategy": "NODES_AND_EDGES",
  "elk.spacing.nodeNode": "28",
  "elk.layered.spacing.nodeNodeBetweenLayers": "48",
  "elk.padding": "[top=44,left=16,bottom=16,right=16]"
}
```

Include only edges whose endpoints are both in the lane.

## Leaf node sizes (px)

| Kind | Width | Height |
|------|-------|--------|
| step / composite / collapsed group | 220 (+ up to 80 for long labels) | 56 |
| observability | 160 | 36 |
| decision | 132 | 72 |
| merge | 16 | 16 |
| loop-test | 150 | 64 |
| entry / terminal | 180 | 52 |

## Grid spacing

Lane gap 80, entries column width 200, terminals column width 220, On-error
lane offset 80 below the tallest normal lane.
