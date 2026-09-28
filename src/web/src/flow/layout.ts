import ELK from "elkjs/lib/elk.bundled.js";
import type { ElkNode } from "elkjs/lib/elk-api";
import type { ProjectedEdge, ProjectedNode } from "./projection";

export interface Point {
  x: number;
  y: number;
}

export interface PositionedNode extends ProjectedNode {
  position: Point;
  width: number;
  height: number;
}

export interface RoutedEdge extends ProjectedEdge {
  route: Point[];
}

export interface FlowLayout {
  nodes: PositionedNode[];
  edges: RoutedEdge[];
  width: number;
  height: number;
}

const elk = new ELK();
const HEADER_HEIGHT = 52;
const PADDING = 16;
const LANE_GAP = 80;
const ENTRY_COLUMN_WIDTH = 200;
const TERMINAL_COLUMN_WIDTH = 220;
const ON_ERROR_OFFSET = 80;

const ELK_OPTIONS = {
  "elk.algorithm": "layered",
  "elk.direction": "DOWN",
  "elk.hierarchyHandling": "INCLUDE_CHILDREN",
  "elk.layered.considerModelOrder.strategy": "NODES_AND_EDGES",
  "elk.spacing.nodeNode": "24",
  "elk.layered.spacing.nodeNodeBetweenLayers": "36",
  "elk.padding": "[top=52,left=20,bottom=20,right=20]",
} as const;

const CHAR_WIDTH = 9.6;
const LINE_HEIGHT = 22;

/** Estimated rendered height of a card: kind caption, wrapped label, optional loop and badge rows. */
function cardHeight(node: ProjectedNode, width: number): number {
  const usable = width - 28 - (node.expandable ? 40 : 0);
  const lines = Math.max(1, Math.ceil((node.label.length * CHAR_WIDTH) / Math.max(usable, 80)));
  const badgeCount =
    (node.element?.badges.length ?? 0) +
    (node.explicitResponseCodes.length ? 1 : 0) +
    (node.raisesError ? 1 : 0);
  const badgeRows = badgeCount === 0 ? 0 : Math.ceil(badgeCount / 2);
  const loopRows = node.loopBadge ? Math.ceil((node.loopBadge.length * 7) / Math.max(usable, 80)) : 0;
  return 30 + lines * LINE_HEIGHT + loopRows * 16 + badgeRows * 20;
}

function leafSize(node: ProjectedNode): { width: number; height: number } {
  if (node.element?.observability) {
    return { width: 190, height: Math.max(40, cardHeight(node, 190) - 8) };
  }
  switch (node.kind) {
    case "decision":
      return { width: 190, height: Math.max(72, cardHeight(node, 190)) };
    case "merge":
      return { width: 16, height: 16 };
    case "loop-test":
      return { width: 230, height: Math.max(64, cardHeight(node, 230)) };
    case "entry":
    case "terminal":
      return { width: 240, height: Math.max(52, cardHeight(node, 240)) };
    default: {
      const width = 250 + Math.min(70, Math.max(0, node.label.length - 30) * 2);
      return { width, height: Math.max(56, cardHeight(node, width)) };
    }
  }
}

function compareNodes(left: ProjectedNode, right: ProjectedNode): number {
  return left.order - right.order || left.id.localeCompare(right.id);
}

// Edge kinds that shape the layered order inside a container; exits to shared terminals, the On-error lane and
// data dependencies would otherwise pull nodes out of source order.
const LAYOUT_EDGE_KINDS = new Set([
  "sequence", "branch", "otherwise", "no-match", "bypass", "merge", "loop-exit", "preflight",
]);

function childAncestor(
  id: string,
  parentId: string,
  byId: ReadonlyMap<string, ProjectedNode>,
): string | undefined {
  let current = byId.get(id);
  const seen = new Set<string>();
  while (current && !seen.has(current.id)) {
    if (current.parentId === parentId) {
      return current.id;
    }
    seen.add(current.id);
    current = current.parentId ? byId.get(current.parentId) : undefined;
  }
  return undefined;
}

