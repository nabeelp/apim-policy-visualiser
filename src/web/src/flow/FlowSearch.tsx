import { useId, useMemo, useState } from "react";
import type { EffectivePolicyFlowModel } from "./model";
import { buildSearchIndex, searchFlow, type FlowSearchResult } from "./search";

export interface FlowSearchProps {
  model: EffectivePolicyFlowModel;
  onChoose: (result: FlowSearchResult) => void;
}

export default function FlowSearch({ model, onChoose }: FlowSearchProps) {
  const id = useId();
  const [query, setQuery] = useState("");
  const [activeIndex, setActiveIndex] = useState(0);
  const index = useMemo(() => buildSearchIndex(model), [model]);
  const results = useMemo(() => searchFlow(index, query), [index, query]);
  const listId = `${id}-results`;

  const choose = (result: FlowSearchResult | undefined) => {
    if (result) onChoose(result);
  };

  return (
    <div className="flow-search">
      <label className="visually-hidden" htmlFor={id}>Search policy flow</label>
      <input
        aria-autocomplete="list"
        aria-controls={listId}
        aria-expanded={results.length > 0}
        aria-activedescendant={results[activeIndex] ? `${id}-result-${activeIndex}` : undefined}
        autoComplete="off"
        id={id}
        placeholder="Search policy flow"
        title="Search policy, variable, status or source"
        role="combobox"
        type="search"
        value={query}
        onChange={(event) => {
          setQuery(event.target.value);
          setActiveIndex(0);
        }}
        onKeyDown={(event) => {
          if (event.key === "ArrowDown") {
            event.preventDefault();
            setActiveIndex((current) => Math.min(results.length - 1, current + 1));
          } else if (event.key === "ArrowUp") {
            event.preventDefault();
            setActiveIndex((current) => Math.max(0, current - 1));
          } else if (event.key === "Enter") {
            event.preventDefault();
            choose(results[activeIndex]);
          } else if (event.key === "Escape") {
            setQuery("");
          }
        }}
      />
      <ul id={listId} role="listbox" aria-label="Policy flow search results">
        {results.map((result, resultIndex) => (
          <li
            aria-selected={resultIndex === activeIndex}
            id={`${id}-result-${resultIndex}`}
            key={result.elementId}
            role="option"
            tabIndex={-1}
            onClick={() => choose(result)}
          >
            <strong>{result.label}</strong>
            <span>{result.stage} · {result.path}</span>
            <small>Matched {result.matchKind}</small>
          </li>
        ))}
      </ul>
      {query && results.length === 0 ? <p role="status">No matching policy elements.</p> : null}
    </div>
  );
}
