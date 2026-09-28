import {
  BaseEdge,
  EdgeLabelRenderer,
  getBezierPath,
  getSmoothStepPath,
  type Edge,
  type EdgeProps,
  type EdgeTypes,
} from "@xyflow/react";
import { useState } from "react";
import type { ProjectedEdge } from "./projection";

export interface FlowEdgeData extends Record<string, unknown> {
  projected: ProjectedEdge;
  onSelect?: (id: string) => void;
  emphasized?: boolean;
}

export type FlowCanvasEdge = Edge<FlowEdgeData>;

const EDGE_LABELS: Record<ProjectedEdge["kind"], string> = {
  sequence: "Sequence",
  branch: "Conditional branch",
  otherwise: "Otherwise branch",
  "no-match": "No-match continuation",
  bypass: "Bypass",
  merge: "Branch merge",
  "loop-back": "Loop back",
  "loop-exit": "Loop exit",
  "explicit-response": "Explicit response",
  "raises-error": "Raises error",
  "stage-exception": "Unhandled stage failure",
  preflight: "CORS preflight",
  "data-dependency": "Data dependency",
};

function FlowEdgeRenderer(props: EdgeProps<FlowCanvasEdge>) {
  const [active, setActive] = useState(false);
  const projected = props.data?.projected;
  if (!projected) {
    return null;
  }
  const pathResult =
    projected.kind === "loop-back"
      ? getBezierPath(props)
      : getSmoothStepPath(props);
  const [edgePath, labelX, labelY] = pathResult;
  const visibleLabel =
    projected.kind === "branch"
      ? `${projected.priority ?? "?"} · ${projected.condition?.summary ?? projected.label ?? "condition"}`
      : projected.label;
  const ariaLabel = `${EDGE_LABELS[projected.kind]}${visibleLabel ? `: ${visibleLabel}` : ""}`;
  const emphasized = active || props.selected || props.data?.emphasized;

  return (
    <g
      aria-label={ariaLabel}
      className={`flow-edge flow-edge--${projected.kind} flow-edge--quiet${emphasized ? " is-emphasized" : ""}`}
      data-edge-kind={projected.kind}
      role="button"
      tabIndex={0}
      onMouseEnter={() => setActive(true)}
      onMouseLeave={() => setActive(false)}
      onFocus={() => setActive(true)}
      onBlur={() => setActive(false)}
      onClick={() => props.data?.onSelect?.(projected.id)}
      onKeyDown={(event) => {
        if (event.key === "Enter" || event.key === " ") {
          event.preventDefault();
          props.data?.onSelect?.(projected.id);
        }
      }}
    >
      <BaseEdge id={props.id} path={edgePath} markerEnd={props.markerEnd} />
      {visibleLabel && emphasized ? (
        <EdgeLabelRenderer>
          <span
            className="flow-edge__label nodrag nopan"
            style={{ transform: `translate(-50%, -50%) translate(${labelX}px, ${labelY}px)` }}
          >
            {visibleLabel}
          </span>
        </EdgeLabelRenderer>
      ) : null}
    </g>
  );
}

export const edgeTypes: EdgeTypes = {
  semantic: FlowEdgeRenderer,
};

export { EDGE_LABELS, FlowEdgeRenderer };