function containerEdges(
  parent: ProjectedNode,
  children: readonly ProjectedNode[],
  projectedEdges: readonly ProjectedEdge[],
  byId: ReadonlyMap<string, ProjectedNode>,
): { id: string; sources: string[]; targets: string[] }[] {
  const pairs = new Set<string>();
  const edges: { id: string; sources: string[]; targets: string[] }[] = [];
  const add = (source: string, target: string) => {
    const key = `${source}\u0000${target}`;
    if (source === target || pairs.has(key)) {
      return;
    }
    pairs.add(key);
    edges.push({ id: `layout:${parent.id}:${edges.length}`, sources: [source], targets: [target] });
  };
  for (const edge of projectedEdges) {
    if (!LAYOUT_EDGE_KINDS.has(edge.kind)) {
      continue;
    }
    const source = childAncestor(edge.source, parent.id, byId);
    const target = childAncestor(edge.target, parent.id, byId);
    if (source && target) {
      add(source, target);
    }
  }
  // Siblings that no flow edge connects (e.g. after a terminating branch) still stack in source order, except
  // inside a choose, where branch bodies are alternatives and must stay side by side.
  if (parent.kind !== "choose") {
    const connected = new Set([...pairs].flatMap((pair) => pair.split("\u0000")));
    for (let index = 1; index < children.length; index += 1) {
      const previous = children[index - 1].id;
      const current = children[index].id;
      if (!connected.has(current) || !connected.has(previous)) {
        add(previous, current);
      }
    }
  }
  return edges;
}

async function layoutChildren(
  parent: ProjectedNode,
  childNodes: readonly ProjectedNode[],
  sizes: Map<string, { width: number; height: number }>,
  projectedEdges: readonly ProjectedEdge[],
  byId: ReadonlyMap<string, ProjectedNode>,
): Promise<{ width: number; height: number; positions: Map<string, Point> }> {
  const children = childNodes.filter((node) => node.parentId === parent.id).sort(compareNodes);
  if (children.length === 0 || parent.collapsed) {
    const size = leafSize(parent);
    return { ...size, positions: new Map() };
  }

  const graph: ElkNode = {
    id: `layout:${parent.id}`,
    layoutOptions: ELK_OPTIONS,
    children: children.map((child) => ({
      id: child.id,
      width: sizes.get(child.id)?.width ?? leafSize(child).width,
      height: sizes.get(child.id)?.height ?? leafSize(child).height,
    })),
    edges: containerEdges(parent, children, projectedEdges, byId),
  };
  const layout = await elk.layout(graph);
  const positions = new Map<string, Point>();
  for (const child of layout.children ?? []) {
    positions.set(child.id, { x: child.x ?? PADDING, y: child.y ?? HEADER_HEIGHT });
  }
  const natural = leafSize(parent);
  // An expanded container shows a one-line header (label + outcome badges); keep it readable.
  const headerBadges =
    (parent.explicitResponseCodes.length ? 1 : 0) + (parent.raisesError ? 1 : 0) + (parent.element?.badges.length ?? 0);
  const headerWidth = Math.min(560, parent.label.length * CHAR_WIDTH + headerBadges * 110 + 90);
  return {
    width: Math.max(natural.width, layout.width ?? natural.width, headerWidth),
    height: Math.max(natural.height, layout.height ?? natural.height),
    positions,
  };
}

function descendantsFirst(nodes: readonly ProjectedNode[]): ProjectedNode[] {
  const byId = new Map(nodes.map((node) => [node.id, node]));
  const depth = (node: ProjectedNode): number => {
    let value = 0;
    let parentId = node.parentId;
    const seen = new Set<string>();
    while (parentId && byId.has(parentId) && !seen.has(parentId)) {
      seen.add(parentId);
      value += 1;
      parentId = byId.get(parentId)?.parentId ?? null;
    }
    return value;
  };
  return [...nodes].sort(
    (left, right) => depth(right) - depth(left) || compareNodes(left, right),
  );
}

function absolutePosition(
  node: PositionedNode,
  byId: ReadonlyMap<string, PositionedNode>,
): Point {
  let x = node.position.x;
  let y = node.position.y;
  let parentId = node.parentId;
  const seen = new Set<string>();
  while (parentId && !seen.has(parentId)) {
    seen.add(parentId);
    const parent = byId.get(parentId);
    if (!parent) {
      break;
    }
    x += parent.position.x;
    y += parent.position.y;
    parentId = parent.parentId;
  }
  return { x, y };
}

