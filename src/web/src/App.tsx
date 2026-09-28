import { useEffect, useRef, useState } from "react";
import ScopeSelector from "./components/ScopeSelector";
import PolicyFlowView from "./flow/PolicyFlowView";

export default function App() {
  const [selectedScopeId, setSelectedScopeId] = useState<string | null>(null);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);
  const [focusSelection, setFocusSelection] = useState(false);
  const scopeHeadingRef = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    if (!focusSelection || selectedScopeId || sidebarCollapsed) {
      return;
    }

    scopeHeadingRef.current?.focus();
    setFocusSelection(false);
  }, [focusSelection, selectedScopeId, sidebarCollapsed]);

  const backToSelection = () => {
    setSelectedScopeId(null);
    setSidebarCollapsed(false);
    setFocusSelection(true);
  };

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="app-header__identity">
          <p className="app-eyebrow">Azure API Management</p>
          <h1>Policy Flow Visualizer</h1>
        </div>
        <span className="app-status">Effective policy explorer</span>
      </header>

      <div
        className={`app-workspace${sidebarCollapsed ? " app-workspace--sidebar-collapsed" : ""}`}
      >
        <aside
          aria-labelledby="scope-panel-heading"
          className={`scope-panel${sidebarCollapsed ? " scope-panel--collapsed" : ""}`}
          id="scope-panel"
        >
          <div className="scope-panel__heading">
            <div className={sidebarCollapsed ? "visually-hidden" : undefined}>
              <p className="panel-kicker">Policy scope</p>
              <h2 id="scope-panel-heading" ref={scopeHeadingRef} tabIndex={-1}>
                Choose a scope
              </h2>
            </div>
            <button
              aria-controls="scope-panel-content"
              aria-expanded={!sidebarCollapsed}
              aria-label={
                sidebarCollapsed
                  ? "Expand scope selection panel"
                  : "Collapse scope selection panel"
              }
              className="scope-panel__toggle"
              type="button"
              onClick={() => setSidebarCollapsed((collapsed) => !collapsed)}
            >
              <span aria-hidden="true">{sidebarCollapsed ? "›" : "‹"}</span>
            </button>
          </div>
          <div
            hidden={sidebarCollapsed}
            id="scope-panel-content"
          >
            <ScopeSelector onScopeSelect={setSelectedScopeId} />
          </div>
        </aside>

        <main aria-labelledby="canvas-heading" className="policy-canvas">
          {selectedScopeId ? (
            <>
              <h2 id="canvas-heading" className="visually-hidden">
                Effective policy flow
              </h2>
              <PolicyFlowView
                scopeId={selectedScopeId}
                onBackToSelection={backToSelection}
              />
            </>
          ) : (
            <div className="canvas-placeholder">
              <p className="panel-kicker">Flow canvas</p>
              <h2 id="canvas-heading">Effective policy flow</h2>
              <p>Select a policy scope to visualize its execution path.</p>
            </div>
          )}
        </main>
      </div>
    </div>
  );
}
