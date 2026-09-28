import type { EffectivePolicyFlowModel, FlowElement } from "./model";

export type SearchMatchKind =
  | "label"
  | "policy tag"
  | "fragment"
  | "variable"
  | "status code"
  | "source";

export interface FlowSearchRecord {
  elementId: string;
  label: string;
  stage: string;
  path: string;
  fields: ReadonlyArray<{ kind: SearchMatchKind; value: string }>;
}

export interface FlowSearchResult extends FlowSearchRecord {
  matchKind: SearchMatchKind;
  matchText: string;
}

function pathFor(element: FlowElement, byId: ReadonlyMap<string, FlowElement>): string {
  const parts = [element.label];
  let parentId = element.parentId;
  const seen = new Set<string>();
  while (parentId && !seen.has(parentId)) {
    seen.add(parentId);
    const parent = byId.get(parentId);
    if (!parent) break;
    parts.unshift(parent.label);
    parentId = parent.parentId;
  }
  return parts.join(" › ");
}

function sourceFor(model: EffectivePolicyFlowModel, element: FlowElement): string {
  if (!element.span) return "";
  return model.source.text
    .split(/\r?\n/)
    .slice(element.span.startLine - 1, element.span.endLine)
    .join("\n");
}

export function buildSearchIndex(
  model: EffectivePolicyFlowModel,
): readonly FlowSearchRecord[] {
  const byId = new Map(model.elements.map((element) => [element.id, element]));
  return model.elements.map((element) => {
    const fields: Array<{ kind: SearchMatchKind; value: string }> = [];
    for (const code of element.exits.explicitResponseCodes) {
      fields.push({ kind: "status code", value: code });
    }
    for (const variable of new Set([...element.variablesRead, ...element.variablesWritten])) {
      fields.push({ kind: "variable", value: variable });
    }
    if (element.fragment) fields.push({ kind: "fragment", value: element.fragment.name });
    if (element.tag) fields.push({ kind: "policy tag", value: element.tag });
    fields.push({ kind: "label", value: element.label });
    const source = sourceFor(model, element);
    if (source) fields.push({ kind: "source", value: source });
    return {
      elementId: element.id,
      label: element.label,
      stage: element.stage ?? "pipeline",
      path: pathFor(element, byId),
      fields,
    };
  });
}

export function searchFlow(
  indexOrModel: readonly FlowSearchRecord[] | EffectivePolicyFlowModel,
  query: string,
): readonly FlowSearchResult[] {
  const index: readonly FlowSearchRecord[] = Array.isArray(indexOrModel)
    ? (indexOrModel as readonly FlowSearchRecord[])
    : buildSearchIndex(indexOrModel as EffectivePolicyFlowModel);
  const needle = query.trim().toLocaleLowerCase();
  if (!needle) return [];
  const results: FlowSearchResult[] = [];
  for (const record of index) {
    const field = record.fields.find((candidate) =>
      candidate.value.toLocaleLowerCase().includes(needle),
    );
    if (field) {
      results.push({ ...record, matchKind: field.kind, matchText: field.value });
    }
  }
  return results;
}
