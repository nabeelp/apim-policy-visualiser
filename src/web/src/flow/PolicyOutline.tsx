import { useEffect, useMemo } from "react";
import { EDGE_LABELS } from "./edges";
import type { InspectorSelection } from "./Inspector";
import { FLOW_IDS, STAGE_ORDER, type StageName } from "./model";
import type { FlowProjection, ProjectedNode } from "./projection";

export const STAGE_LABELS: Record<StageName, string> = {
  inbound: "Inbound",
  backend: "Backend",
  outbound: "Outbound",
  "on-error": "On-error",
};

const STAGE_DESCRIPTIONS: Record<StageName, string> = {
  inbound: "Receive, validate and route",
  backend: "Call the upstream service",
  outbound: "Prepare the response",
  "on-error": "Handle failures from any stage",
};

interface PolicyOutlineProps {
  mode: "board" | "reading";
  projection: FlowProjection;
  activeStage: StageName;
  onStageChange: (stage: StageName) => void;
  selection: InspectorSelection | null;
  highlightedIds: ReadonlySet<string>;
  pendingFocusId: string | null;
  onLayoutFocused: () => void;
  onSelect: (selection: InspectorSelection) => void;
  onToggle: (id: string) => void;
}

export default function PolicyOutline({
  mode, projection, activeStage, onStageChange, selection, highlightedIds,
  pendingFocusId, onLayoutFocused, onSelect, onToggle,
}: PolicyOutlineProps) {
  const children = useMemo(() => {
    const result = new Map<string, ProjectedNode[]>();
    for (const node of projection.nodes) {
      if (!node.parentId) continue;
      const siblings = result.get(node.parentId) ?? [];
      siblings.push(node);
      result.set(node.parentId, siblings);
    }
    for (const siblings of result.values()) {
      siblings.sort((a, b) => a.order - b.order || a.id.localeCompare(b.id));
    }
    return result;
  }, [projection.nodes]);

  useEffect(() => {
    if (!pendingFocusId) return;
    const node = projection.nodes.find((item) => item.id === pendingFocusId);
    if (!node) return;
    if (mode === "reading" && node.stage && node.stage !== activeStage) {
      onStageChange(node.stage);
      return;
    }
    const target = document.querySelector<HTMLElement>(
      `[data-flow-node-id="${CSS.escape(pendingFocusId)}"]`,
    );
    const disclosure = target?.closest("details");
    if (disclosure) disclosure.open = true;
    target?.scrollIntoView?.({ block: selection ? "center" : "start" });
    if (!selection) target?.focus();
    onLayoutFocused();
  }, [activeStage, mode, onLayoutFocused, onStageChange, pendingFocusId, projection.nodes, selection]);

  const renderNode = (node: ProjectedNode) => (
    <li key={node.id}>
      <div className={`outline-row${selection?.type === "node" && selection.id === node.id ? " is-selected" : ""}${highlightedIds.has(node.id) ? " is-highlighted" : ""}`}>
        <button
          className="outline-row__select"
          type="button"
          data-flow-node-id={node.id}
          aria-pressed={selection?.type === "node" && selection.id === node.id}
          onClick={() => onSelect({ type: "node", id: node.id })}
        >
          <strong>{node.label}</strong>
          <span className="outline-row__meta">
            <span>{node.element?.fragment ? "Fragment" : node.element?.tag ?? node.kind}</span>
            {node.explicitResponseCodes.length > 0 && (
              <span className="outline-outcome">Returns {node.explicitResponseCodes.join(" / ")}</span>
            )}
            {node.raisesError && <span className="outline-error">May raise error</span>}
            {node.loopBadge && mode === "reading" && <span>{node.loopBadge}</span>}
            {node.element?.badges.map((badge) => <span key={badge}>{badge}</span>)}
          </span>
        </button>
        {node.expandable && (
          <button
            type="button"
            className="outline-row__toggle"
            aria-label={`${node.collapsed ? "Expand" : "Collapse"} ${node.label}`}
            aria-expanded={!node.collapsed}
            onClick={() => onToggle(node.id)}
          >{node.collapsed ? "+" : "-"}</button>
        )}
      </div>
      {children.has(node.id) && (
        <ul className="outline-children" aria-label={`Contents of ${node.label}`}>
          {children.get(node.id)!.map(renderNode)}
        </ul>
      )}
    </li>
  );

  return (
    <div className={`policy-outline policy-outline--${mode}`}>
      {mode === "reading" && (
        <nav className="outline-stage-nav" aria-label="Read a policy stage">
          {STAGE_ORDER.map((stage) => (
            <button
              key={stage}
              type="button"
              aria-pressed={activeStage === stage}
              onClick={() => onStageChange(stage)}
            >{STAGE_LABELS[stage]}</button>
          ))}
        </nav>
      )}
      <p className="outline-guidance">
        Source order, not an execution trace. Branches are alternatives; select a card for conditions, connections and source.
      </p>
      <div className="outline-stages">
        {STAGE_ORDER.filter((stage) => mode === "board" || stage === activeStage).map((stage) => {
          const lane = projection.nodes.find((node) => node.id === FLOW_IDS.stage(stage));
          const items = lane ? children.get(lane.id) ?? [] : [];
          const stageEdges = projection.edges.filter((edge) => edge.source === lane?.id || edge.target === lane?.id);
          return (
            <section key={stage} className={`outline-stage outline-stage--${stage}`} aria-label={`${STAGE_LABELS[stage]} policy outline`}>
              <header>
                <div>
                  <h3>{STAGE_LABELS[stage]}</h3>
                  <p>{STAGE_DESCRIPTIONS[stage]}</p>
                </div>
                <span className="outline-count">{items.length} {items.length === 1 ? "block" : "blocks"}</span>
              </header>
              <div className="outline-stage__scroll">
                {items.length ? <ul className="outline-nodes">{items.map(renderNode)}</ul> : <p className="outline-empty">No policy blocks in this stage.</p>}
                {stageEdges.length > 0 && (
                  <details className="outline-connections">
                    <summary>Stage connections ({stageEdges.length})</summary>
                    {stageEdges.map((edge) => (
                      <button key={edge.id} type="button" onClick={() => onSelect({ type: "edge", id: edge.id })}>
                        {EDGE_LABELS[edge.kind]}{edge.label ? `: ${edge.label}` : ""}
                      </button>
                    ))}
                  </details>
                )}
              </div>
            </section>
          );
        })}
      </div>
      <details className="outline-endpoints">
        <summary>Request entries and response terminals</summary>
        <ul className="outline-nodes">
          {projection.nodes.filter((node) => !node.parentId && node.kind !== "stage").map(renderNode)}
        </ul>
      </details>
    </div>
  );
}
