import {
  CONTAINER_KINDS,
  type EdgeKind,
  type EffectivePolicyFlowModel,
  type FlowCondition,
  type FlowEdge,
  type FlowElement,
  type FlowFact,
} from "./model";

export type ProjectedNodeKind = FlowElement["kind"] | "composite";

export interface ProjectionOptions {
  showDataDependencies?: boolean;
  showObservability?: boolean;
}

export interface ProjectedNode {
  id: string;
  kind: ProjectedNodeKind;
  stage: FlowElement["stage"];
  parentId: string | null;
  order: number;
  label: string;
  element: FlowElement | null;
  synthetic: boolean;
  collapsed: boolean;
  expandable: boolean;
  memberIds: string[];
  explicitResponseCodes: string[];
  raisesError: boolean;
  hasDataDependency: boolean;
  loopBadge: string | null;
  internalEdgeKinds: EdgeKind[];
}

export interface ProjectedEdge {
  id: string;
  source: string;
  target: string;
  kind: EdgeKind;
  label: string | null;
  priority: number | null;
  condition: FlowCondition | null;
  facts: FlowFact[] | null;
  contributors: string[];
  statusCodes: string[];
}

export interface FlowProjection {
  nodes: ProjectedNode[];
  edges: ProjectedEdge[];
}

interface Composite {
  id: string;
  stageId: string;
  stage: FlowElement["stage"];
  order: number;
  members: FlowElement[];
}

interface WorkingEdge extends FlowEdge {
  contributors: string[];
}

const DEFAULT_OPTIONS: Required<ProjectionOptions> = {
  showDataDependencies: false,
  showObservability: true,
};

const CONTAINER_KIND_SET = CONTAINER_KINDS as ReadonlySet<string>;
const SIMPLE_TOP_LEVEL_KINDS = new Set(["step", "opaque"]);

function compareElement(left: FlowElement, right: FlowElement): number {
  return left.order - right.order || left.id.localeCompare(right.id);
}

function compositeId(firstMember: FlowElement): string {
  return `composite:${firstMember.id}`;
}

function buildComposites(model: EffectivePolicyFlowModel): Composite[] {
  const result: Composite[] = [];
  for (const stage of model.elements.filter((element) => element.kind === "stage")) {
    const children = model.elements
      .filter((element) => element.parentId === stage.id)
      .sort(compareElement);
    let run: FlowElement[] = [];
    const flush = () => {
      if (run.length >= 2) {
        result.push({
          id: compositeId(run[0]),
          stageId: stage.id,
          stage: run[0].stage,
          order: run[0].order,
          members: run,
        });
      }
      run = [];
    };

    for (const child of children) {
      if (SIMPLE_TOP_LEVEL_KINDS.has(child.kind) && !child.fragment) {
        run.push(child);
      } else {
        flush();
      }
    }
    flush();
  }
  return result;
}

function ancestorIds(
  elementById: ReadonlyMap<string, FlowElement>,
  id: string,
): string[] {
  const ancestors: string[] = [];
  let current = elementById.get(id);
  const seen = new Set<string>();
  while (current?.parentId && !seen.has(current.parentId)) {
    seen.add(current.parentId);
    ancestors.unshift(current.parentId);
    current = elementById.get(current.parentId);
  }
  return ancestors;
}

function reconnectHiddenObservability(
  elements: readonly FlowElement[],
  sourceEdges: readonly FlowEdge[],
): WorkingEdge[] {
  const hidden = new Set(
    elements.filter((element) => element.observability).map((element) => element.id),
  );
  let edges: WorkingEdge[] = sourceEdges.map((edge) => ({
    ...edge,
    contributors: [edge.id],
  }));

  for (const hiddenId of [...hidden].sort()) {
    const incoming = edges.filter((edge) => edge.to === hiddenId);
    const outgoing = edges.filter((edge) => edge.from === hiddenId);
    const untouched = edges.filter(
      (edge) => edge.from !== hiddenId && edge.to !== hiddenId,
    );
    const bypasses: WorkingEdge[] = [];

    for (const before of incoming) {
      for (const after of outgoing) {
        if (before.from === after.to) {
          continue;
        }
        bypasses.push({
          ...before,
          id: `bypass-observability:${before.id}:${after.id}`,
          to: after.to,
          contributors: [...before.contributors, ...after.contributors],
        });
      }
    }
    edges = [...untouched, ...bypasses];
  }

  return edges.filter((edge) => !hidden.has(edge.from) && !hidden.has(edge.to));
}

function payloadClass(edge: WorkingEdge): string {
  switch (edge.kind) {
    case "branch":
      return `priority:${edge.priority ?? "none"}:${edge.condition?.text ?? ""}`;
    case "otherwise":
    case "no-match":
    case "bypass":
      return edge.kind;
    case "explicit-response":
      return "explicit";
    case "raises-error":
    case "stage-exception":
      return "error";
    case "loop-back":
      return "loop";
    case "data-dependency":
      return "data";
    default:
      return "flow";
  }
}

