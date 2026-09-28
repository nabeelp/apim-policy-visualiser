import {
  Controls,
  MarkerType,
  ReactFlow,
  ReactFlowProvider,
  useReactFlow,
  type Edge,
  type Node,
} from "@xyflow/react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../api/client";
import { edgeTypes, type FlowEdgeData } from "./edges";
import FlowSearch from "./FlowSearch";
import Inspector, { type InspectorSelection } from "./Inspector";
import { layoutFlow, type FlowLayout } from "./layout";
import Legend from "./Legend";
import { STAGE_ORDER, type EffectivePolicyFlowModel, type StageName } from "./model";
import { nodeTypeFor, nodeTypes, type FlowNodeData } from "./nodes";
import PolicyOutline, { STAGE_LABELS } from "./PolicyOutline";
import {
  collapseToOverview,
  expandAll,
  expandAncestors,
  projectFlow,
  type FlowProjection,
} from "./projection";
import type { FlowSearchResult } from "./search";

export interface PolicyFlowViewProps {
  scopeId: string;
  onBackToSelection: () => void;
}

const FIT_OPTIONS = { padding: 0.12, maxZoom: 1.25, duration: 250 } as const;
type Presentation = "quiet" | "board" | "reading";
const PRESENTATIONS: { id: Presentation; label: string; description: string }[] = [
  { id: "quiet", label: "Flow map", description: "Connections stay visible. Hover, focus or select to reveal their labels." },
  { id: "board", label: "Stage board", description: "Scan the policy by stage. Outcomes stay on the cards; connections live in the inspector." },
  { id: "reading", label: "Reading view", description: "Read one stage at a time. Expand only what you need and inspect alongside it." },
];

function sourceHandle(kind: string): string {
  if (kind === "explicit-response") return "explicit-response";
  if (kind === "raises-error" || kind === "stage-exception") return "raises-error";
  if (kind === "data-dependency") return "data-dependency";
  return "continuation";
}

function FlowCanvas({
  model,
  projection,
  layout,
  selection,
  highlightedIds,
  pendingFocusId,
  onToggle,
  onSelect,
  onLayoutFocused,
}: {
  model: EffectivePolicyFlowModel;
  projection: FlowProjection;
  layout: FlowLayout;
  selection: InspectorSelection | null;
  highlightedIds: ReadonlySet<string>;
  pendingFocusId: string | null;
  onToggle: (id: string) => void;
  onSelect: (selection: InspectorSelection) => void;
  onLayoutFocused: () => void;
}) {
  const reactFlow = useReactFlow();
  const initialFitDone = useRef(false);
  const flowNodes = useMemo<Node<FlowNodeData>[]>(
    () =>
      layout.nodes.map((node) => ({
        id: node.id,
        type: nodeTypeFor(node),
        parentId: node.parentId ?? undefined,
        extent: node.parentId ? ("parent" as const) : undefined,
        expandParent: false,
        position: node.position,
        width: node.width,
        height: node.height,
        draggable: false,
        selectable: true,
        selected: selection?.type === "node" && selection.id === node.id,
        data: {
          projected: node,
          highlighted: highlightedIds.has(node.id),
          onToggle,
          onSelect: (id: string) => onSelect({ type: "node", id }),
        },
        style: { width: node.width, height: node.height },
        ariaLabel: `${node.kind} ${node.label}`,
      })),
    [highlightedIds, layout.nodes, onSelect, onToggle, selection],
  );
  const flowEdges = useMemo<Edge<FlowEdgeData>[]>(
    () =>
      projection.edges.map((edge) => ({
        id: edge.id,
        source: edge.source,
        target: edge.target,
        sourceHandle: sourceHandle(edge.kind),
        targetHandle: "incoming",
        type: "semantic",
        markerEnd: { type: MarkerType.ArrowClosed, color: "#a8bacd" },
        selected: selection?.type === "edge" && selection.id === edge.id,
        data: {
          projected: edge,
          emphasized: selection?.type === "node" &&
            (edge.source === selection.id || edge.target === selection.id),
          onSelect: (id: string) => onSelect({ type: "edge", id }),
        },
      })),
    [onSelect, projection.edges, selection],
  );

  useEffect(() => {
    if (!initialFitDone.current && flowNodes.length) {
      initialFitDone.current = true;
      requestAnimationFrame(() => void reactFlow.fitView(FIT_OPTIONS));
    }
  }, [flowNodes.length, reactFlow]);

  useEffect(() => {
    const focusedNode = layout.nodes.find((node) => node.id === pendingFocusId);
    if (!pendingFocusId || !focusedNode) return;
    const byId = new Map(layout.nodes.map((node) => [node.id, node]));
    let { x, y } = focusedNode.position;
    let parent = focusedNode.parentId ? byId.get(focusedNode.parentId) : undefined;
    while (parent) {
      x += parent.position.x;
      y += parent.position.y;
      parent = parent.parentId ? byId.get(parent.parentId) : undefined;
    }
    const frame = requestAnimationFrame(() => {
      // Use the completed layout, not React Flow's still-updating internal parent bounds.
      void reactFlow.fitBounds({ x, y, width: focusedNode.width, height: focusedNode.height }, FIT_OPTIONS);
      if (!selection) {
        document
          .querySelector<HTMLElement>(`[data-flow-node-id="${CSS.escape(pendingFocusId)}"]`)
          ?.focus({ preventScroll: true });
      }
      onLayoutFocused();
    });
    return () => cancelAnimationFrame(frame);
  }, [layout.nodes, onLayoutFocused, pendingFocusId, reactFlow, selection]);

  return (
    <ReactFlow
      aria-label="Interactive policy graph"
      className="flow-canvas flow-canvas--quiet"
      edges={flowEdges}
      edgeTypes={edgeTypes}
      elementsSelectable
      fitView={false}
      minZoom={0.05}
      nodes={flowNodes}
      nodesConnectable={false}
      nodesDraggable={false}
      nodeTypes={nodeTypes}
      nodesFocusable={false}
      panOnDrag
      proOptions={{ hideAttribution: true }}
      role="region"
      zoomOnDoubleClick={false}
    >
      <Controls position="bottom-left" />
    </ReactFlow>
  );
}