export async function layoutFlow(
  projectedNodes: readonly ProjectedNode[],
  projectedEdges: readonly ProjectedEdge[],
): Promise<FlowLayout> {
  const nodes = [...projectedNodes].sort(
    (left, right) => left.id.localeCompare(right.id),
  );
  const nodeById = new Map(nodes.map((node) => [node.id, node]));
  const sizes = new Map<string, { width: number; height: number }>();
  const relativePositions = new Map<string, Point>();

  for (const node of descendantsFirst(nodes)) {
    const result = await layoutChildren(node, nodes, sizes, projectedEdges, nodeById);
    sizes.set(node.id, { width: result.width, height: result.height });
    for (const [childId, position] of result.positions) {
      relativePositions.set(childId, position);
    }
  }

  const stageIds = {
    inbound: "stage:inbound",
    backend: "stage:backend",
    outbound: "stage:outbound",
    onError: "stage:on-error",
  };
  const inboundWidth = sizes.get(stageIds.inbound)?.width ?? 220;
  const backendWidth = sizes.get(stageIds.backend)?.width ?? 220;
  const outboundWidth = sizes.get(stageIds.outbound)?.width ?? 220;
  const normalX = {
    inbound: ENTRY_COLUMN_WIDTH + LANE_GAP,
    backend: ENTRY_COLUMN_WIDTH + LANE_GAP + inboundWidth + LANE_GAP,
    outbound:
      ENTRY_COLUMN_WIDTH +
      LANE_GAP +
      inboundWidth +
      LANE_GAP +
      backendWidth +
      LANE_GAP,
  };
  const normalTop = 24;
  const normalBottom = Math.max(
    (sizes.get(stageIds.inbound)?.height ?? 56) + normalTop,
    (sizes.get(stageIds.backend)?.height ?? 56) + normalTop,
    (sizes.get(stageIds.outbound)?.height ?? 56) + normalTop,
  );
  const normalRight = normalX.outbound + outboundWidth;
  const onErrorWidth = Math.max(
    sizes.get(stageIds.onError)?.width ?? 220,
    normalRight - normalX.inbound,
  );
  if (sizes.has(stageIds.onError)) {
    sizes.set(stageIds.onError, {
      width: onErrorWidth,
      height: sizes.get(stageIds.onError)!.height,
    });
  }

  const rootPositions = new Map<string, Point>([
    [stageIds.inbound, { x: normalX.inbound, y: normalTop }],
    [stageIds.backend, { x: normalX.backend, y: normalTop }],
    [stageIds.outbound, { x: normalX.outbound, y: normalTop }],
    [stageIds.onError, { x: normalX.inbound, y: normalBottom + ON_ERROR_OFFSET }],
  ]);

  const entries = nodes
    .filter((node) => node.kind === "entry" && node.parentId === null)
    .sort(compareNodes);
  entries.forEach((node, index) => {
    rootPositions.set(node.id, { x: 16, y: normalTop + 56 + index * 88 });
  });

  const terminalX = normalRight + LANE_GAP;
  const terminals = nodes
    .filter((node) => node.kind === "terminal")
    .sort(compareNodes);
  let normalIndex = 0;
  let errorIndex = 0;
  terminals.forEach((node) => {
    const isError = node.id.includes("error") && !node.id.includes("explicit");
    const slot = isError ? errorIndex++ : normalIndex++;
    rootPositions.set(node.id, {
      x: terminalX,
      y: isError
        ? normalBottom + ON_ERROR_OFFSET + 56 + slot * 96
        : normalTop + slot * 96,
    });
  });

  const depthOf = (node: ProjectedNode) => {
    let depth = 0;
    let parentId = node.parentId;
    const seen = new Set<string>();
    while (parentId && nodeById.has(parentId) && !seen.has(parentId)) {
      seen.add(parentId);
      depth += 1;
      parentId = nodeById.get(parentId)?.parentId ?? null;
    }
    return depth;
  };
  const positioned: PositionedNode[] = nodes
    .map((node) => ({
      ...node,
      position:
        node.parentId && nodeById.has(node.parentId)
          ? relativePositions.get(node.id) ?? { x: PADDING, y: HEADER_HEIGHT }
          : rootPositions.get(node.id) ?? { x: 16, y: normalTop },
      width: sizes.get(node.id)?.width ?? leafSize(node).width,
      height: sizes.get(node.id)?.height ?? leafSize(node).height,
    }))
    .sort(
      (left, right) =>
        depthOf(left) - depthOf(right) ||
        left.order - right.order ||
        left.id.localeCompare(right.id),
    );
  const positionedById = new Map(positioned.map((node) => [node.id, node]));

  const routed: RoutedEdge[] = projectedEdges.map((edge) => {
    const source = positionedById.get(edge.source);
    const target = positionedById.get(edge.target);
    if (!source || !target) {
      return { ...edge, route: [] };
    }
    const sourcePosition = absolutePosition(source, positionedById);
    const targetPosition = absolutePosition(target, positionedById);
    const start = {
      x: sourcePosition.x + source.width,
      y: sourcePosition.y + source.height / 2,
    };
    const end = {
      x: targetPosition.x,
      y: targetPosition.y + target.height / 2,
    };
    const middleX = (start.x + end.x) / 2;
    return { ...edge, route: [start, { x: middleX, y: start.y }, { x: middleX, y: end.y }, end] };
  });

  const onErrorBottom =
    normalBottom +
    ON_ERROR_OFFSET +
    (sizes.get(stageIds.onError)?.height ?? 56);
  return {
    nodes: positioned,
    edges: routed,
    width: terminalX + TERMINAL_COLUMN_WIDTH,
    height: onErrorBottom + 48,
  };
}

export default layoutFlow;
