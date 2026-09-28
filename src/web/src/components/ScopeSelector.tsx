import { useEffect, useMemo, useRef, useState } from "react";
import { apiFetch } from "../api/client";

interface OperationScope {
  scopeId: string;
  displayName: string;
  method: string | null;
  urlTemplate: string | null;
}

interface ApiScope {
  scopeId: string;
  displayName: string;
  path: string | null;
  operations: OperationScope[];
}

interface ProductScope {
  scopeId: string;
  displayName: string;
  apis: ApiScope[];
}

interface ScopeCatalog {
  global: {
    scopeId: string;
    displayName: string;
  };
  products: ProductScope[];
  apis: ApiScope[];
}

export interface ScopeSelectorProps {
  onScopeSelect: (scopeId: string) => void;
}

function errorMessage(error: unknown): string {
  return error instanceof Error
    ? error.message
    : "The available APIM scopes could not be loaded.";
}

export default function ScopeSelector({
  onScopeSelect,
}: ScopeSelectorProps) {
  const [catalog, setCatalog] = useState<ScopeCatalog | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedProductId, setSelectedProductId] = useState("");
  const [selectedApiId, setSelectedApiId] = useState("");
  const [selectedOperationId, setSelectedOperationId] = useState("");
  const requestSequence = useRef(0);

  useEffect(() => {
    const controller = new AbortController();
    const requestId = ++requestSequence.current;

    setCatalog(null);
    setError(null);
    void apiFetch<ScopeCatalog>("/api/scopes", {
      signal: controller.signal,
    })
      .then((nextCatalog) => {
        if (!controller.signal.aborted && requestId === requestSequence.current) {
          setCatalog(nextCatalog);
        }
      })
      .catch((requestError: unknown) => {
        if (
          !controller.signal.aborted &&
          requestId === requestSequence.current
        ) {
          setError(errorMessage(requestError));
        }
      });

    return () => controller.abort();
  }, []);

  const selectedProduct = catalog?.products.find(
    (product) => product.scopeId === selectedProductId,
  );
  const availableApis = selectedProduct?.apis ?? catalog?.apis ?? [];
  const selectedApi = useMemo(
    () => availableApis.find((api) => api.scopeId === selectedApiId),
    [availableApis, selectedApiId],
  );

  if (error) {
    return (
      <p className="ui-message ui-message--error" role="alert">
        {error}
      </p>
    );
  }

  if (!catalog) {
    return (
      <p className="ui-message" role="status">
        Loading policy scopes...
      </p>
    );
  }

  const selectGlobal = () => {
    setSelectedProductId("");
    setSelectedApiId("");
    setSelectedOperationId("");
    onScopeSelect(catalog.global.scopeId);
  };

  const resetCascade = () => {
    setSelectedProductId("");
    setSelectedApiId("");
    setSelectedOperationId("");
    onScopeSelect(catalog.global.scopeId);
  };

  const selectProduct = (scopeId: string) => {
    setSelectedProductId(scopeId);
    setSelectedApiId("");
    setSelectedOperationId("");
    onScopeSelect(scopeId || catalog.global.scopeId);
  };

  const selectApi = (scopeId: string) => {
    setSelectedApiId(scopeId);
    setSelectedOperationId("");
    onScopeSelect(
      scopeId || selectedProduct?.scopeId || catalog.global.scopeId,
    );
  };

  const selectOperation = (scopeId: string) => {
    setSelectedOperationId(scopeId);
    onScopeSelect(
      scopeId ||
        selectedApi?.scopeId ||
        selectedProduct?.scopeId ||
        catalog.global.scopeId,
    );
  };

  return (
    <fieldset className="scope-selector">
      <legend className="scope-selector__legend">Policy scope selector</legend>
      <p className="scope-selector__hint">
        Every level is selectable. Choose a parent to visualize it, or
        continue down the hierarchy.
      </p>
      <button
        className="scope-selector__global"
        type="button"
        onClick={selectGlobal}
      >
        {catalog.global.displayName}
      </button>

      <div className="scope-selector__field">
        <label htmlFor="product-scope">Product</label>
        <select
          id="product-scope"
          value={selectedProductId}
          onChange={(event) => selectProduct(event.target.value)}
        >
          <option value="">No product selected</option>
          {catalog.products.map((product) => (
            <option key={product.scopeId} value={product.scopeId}>
              {product.displayName}
            </option>
          ))}
        </select>
      </div>

      <div className="scope-selector__field">
        <label htmlFor="api-scope">API</label>
        <select
          id="api-scope"
          value={selectedApiId}
          onChange={(event) => selectApi(event.target.value)}
        >
          <option value="">No API selected</option>
          {availableApis.map((api) => (
            <option key={api.scopeId} value={api.scopeId}>
              {api.displayName}
            </option>
          ))}
        </select>
      </div>

      <div className="scope-selector__field">
        <label htmlFor="operation-scope">Operation</label>
        <select
          disabled={!selectedApi}
          id="operation-scope"
          value={selectedOperationId}
          onChange={(event) => selectOperation(event.target.value)}
        >
          <option value="">No operation selected</option>
          {selectedApi?.operations.map((operation) => (
            <option key={operation.scopeId} value={operation.scopeId}>
              {operation.displayName}
            </option>
          ))}
        </select>
      </div>
      <button
        className="scope-selector__reset"
        disabled={!selectedProductId && !selectedApiId && !selectedOperationId}
        type="button"
        onClick={resetCascade}
      >
        Reset hierarchy
      </button>
    </fieldset>
  );
}
