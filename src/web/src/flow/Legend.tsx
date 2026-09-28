import { EDGE_KINDS, ELEMENT_KINDS } from "./model";

const NODE_NAMES: Record<string, string> = {
  stage: "Stage lane",
  group: "Fragment subprocess",
  choose: "Decision subprocess",
  decision: "Decision diamond",
  merge: "Merge dot",
  loop: "Retry loop",
  "loop-test": "Loop test",
  step: "Policy action",
  terminal: "Caller terminal",
  entry: "Request entry",
  opaque: "Opaque policy",
  composite: "Composite subprocess",
};

export default function Legend() {
  return (
    <details className="flow-legend">
      <summary>Legend</summary>
      <div className="flow-legend__content">
        <section aria-labelledby="legend-nodes">
          <h3 id="legend-nodes">Nodes</h3>
          <ul>
            {[...ELEMENT_KINDS, "composite"].map((kind) => (
              <li key={kind} data-legend-node-kind={kind}>
                <span aria-hidden="true" className={`legend-node legend-node--${kind}`} />
                {NODE_NAMES[kind]}
              </li>
            ))}
          </ul>
        </section>
        <section aria-labelledby="legend-edges">
          <h3 id="legend-edges">Edges</h3>
          <ul>
            {EDGE_KINDS.map((kind) => (
              <li key={kind} data-legend-edge-kind={kind}>
                <span aria-hidden="true" className={`legend-edge legend-edge--${kind}`} />
                {kind.replaceAll("-", " ")}
              </li>
            ))}
          </ul>
        </section>
      </div>
    </details>
  );
}