function edgeLabel(kind: EdgeKind, edge: WorkingEdge, codes: string[]): string | null {
  switch (kind) {
    case "explicit-response":
      return codes.join(" / ") || edge.label || "explicit response";
    case "raises-error":
      return edge.label ?? "raises error";
    case "stage-exception":
      return edge.label ?? "unhandled failure";
    case "otherwise":
      return edge.label ?? "Otherwise";
    case "no-match":
      return edge.label ?? "No match";
    case "bypass":
      return edge.label ?? "Bypass";
    case "data-dependency":
      return edge.label ?? "affects subsequent requests";
    default:
      return edge.label;
  }
}

function numericCodeSort(left: string, right: string): number {
  const leftNumber = Number(left);
  const rightNumber = Number(right);
  return (
    (Number.isFinite(leftNumber) ? leftNumber : Number.MAX_SAFE_INTEGER) -
      (Number.isFinite(rightNumber) ? rightNumber : Number.MAX_SAFE_INTEGER) ||
    left.localeCompare(right)
  );
}

function loopSummary(element: FlowElement, model: EffectivePolicyFlowModel): string {
  const test = model.elements.find(
    (candidate) =>
      candidate.parentId === element.id && candidate.kind === "loop-test",
  );
  const condition =
    test?.expressions[0]?.analysis.summary ??
    test?.expressions[0]?.text ??
    test?.label ??
    "retry condition";
  const count =
    element.attributes.find((attribute) => attribute.name === "count")?.value ??
    "configured";
  const interval =
    element.attributes.find((attribute) => attribute.name === "interval")?.value;
  return `Loop: ${condition}; budget ${count}${interval ? ` × ${interval}s` : ""}`;
}

export function collapseToOverview(
  model: EffectivePolicyFlowModel,
): ReadonlySet<string> {
  return new Set(
    model.elements
      .filter((element) => element.kind === "stage")
      .map((element) => element.id),
  );
}

export function expandAncestors(
  model: EffectivePolicyFlowModel,
  elementId: string,
  expandedIds: ReadonlySet<string> = collapseToOverview(model),
): ReadonlySet<string> {
  const elementById = new Map(model.elements.map((element) => [element.id, element]));
  const next = new Set(expandedIds);
  const chain = ancestorIds(elementById, elementId);
  for (const id of chain) {
    if (CONTAINER_KIND_SET.has(elementById.get(id)?.kind ?? "")) {
      next.add(id);
    }
  }
  // Synthetic composites are not model ancestors; open the one that hides the target or its top-level ancestor.
  const members = new Set([elementId, ...chain]);
  for (const composite of buildComposites(model)) {
    if (composite.members.some((member) => members.has(member.id))) {
      next.add(composite.id);
    }
  }
  return next;
}

export function expandAll(model: EffectivePolicyFlowModel): ReadonlySet<string> {
  return new Set([
    ...model.elements
      .filter((element) => CONTAINER_KIND_SET.has(element.kind))
      .map((element) => element.id),
    ...buildComposites(model).map((composite) => composite.id),
  ]);
}

