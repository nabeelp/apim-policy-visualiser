import {
  Handle,
  Position,
  type Node,
  type NodeProps,
  type NodeTypes,
} from "@xyflow/react";
import type { ProjectedNode, ProjectedNodeKind } from "./projection";

export interface FlowNodeData extends Record<string, unknown> {
  projected: ProjectedNode;
  onSelect?: (id: string) => void;
  onToggle?: (id: string) => void;
  highlighted?: boolean;
}

export type FlowCanvasNode = Node<FlowNodeData>;

const KIND_NAMES: Record<ProjectedNodeKind, string> = {
  stage: "Stage lane",
  group: "Subprocess",
  choose: "Decision subprocess",
  decision: "Decision",
  merge: "Merge",
  loop: "Retry loop",
  "loop-test": "Loop test",
  step: "Policy action",
  terminal: "Terminal",
  entry: "Entry",
  opaque: "Opaque policy",
  composite: "Composite subprocess",
};

function accessibleName(node: ProjectedNode): string {
  const parts = [KIND_NAMES[node.kind], node.label];
  if (node.explicitResponseCodes.length) {
    parts.push(`explicit responses ${node.explicitResponseCodes.join(", ")}`);
  }
  if (node.raisesError) {
    parts.push("raises error");
  }
  if (node.loopBadge) {
    parts.push(node.loopBadge);
  }
  return parts.join(", ");
}

function outcomeHandles() {
  return (
    <>
      <Handle id="incoming" type="target" position={Position.Top} aria-hidden="true" />
      <Handle id="continuation" type="source" position={Position.Bottom} aria-hidden="true" />
      <Handle id="explicit-response" type="source" position={Position.Right} className="flow-handle flow-handle--explicit" style={{ top: "30%" }} aria-hidden="true" />
      <Handle id="raises-error" type="source" position={Position.Right} className="flow-handle flow-handle--error" style={{ top: "72%" }} aria-hidden="true" />
      <Handle id="data-dependency" type="source" position={Position.Left} className="flow-handle flow-handle--data" aria-hidden="true" />
    </>
  );
}

function FlowNodeRenderer({ data, selected }: NodeProps<FlowCanvasNode>) {
  const { projected } = data;
  const element = projected.element;
  const badges = [
    ...(element?.badges ?? []),
    ...(projected.explicitResponseCodes.length
      ? [`responses ${projected.explicitResponseCodes.join(" / ")}`]
      : []),
    ...(projected.raisesError ? ["raises error"] : []),
  ];
  const isSubprocess =
    projected.expandable &&
    projected.kind !== "stage";
  const classNames = [
    "flow-node",
    `flow-node--${projected.kind}`,
    `flow-node--category-${element?.category ?? "control"}`,
    isSubprocess ? "flow-node--subprocess" : "",
    projected.collapsed ? "is-collapsed" : "is-expanded",
    element?.observability ? "flow-node--observability" : "",
    element?.badges.includes("configuration-dependent")
      ? "flow-node--configuration-dependent"
      : "",
    data.highlighted ? "is-highlighted" : "",
    selected ? "is-selected" : "",
  ]
    .filter(Boolean)
    .join(" ");

  const select = () => data.onSelect?.(projected.id);
  const toggle = () => projected.expandable && data.onToggle?.(projected.id);

  return (
    <div className={classNames} data-node-kind={projected.kind}>
      {outcomeHandles()}
      <button
        aria-label={accessibleName(projected)}
        aria-pressed={selected}
        className="flow-node__body"
        data-flow-node-id={projected.id}
        title={projected.label}
        type="button"
        onClick={select}
        onDoubleClick={toggle}
        onKeyDown={(event) => {
          if (event.key === " ") {
            event.preventDefault();
            toggle();
          } else if (event.key === "Enter") {
            event.preventDefault();
            select();
            toggle();
          }
        }}
      >
        {projected.kind === "stage" || (projected.expandable && !projected.collapsed) ? null : (
          <span className="flow-node__kind">
            {(projected.kind === "step" || projected.kind === "opaque") && element?.tag
              ? element.tag
              : KIND_NAMES[projected.kind]}
          </span>
        )}
        <strong>{projected.label}</strong>
        {projected.loopBadge ? (
          <span className="flow-node__loop-badge">↻ {projected.loopBadge}</span>
        ) : null}
        {badges.length ? (
          <span className="flow-node__badges">
            {badges.map((badge) => (
              <span className="flow-badge" key={badge}>
                {badge}
              </span>
            ))}
          </span>
        ) : null}
      </button>
      {projected.expandable && projected.kind !== "stage" ? (
        <button
          aria-expanded={!projected.collapsed}
          aria-label={`${projected.collapsed ? "Expand" : "Collapse"} ${projected.label}`}
          className="flow-node__toggle nodrag"
          tabIndex={-1}
          type="button"
          onClick={toggle}
        >
          {projected.collapsed ? "+" : "−"}
        </button>
      ) : null}
    </div>
  );
}

export const nodeTypes: NodeTypes = {
  stage: FlowNodeRenderer,
  subprocess: FlowNodeRenderer,
  composite: FlowNodeRenderer,
  step: FlowNodeRenderer,
  observability: FlowNodeRenderer,
  decision: FlowNodeRenderer,
  merge: FlowNodeRenderer,
  loop: FlowNodeRenderer,
  "loop-test": FlowNodeRenderer,
  entry: FlowNodeRenderer,
  terminal: FlowNodeRenderer,
  opaque: FlowNodeRenderer,
};

export function nodeTypeFor(node: ProjectedNode): keyof typeof nodeTypes {
  if (node.kind === "stage") return "stage";
  if (node.kind === "group" || node.kind === "choose") return "subprocess";
  if (node.kind === "composite") return "composite";
  if (node.kind === "loop") return "loop";
  if (node.kind === "step" && node.element?.observability) return "observability";
  return node.kind;
}

export { FlowNodeRenderer };