function PolicyFlowViewInner({
  scopeId,
  onBackToSelection,
}: PolicyFlowViewProps) {
  const [model, setModel] = useState<EffectivePolicyFlowModel | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [expandedIds, setExpandedIds] = useState<ReadonlySet<string>>(new Set());
  const [showDataDependencies, setShowDataDependencies] = useState(false);
  const [showObservability, setShowObservability] = useState(true);
  const [layout, setLayout] = useState<FlowLayout | null>(null);
  const [laidOutProjection, setLaidOutProjection] = useState<FlowProjection | null>(null);
  const [selection, setSelection] = useState<InspectorSelection | null>(null);
  const [highlightedIds, setHighlightedIds] = useState<ReadonlySet<string>>(new Set());
  const [pendingFocusId, setPendingFocusId] = useState<string | null>(null);
  const [presentation, setPresentation] = useState<Presentation>("quiet");
  const [activeStage, setActiveStage] = useState<StageName>("inbound");
  const requestSequence = useRef(0);
  const layoutSequence = useRef(0);
  const lastSelectedNodeId = useRef<string | null>(null);
  const reactFlow = useReactFlow();

  useEffect(() => {
    const controller = new AbortController();
    const sequence = ++requestSequence.current;
    // Invalidate any layout still running for the previous scope.
    layoutSequence.current += 1;
    setModel(null);
    setLayout(null);
    setLaidOutProjection(null);
    setError(null);
    setSelection(null);
    setPendingFocusId(null);
    setHighlightedIds(new Set());
    setActiveStage("inbound");
    void apiFetch<EffectivePolicyFlowModel>(
      `/api/policy/effective-flow?scope=${encodeURIComponent(scopeId)}`,
      { signal: controller.signal },
    )
      .then((response) => {
        if (!controller.signal.aborted && sequence === requestSequence.current) {
          setModel(response);
          setExpandedIds(collapseToOverview(response));
        }
      })
      .catch((requestError: unknown) => {
        if (!controller.signal.aborted && sequence === requestSequence.current) {
          setError(requestError instanceof Error ? requestError.message : String(requestError));
        }
      });
    return () => controller.abort();
  }, [scopeId]);

  const currentModel = model?.scopeId === scopeId ? model : null;
  const projection = useMemo(
    () =>
      currentModel
        ? projectFlow(currentModel, expandedIds, {
            showDataDependencies,
            showObservability,
          })
        : null,
    [currentModel, expandedIds, showDataDependencies, showObservability],
  );

  useEffect(() => {
    if (!projection) return;
    let cancelled = false;
    const sequence = ++layoutSequence.current;
    const current = () => !cancelled && sequence === layoutSequence.current;
    layoutFlow(projection.nodes, projection.edges)
      .then((nextLayout) => {
        if (current()) {
          setLayout(nextLayout);
          setLaidOutProjection(projection);
        }
      })
      .catch((layoutError: unknown) => {
        if (current()) {
          setError(`The policy flow could not be laid out: ${layoutError instanceof Error ? layoutError.message : String(layoutError)}`);
        }
      });
    return () => {
      cancelled = true;
    };
  }, [projection]);

  const toggle = useCallback((id: string) => {
    setExpandedIds((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
    setPendingFocusId(id);
  }, []);

  const chooseSearchResult = useCallback(
    (result: FlowSearchResult) => {
      if (!currentModel) return;
      setExpandedIds((current) =>
        expandAncestors(currentModel, result.elementId, current),
      );
      lastSelectedNodeId.current = result.elementId;
      setSelection({ type: "node", id: result.elementId });
      setPendingFocusId(result.elementId);
    },
    [currentModel],
  );

  const highlightVariable = useCallback(
    (name: string) => {
      if (!currentModel) return;
      const variable = currentModel.variables.find((item) => item.name === name);
      if (!variable) return;
      const ids = [
        ...variable.writers.map((writer) => writer.elementId),
        ...variable.readers,
      ];
      setHighlightedIds(new Set(ids));
      setExpandedIds((current) =>
        ids.reduce(
          (expanded, id) => expandAncestors(currentModel, id, expanded),
          current,
        ),
      );
      if (ids[0]) setPendingFocusId(ids[0]);
    },
    [currentModel],
  );

  const returnFocus = useCallback(() => {
    if (lastSelectedNodeId.current) {
      document
        .querySelector<HTMLElement>(
          `[data-flow-node-id="${CSS.escape(lastSelectedNodeId.current)}"]`,
        )
        ?.focus({ preventScroll: true });
    }
  }, []);

  const selectItem = useCallback((nextSelection: InspectorSelection) => {
    if (nextSelection.type === "node") {
      lastSelectedNodeId.current = nextSelection.id;
      const stage = projection?.nodes.find((node) => node.id === nextSelection.id)?.stage;
      if (stage) setActiveStage(stage);
      if (presentation === "board" || presentation === "reading") {
        setPendingFocusId(nextSelection.id);
      }
    }
    setSelection(nextSelection);
  }, [presentation, projection]);

  const layoutFocused = useCallback(() => setPendingFocusId(null), []);

  if (error) {
    return (
      <section aria-label="Policy flow error" className="policy-flow-state policy-flow-state--error">
        <div className="ui-message ui-message--error" role="alert">
          <strong>Effective policy could not be displayed.</strong>
          <span>{error}</span>
        </div>
        <button type="button" onClick={onBackToSelection}>Back to scope selection</button>
      </section>
    );
  }
  if (!currentModel || !projection || !layout) {
    return (
      <section aria-label="Policy flow loading" className="policy-flow-state">
        <p className="ui-message" role="status">Loading effective policy flow…</p>
      </section>
    );
  }

  const displayControls = (
    <>
      <label className="flow-toggle">
        <input
          checked={showDataDependencies}
          type="checkbox"
          onChange={(event) => setShowDataDependencies(event.target.checked)}
        />
        Data dependencies
      </label>
      <label className="flow-toggle">
        <input
          checked={showObservability}
          type="checkbox"
          onChange={(event) => setShowObservability(event.target.checked)}
        />
        Observability
      </label>
      <button type="button" onClick={onBackToSelection}>Back to scope selection</button>
    </>
  );

  return (
    <section
      aria-label={`Effective policy flow for ${scopeId}`}
      className={`semantic-flow semantic-flow--${presentation}`}
    >
      <div className="flow-presentation">
        <div className="flow-toolbar__title">
          <strong>{currentModel.scopeKind}</strong>
          <span title={currentModel.scopeId}>{currentModel.scopeId}</span>
        </div>
        <div className="flow-presentation__choices" role="group" aria-label="Visualization views">
          {PRESENTATIONS.map((option) => (
            <button
              type="button"
              key={option.id}
              aria-pressed={presentation === option.id}
              aria-description={option.description}
              title={option.description}
              onClick={() => {
                setPresentation(option.id);
                setPendingFocusId(null);
              }}
            >{option.label}</button>
          ))}
        </div>
      </div>
      <div className="flow-toolbar" role="toolbar" aria-label="Policy flow controls">
        <FlowSearch model={currentModel} onChoose={chooseSearchResult} />
        <button
          type="button"
          onClick={() => {
            setExpandedIds(collapseToOverview(currentModel));
            setPendingFocusId(null);
          }}
        >
          Collapse to overview
        </button>
        <button type="button" onClick={() => setExpandedIds(expandAll(currentModel))}>
          Expand all
        </button>
        {presentation === "quiet" && (
          <button type="button" onClick={() => void reactFlow.fitView(FIT_OPTIONS)}>
            Fit view
          </button>
        )}
        {presentation === "quiet" && (
          <label className="flow-stage-jump">
            <span className="visually-hidden">Focus stage</span>
            <select
              aria-label="Focus graph stage"
              value=""
              onChange={(event) => {
                if (event.target.value) setPendingFocusId(event.target.value);
              }}
            >
              <option value="">Choose stage</option>
              {STAGE_ORDER.map((stage) => <option key={stage} value={`stage:${stage}`}>{STAGE_LABELS[stage]}</option>)}
            </select>
          </label>
        )}
        <details className="flow-options">
          <summary>Display options</summary>
          <div className="flow-options__content">{displayControls}</div>
        </details>
        {presentation === "quiet" && <Legend />}
      </div>

      <div className={`flow-workspace${selection ? " has-inspector" : ""}`}>
        {presentation === "quiet" ? (
          <FlowCanvas
            highlightedIds={highlightedIds}
            layout={layout}
            model={currentModel}
            onLayoutFocused={layoutFocused}
            onSelect={selectItem}
            onToggle={toggle}
            pendingFocusId={laidOutProjection === projection ? pendingFocusId : null}
            projection={projection}
            selection={selection}
          />
        ) : (
          <PolicyOutline
            mode={presentation}
            projection={projection}
            activeStage={activeStage}
            onStageChange={setActiveStage}
            selection={selection}
            highlightedIds={highlightedIds}
            pendingFocusId={pendingFocusId}
            onLayoutFocused={layoutFocused}
            onSelect={selectItem}
            onToggle={toggle}
          />
        )}

        {selection ? (
          <Inspector
            model={currentModel}
            onClose={() => setSelection(null)}
            onHighlightVariable={highlightVariable}
            onSelectEdge={(id) => selectItem({ type: "edge", id })}
            projectedEdges={projection.edges}
            projectedNodes={projection.nodes}
            returnFocus={returnFocus}
            selection={selection}
          />
        ) : null}
      </div>
      <details className="flow-diagnostics">
        <summary>Diagnostics ({currentModel.diagnostics.length})</summary>
        {currentModel.diagnostics.length ? (
          <ul>
            {currentModel.diagnostics.map((item, index) => (
              <li key={`${item.code}:${index}`}>
                <strong>{item.severity}: {item.code}</strong> — {item.message}
              </li>
            ))}
          </ul>
        ) : <p>No diagnostics.</p>}
      </details>
    </section>
  );
}

export default function PolicyFlowView(props: PolicyFlowViewProps) {
  return (
    <ReactFlowProvider>
      <PolicyFlowViewInner {...props} />
    </ReactFlowProvider>
  );
}

export { FlowCanvas, PolicyFlowViewInner };
