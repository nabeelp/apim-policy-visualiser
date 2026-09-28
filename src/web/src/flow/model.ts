// schemaVersion 2 contract for GET /api/policy/effective-flow (SPF-FR-12).
// Mirror of src/server/ApimPolicyVisualizer.Api/Policy/Model/PolicyFlowModel.cs; keep the two in sync.

export type StageName = "inbound" | "backend" | "outbound" | "on-error";

export const STAGE_ORDER: readonly StageName[] = ["inbound", "backend", "outbound", "on-error"];

export type ElementKind =
  | "stage"
  | "group"
  | "choose"
  | "decision"
  | "merge"
  | "loop"
  | "loop-test"
  | "step"
  | "terminal"
  | "entry"
  | "opaque";

export const ELEMENT_KINDS: readonly ElementKind[] = [
  "stage", "group", "choose", "decision", "merge", "loop", "loop-test", "step", "terminal", "entry", "opaque",
];

export const CONTAINER_KINDS: ReadonlySet<ElementKind> = new Set<ElementKind>(["stage", "group", "choose", "loop"]);

export type EdgeKind =
  | "sequence"
  | "branch"
  | "otherwise"
  | "no-match"
  | "bypass"
  | "merge"
  | "loop-back"
  | "loop-exit"
  | "explicit-response"
  | "raises-error"
  | "stage-exception"
  | "preflight"
  | "data-dependency";

export const EDGE_KINDS: readonly EdgeKind[] = [
  "sequence", "branch", "otherwise", "no-match", "bypass", "merge", "loop-back", "loop-exit",
  "explicit-response", "raises-error", "stage-exception", "preflight", "data-dependency",
];

export type Category =
  | "backend-call"
  | "routing"
  | "authentication"
  | "validation"
  | "transformation"
  | "state"
  | "cache"
  | "observability"
  | "cors"
  | "inherited"
  | "control"
  | "response"
  | "unknown";

export type Provenance = "structural" | "apim-rule" | "inferred" | "comment" | "fragment-name";

export type Badge = "configuration-dependent" | "variable-not-assigned" | "opaque" | "has-diagnostics";

export type DependencyClass = "configuration" | "runtime";

export const FLOW_IDS = {
  requestEntry: "entry:request",
  preflightEntry: "entry:preflight",
  onErrorEntry: "entry:on-error",
  explicitResponseTerminal: "terminal:explicit-response",
  finalResponseTerminal: "terminal:final-response",
  errorResponseTerminal: "terminal:error-response",
  gatewayDefaultErrorTerminal: "terminal:gateway-default-error",
  preflightResponseTerminal: "terminal:preflight-response",
  stage: (name: StageName) => `stage:${name}`,
} as const;

/** 1-based, inclusive positions in FlowSource.text. */
export interface SourceSpan {
  startLine: number;
  startColumn: number;
  endLine: number;
  endColumn: number;
}

export interface FlowSource {
  text: string;
  lineCount: number;
}

export interface FlowStage {
  id: string;
  name: StageName;
  present: boolean;
}

export interface FragmentOccurrence {
  name: string;
  occurrenceId: string;
  index: number;
  count: number;
  description: string | null;
}

export interface FlowAttribute {
  name: string;
  value: string;
  isExpression: boolean;
}

export interface ReturnPath {
  order: number;
  condition: string | null;
  conditionSummary: string | null;
  valueKind: "literal" | "variable" | "expression";
  valueText: string;
}

export interface ExpressionAnalysis {
  opaque: boolean;
  summary: string | null;
  variablesRead: string[];
  namedValues: string[];
  contextMembers: string[];
  returnPaths: ReturnPath[];
  hasLocalCatch: boolean;
  catchFallback: string | null;
  keyPrefix: string | null;
  diagnostics: string[];
}

export interface FlowExpression {
  location: string;
  text: string;
  span: SourceSpan | null;
  analysis: ExpressionAnalysis;
}

export interface FlowFact {
  text: string;
  provenance: Provenance;
  ruleId: string | null;
}

export interface FlowExits {
  /** Distinct status codes as strings ("403", "200", "variable"), ascending. */
  explicitResponseCodes: string[];
  raisesError: boolean;
}

export interface FlowElement {
  id: string;
  kind: ElementKind;
  stage: StageName | null;
  parentId: string | null;
  order: number;
  label: string;
  labelProvenance: Provenance;
  tag: string | null;
  category: Category;
  observability: boolean;
  fragment: FragmentOccurrence | null;
  span: SourceSpan | null;
  attributes: FlowAttribute[];
  expressions: FlowExpression[];
  properties: FlowAttribute[];
  comment: string | null;
  badges: Badge[];
  facts: FlowFact[];
  exits: FlowExits;
  variablesRead: string[];
  variablesWritten: string[];
}

export interface FlowCondition {
  text: string;
  summary: string | null;
  provenance: Provenance;
}

export interface FlowEdge {
  id: string;
  from: string;
  to: string;
  kind: EdgeKind;
  label: string | null;
  priority: number | null;
  condition: FlowCondition | null;
  facts: FlowFact[] | null;
}

export interface VariableWriter {
  elementId: string;
  literal: boolean;
  dependencyClasses: DependencyClass[];
}

export interface FlowVariable {
  name: string;
  writers: VariableWriter[];
  readers: string[];
  dependencyClasses: DependencyClass[];
}

export interface FlowDiagnostic {
  severity: "info" | "warning" | "error";
  code: string;
  message: string;
  span: SourceSpan | null;
  elementId: string | null;
}

export interface EffectivePolicyFlowModel {
  schemaVersion: 2;
  scopeId: string;
  scopeKind: string;
  source: FlowSource;
  stages: FlowStage[];
  elements: FlowElement[];
  edges: FlowEdge[];
  variables: FlowVariable[];
  diagnostics: FlowDiagnostic[];
}
