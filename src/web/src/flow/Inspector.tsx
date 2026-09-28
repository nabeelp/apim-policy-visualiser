import { useEffect, useMemo, useRef } from "react";
import type {
  EffectivePolicyFlowModel,
  FlowElement,
  Provenance,
} from "./model";
import type { ProjectedEdge, ProjectedNode } from "./projection";

export type InspectorSelection =
  | { type: "node"; id: string }
  | { type: "edge"; id: string };

export interface InspectorProps {
  model: EffectivePolicyFlowModel;
  selection: InspectorSelection;
  projectedNodes?: readonly ProjectedNode[];
  projectedEdges: readonly ProjectedEdge[];
  onClose: () => void;
  onSelectEdge: (id: string) => void;
  onHighlightVariable: (name: string) => void;
  returnFocus?: () => void;
}

const PROVENANCE_LABEL: Record<Provenance, string> = {
  structural: "Structural",
  "apim-rule": "APIM rule",
  inferred: "Inferred",
  comment: "Comment",
  "fragment-name": "Fragment name",
};

function structuralPath(
  element: FlowElement,
  byId: ReadonlyMap<string, FlowElement>,
): FlowElement[] {
  const result: FlowElement[] = [element];
  let parentId = element.parentId;
  const seen = new Set<string>();
  while (parentId && !seen.has(parentId)) {
    seen.add(parentId);
    const parent = byId.get(parentId);
    if (!parent) break;
    result.unshift(parent);
    parentId = parent.parentId;
  }
  return result;
}

function sourceSlice(model: EffectivePolicyFlowModel, element: FlowElement) {
  if (!element.span) return [];
  const lines = model.source.text.split(/\r?\n/);
  return lines
    .slice(element.span.startLine - 1, element.span.endLine)
    .map((text, index) => ({
      number: element.span!.startLine + index,
      text:
        element.span!.startLine === element.span!.endLine
          ? text.slice(element.span!.startColumn - 1, element.span!.endColumn)
          : index === 0
            ? text.slice(element.span!.startColumn - 1)
            : element.span!.startLine + index === element.span!.endLine
              ? text.slice(0, element.span!.endColumn)
              : text,
    }));
}

function ProvenanceChip({ provenance }: { provenance: Provenance }) {
  return (
    <span className={`provenance-chip provenance-chip--${provenance}`}>
      {PROVENANCE_LABEL[provenance]}
    </span>
  );
}