export function projectFlow(
  model: EffectivePolicyFlowModel,
  expandedIds: ReadonlySet<string> = collapseToOverview(model),
  options: ProjectionOptions = {},
): FlowProjection {
  const resolvedOptions = { ...DEFAULT_OPTIONS, ...options };
  const elementById = new Map(model.elements.map((element) => [element.id, element]));
  const composites = buildComposites(model);
  const compositeByMember = new Map<string, Composite>();
  for (const composite of composites) {
    for (const member of composite.members) {
      compositeByMember.set(member.id, composite);
    }
  }

  const visibleRepresentative = (id: string): string => {
    const element = elementById.get(id);
    if (!element) {
      return id;
    }
    const chain = [...ancestorIds(elementById, id), id];
    for (const ancestorId of chain) {
      const ancestor = elementById.get(ancestorId);
      if (
        ancestor &&
        CONTAINER_KIND_SET.has(ancestor.kind) &&
        !expandedIds.has(ancestorId)
      ) {
        return ancestorId;
      }
    }
    const composite = compositeByMember.get(id);
    if (composite && !expandedIds.has(composite.id)) {
      return composite.id;
    }
    return id;
  };

  const hiddenObservability = new Set(
    resolvedOptions.showObservability
      ? []
      : model.elements
          .filter((element) => element.observability)
          .map((element) => element.id),
  );
  const visibleOriginal = model.elements.filter(
    (element) =>
      !hiddenObservability.has(element.id) &&
      visibleRepresentative(element.id) === element.id,
  );
  const visibleIds = new Set(visibleOriginal.map((element) => element.id));

  const projectedNodes: ProjectedNode[] = visibleOriginal.map((element) => {
    const directComposite = compositeByMember.get(element.id);
    let parentId = element.parentId;
    if (directComposite && expandedIds.has(directComposite.id)) {
      parentId = directComposite.id;
    } else if (parentId && !visibleIds.has(parentId)) {
      parentId = visibleRepresentative(parentId);
    }
    const collapsed =
      CONTAINER_KIND_SET.has(element.kind) && !expandedIds.has(element.id);
    return {
      id: element.id,
      kind: element.kind,
      stage: element.stage,
      parentId,
      order: element.order,
      label: element.label,
      element,
      synthetic: false,
      collapsed,
      expandable: CONTAINER_KIND_SET.has(element.kind),
      memberIds: [element.id],
      explicitResponseCodes: [...element.exits.explicitResponseCodes],
      raisesError: element.exits.raisesError,
      hasDataDependency: false,
      loopBadge:
        collapsed && element.kind === "loop" ? loopSummary(element, model) : null,
      internalEdgeKinds: [],
    };
  });

  for (const composite of composites) {
    if (
      visibleRepresentative(composite.stageId) !== composite.stageId ||
      !expandedIds.has(composite.stageId)
    ) {
      continue;
    }
    const expanded = expandedIds.has(composite.id);
    projectedNodes.push({
      id: composite.id,
      kind: "composite",
      stage: composite.stage,
      parentId: composite.stageId,
      order: composite.order,
      label: `${composite.members[0].label} + ${composite.members.length - 1} more`,
      element: null,
      synthetic: true,
      collapsed: !expanded,
      expandable: true,
      memberIds: composite.members.map((member) => member.id),
      explicitResponseCodes: [],
      raisesError: false,
      hasDataDependency: false,
      loopBadge: null,
      internalEdgeKinds: [],
    });
  }

  let workingEdges = resolvedOptions.showObservability
    ? model.edges.map((edge) => ({ ...edge, contributors: [edge.id] }))
    : reconnectHiddenObservability(model.elements, model.edges);
  if (!resolvedOptions.showDataDependencies) {
    workingEdges = workingEdges.filter((edge) => edge.kind !== "data-dependency");
  }

  const nodeById = new Map(projectedNodes.map((node) => [node.id, node]));
  const groups = new Map<string, { edge: WorkingEdge; contributors: string[]; codes: Set<string> }>();
  for (const edge of workingEdges) {
    const source = visibleRepresentative(edge.from);
    const target = visibleRepresentative(edge.to);
    if (source === target) {
      const node = nodeById.get(source);
      if (node && !node.internalEdgeKinds.includes(edge.kind)) {
        node.internalEdgeKinds.push(edge.kind);
      }
      continue;
    }
    if (!nodeById.has(source) || !nodeById.has(target)) {
      continue;
    }
    const key = `${source}\u0000${target}\u0000${edge.kind}\u0000${payloadClass(edge)}`;
    const sourceElement = elementById.get(edge.from);
    const codes = new Set(
      edge.kind === "explicit-response"
        ? sourceElement?.exits.explicitResponseCodes ?? []
        : [],
    );
    const group = groups.get(key);
    if (group) {
      group.contributors.push(...edge.contributors);
      codes.forEach((code) => group.codes.add(code));
    } else {
      groups.set(key, {
        edge: { ...edge, from: source, to: target },
        contributors: [...edge.contributors],
        codes,
      });
    }
  }

  const projectedEdges: ProjectedEdge[] = [...groups.values()].map(
    ({ edge, contributors, codes }, index) => {
      const statusCodes = [...codes].sort(numericCodeSort);
      const sourceNode = nodeById.get(edge.from);
      if (sourceNode) {
        if (edge.kind === "explicit-response") {
          sourceNode.explicitResponseCodes = [
            ...new Set([...sourceNode.explicitResponseCodes, ...statusCodes]),
          ].sort(numericCodeSort);
        }
        if (edge.kind === "raises-error" || edge.kind === "stage-exception") {
          sourceNode.raisesError = true;
        }
        if (edge.kind === "data-dependency") {
          sourceNode.hasDataDependency = true;
        }
      }
      return {
        id: `p:${edge.from}->${edge.to}:${edge.kind}:${index}`,
        source: edge.from,
        target: edge.to,
        kind: edge.kind,
        label: edgeLabel(edge.kind, edge, statusCodes),
        priority: edge.priority,
        condition: edge.condition,
        facts: edge.facts,
        contributors: [...new Set(contributors)],
        statusCodes,
      };
    },
  );

  return {
    nodes: projectedNodes.sort(
      (left, right) =>
        (left.stage ?? "").localeCompare(right.stage ?? "") ||
        left.order - right.order ||
        left.id.localeCompare(right.id),
    ),
    edges: projectedEdges.sort((left, right) => left.id.localeCompare(right.id)),
  };
}

export const projectPolicyFlow = projectFlow;