export default function Inspector({
  model,
  selection,
  projectedNodes = [],
  projectedEdges,
  onClose,
  onSelectEdge,
  onHighlightVariable,
  returnFocus,
}: InspectorProps) {
  const closeRef = useRef<HTMLButtonElement>(null);
  const elementById = useMemo(
    () => new Map(model.elements.map((element) => [element.id, element])),
    [model],
  );
  const selectedNode =
    selection.type === "node" ? elementById.get(selection.id) ?? null : null;
  const selectedProjectedNode =
    selection.type === "node"
      ? projectedNodes.find((node) => node.id === selection.id) ?? null
      : null;
  const selectedEdge =
    selection.type === "edge"
      ? projectedEdges.find((edge) => edge.id === selection.id) ?? null
      : null;

  useEffect(() => {
    closeRef.current?.focus();
  }, [selection.id, selection.type]);

  const close = () => {
    onClose();
    queueMicrotask(() => returnFocus?.());
  };
  const relevantEdges = selection.type === "node"
    ? projectedEdges.filter(
        (edge) => edge.source === selection.id || edge.target === selection.id,
      )
    : [];
  const conditions = selectedNode
    ? selectedNode.expressions.filter((expression) => expression.location.includes("condition"))
    : [];
  const variables = selectedNode
    ? [...new Set([...selectedNode.variablesRead, ...selectedNode.variablesWritten])]
    : [];
  const diagnostics = selectedNode
    ? model.diagnostics.filter((item) => item.elementId === selectedNode.id)
    : [];

  return (
    <aside
      aria-label="Flow inspector"
      className="flow-inspector"
      onKeyDown={(event) => {
        if (event.key === "Escape") {
          event.preventDefault();
          close();
        }
      }}
    >
      <header className="flow-inspector__header">
        <div>
          <span className="panel-kicker">Inspector</span>
          <h2>{selectedNode?.label ?? selectedProjectedNode?.label ?? selectedEdge?.label ?? selectedEdge?.kind ?? "Selection"}</h2>
        </div>
        <button ref={closeRef} type="button" onClick={close} aria-label="Close inspector">
          ×
        </button>
      </header>

      {selectedNode ? (
        <div className="flow-inspector__content">
          <section>
            <h3>Identity</h3>
            <dl>
              <dt>Policy</dt>
              <dd>
                {selectedNode.fragment
                  ? `${selectedNode.fragment.name} #${selectedNode.fragment.index} of ${selectedNode.fragment.count} · ${selectedNode.stage}`
                  : selectedNode.tag ?? selectedNode.kind}
              </dd>
              <dt>Stage and structural path</dt>
              <dd>
                <ol className="inspector-breadcrumb">
                  {structuralPath(selectedNode, elementById).map((item) => (
                    <li key={item.id}>{item.label}</li>
                  ))}
                </ol>
              </dd>
            </dl>
          </section>

          {conditions.length ? (
            <section>
              <h3>Condition</h3>
              {conditions.map((expression) => (
                <div key={`${expression.location}:${expression.text}`}>
                  <code>{expression.text}</code>
                  {expression.analysis.summary ? (
                    <p>
                      <ProvenanceChip provenance="inferred" /> {expression.analysis.summary}
                    </p>
                  ) : null}
                </div>
              ))}
            </section>
          ) : null}

          {selectedNode.span ? (
            <section>
              <h3>
                Source lines {selectedNode.span.startLine}–{selectedNode.span.endLine}
              </h3>
              <pre className="inspector-source" aria-label="Exact policy source slice" tabIndex={0}>
                {sourceSlice(model, selectedNode).map((line) => (
                  <span key={line.number}>
                    <b>{line.number}</b> {line.text}
                    {"\n"}
                  </span>
                ))}
              </pre>
            </section>
          ) : null}

          {variables.length ? (
            <section>
              <h3>Variables</h3>
              {variables.map((name) => {
                const variable = model.variables.find((item) => item.name === name);
                return (
                  <details key={name}>
                    <summary>
                      {name} ({selectedNode.variablesWritten.includes(name) ? "written" : "read"})
                    </summary>
                    <p>
                      Writers: {variable?.writers.map((writer) => elementById.get(writer.elementId)?.label ?? writer.elementId).join(", ") || "none in this policy"}
                    </p>
                    <p>
                      Readers: {variable?.readers.map((id) => elementById.get(id)?.label ?? id).join(", ") || "none"}
                    </p>
                    <button type="button" onClick={() => onHighlightVariable(name)}>
                      Highlight {name} writers and readers
                    </button>
                  </details>
                );
              })}
            </section>
          ) : null}

          {selectedNode.comment ? (
            <section>
              <h3>Comment</h3>
              <p><ProvenanceChip provenance="comment" /> {selectedNode.comment}</p>
            </section>
          ) : null}

          {selectedNode.badges.length ? (
            <section>
              <h3>Badges</h3>
              <ul>{selectedNode.badges.map((badge) => <li key={badge}>{badge}</li>)}</ul>
            </section>
          ) : null}

          {diagnostics.length ? (
            <section>
              <h3>Diagnostics</h3>
              <ul>{diagnostics.map((item) => <li key={item.code}><strong>{item.code}</strong>: {item.message}</li>)}</ul>
            </section>
          ) : null}

          {selectedNode.facts.length ? (
            <section>
              <h3>Facts</h3>
              <ul className="inspector-facts">
                {selectedNode.facts.map((fact, index) => (
                  <li key={`${fact.text}:${index}`}>
                    <ProvenanceChip provenance={fact.provenance} /> {fact.text}
                  </li>
                ))}
              </ul>
            </section>
          ) : null}

          <section>
            <h3>Incoming and outgoing edges</h3>
            {relevantEdges.length ? (
              <ul className="inspector-edge-list">
                {relevantEdges.map((edge) => {
                  const incoming = edge.target === selectedNode.id;
                  const otherId = incoming ? edge.source : edge.target;
                  return (
                    <li key={edge.id}>
                      <button type="button" onClick={() => onSelectEdge(edge.id)}>
                        {incoming ? "Incoming" : "Outgoing"} {edge.kind}
                        {edge.label ? ` · ${edge.label}` : ""} ·{" "}
                        {elementById.get(otherId)?.label ?? otherId}
                      </button>
                    </li>
                  );
                })}
              </ul>
            ) : <p>No visible boundary edges.</p>}
          </section>
        </div>
      ) : selectedProjectedNode ? (
        <div className="flow-inspector__content">
          <section>
            <h3>Composite identity</h3>
            <p>
              <ProvenanceChip provenance="structural" />{" "}
              {selectedProjectedNode.kind} in {selectedProjectedNode.stage ?? "pipeline"}
            </p>
            <p>{selectedProjectedNode.memberIds.length} grouped policy actions</p>
          </section>
          <section>
            <h3>Incoming and outgoing edges</h3>
            <ul className="inspector-edge-list">
              {relevantEdges.map((edge) => (
                <li key={edge.id}>
                  <button type="button" onClick={() => onSelectEdge(edge.id)}>
                    {edge.target === selectedProjectedNode.id ? "Incoming" : "Outgoing"}{" "}
                    {edge.kind}{edge.label ? ` · ${edge.label}` : ""}
                  </button>
                </li>
              ))}
            </ul>
          </section>
        </div>
      ) : selectedEdge ? (
        <div className="flow-inspector__content">
          <section>
            <h3>Edge</h3>
            <dl>
              <dt>Kind</dt><dd>{selectedEdge.kind}</dd>
              <dt>From</dt><dd>{elementById.get(selectedEdge.source)?.label ?? selectedEdge.source}</dd>
              <dt>To</dt><dd>{elementById.get(selectedEdge.target)?.label ?? selectedEdge.target}</dd>
              {selectedEdge.priority != null ? <><dt>Priority</dt><dd>{selectedEdge.priority}</dd></> : null}
            </dl>
          </section>
          {selectedEdge.condition ? (
            <section>
              <h3>Condition</h3>
              <code>{selectedEdge.condition.text}</code>
              {selectedEdge.condition.summary ? (
                <p><ProvenanceChip provenance="inferred" /> {selectedEdge.condition.summary}</p>
              ) : null}
            </section>
          ) : null}
          {selectedEdge.facts?.length ? (
            <section>
              <h3>Facts</h3>
              <ul>{selectedEdge.facts.map((fact, index) => <li key={`${fact.text}:${index}`}><ProvenanceChip provenance={fact.provenance} /> {fact.text}</li>)}</ul>
            </section>
          ) : null}
          <section>
            <h3>Contributing model edges</h3>
            <ul>{selectedEdge.contributors.map((id) => <li key={id}><code>{id}</code></li>)}</ul>
          </section>
        </div>
      ) : (
        <p>The selected item is no longer visible.</p>
      )}
    </aside>
  );
}

export { ProvenanceChip, sourceSlice, structuralPath };
