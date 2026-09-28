import type {
  Category,
  EffectivePolicyFlowModel,
  FlowEdge,
  FlowElement,
  Provenance,
  StageName,
} from "../model";

const sourceLines = [
  "<policies>",
  "  <inbound>",
  "    <!-- Authenticate subscription -->",
  "    <validate-jwt header-name=\"Authorization\" />",
  "    <return-response><set-status code=\"401\" /></return-response>",
  "    <return-response><set-status code=\"403\" /></return-response>",
  "    <return-response><set-status code=\"503\" /></return-response>",
  "    <choose><when condition=\"@(context.Request.Headers.ContainsKey(&quot;x-bypass&quot;))\"></when></choose>",
  "    <cache-lookup-value key=\"responses\" variable-name=\"owner\" />",
  "    <set-backend-service backend-id=\"pool\" />",
  "    <mystery-policy />",
  "    <cors />",
  "  </inbound>",
  "  <backend><retry count=\"3\"><forward-request /></retry></backend>",
  "  <outbound><cache-store-value key=\"responses\" value=\"owner\" /></outbound>",
  "  <on-error><choose><when condition=\"@(context.Response.StatusCode == 429)\"><emit-metric name=\"throttle\" /></when></choose></on-error>",
  "</policies>",
];

const source = sourceLines.join("\n");

function element(
  id: string,
  kind: FlowElement["kind"],
  label: string,
  stage: StageName | null,
  parentId: string | null,
  order: number,
  overrides: Partial<FlowElement> = {},
): FlowElement {
  const tag = kind === "step" || kind === "opaque" ? label.toLowerCase().replaceAll(" ", "-") : null;
  return {
    id,
    kind,
    stage,
    parentId,
    order,
    label,
    labelProvenance: "structural",
    tag,
    category: "control",
    observability: false,
    fragment: null,
    span: null,
    attributes: [],
    expressions: [],
    properties: [],
    comment: null,
    badges: [],
    facts: [],
    exits: { explicitResponseCodes: [], raisesError: false },
    variablesRead: [],
    variablesWritten: [],
    ...overrides,
  };
}

function fragment(
  id: string,
  name: string,
  stage: StageName,
  parentId: string,
  order: number,
  index = 1,
  count = 1,
): FlowElement {
  return element(id, "group", count > 1 ? `${name} · ${stage}` : name, stage, parentId, order, {
    labelProvenance: "fragment-name",
    fragment: {
      name,
      occurrenceId: `${name}#${index}`,
      index,
      count,
      description: `${name} policy fragment`,
    },
  });
}

function step(
  id: string,
  label: string,
  stage: StageName,
  parentId: string,
  order: number,
  tag: string,
  category: Category,
  overrides: Partial<FlowElement> = {},
): FlowElement {
  return element(id, "step", label, stage, parentId, order, {
    tag,
    category,
    span: { startLine: Math.min(order + 3, 16), startColumn: 5, endLine: Math.min(order + 3, 16), endColumn: 40 },
    ...overrides,
  });
}

function condition(text: string, summary: string) {
  return { text, summary, provenance: "inferred" as Provenance };
}

const elements: FlowElement[] = [
  element("entry:request", "entry", "Incoming API request", null, null, 0),
  element("entry:preflight", "entry", "CORS preflight request (if no matching OPTIONS operation)", null, null, 1, {
    category: "cors",
    badges: ["configuration-dependent"],
    facts: [{ text: "A matching OPTIONS operation disables gateway preflight handling.", provenance: "apim-rule", ruleId: "cors-preflight" }],
  }),
  element("stage:inbound", "stage", "Inbound", null, null, 2),
  element("stage:backend", "stage", "Backend", null, null, 3),
  element("stage:outbound", "stage", "Outbound", null, null, 4),
  element("stage:on-error", "stage", "On-error", null, null, 5),
  element("terminal:explicit-response", "terminal", "Explicit response to caller", null, null, 6, { category: "response" }),
  element("terminal:final-response", "terminal", "Final backend response to caller", null, null, 7, { category: "response" }),
  element("terminal:error-response", "terminal", "Error response to caller", null, null, 8, { category: "response" }),
  element("terminal:preflight-response", "terminal", "Preflight response to caller", null, null, 9, { category: "cors" }),

  fragment("inbound/fragment:authentication#1", "Authenticate subscription", "inbound", "stage:inbound", 0),
  step("inbound/fragment:authentication#1/0", "Derive authentication metadata", "inbound", "inbound/fragment:authentication#1", 0, "set-variable", "authentication", { variablesWritten: ["auth-type"] }),
  step("inbound/fragment:authentication#1/1", "Return 401 Unauthorized", "inbound", "inbound/fragment:authentication#1", 1, "return-response", "response", { exits: { explicitResponseCodes: ["401"], raisesError: false } }),
  step("inbound/fragment:authentication#1/2", "Return 403 Forbidden", "inbound", "inbound/fragment:authentication#1", 2, "return-response", "response", { exits: { explicitResponseCodes: ["403"], raisesError: false } }),
  step("inbound/fragment:authentication#1/3", "Return 503 Service Unavailable", "inbound", "inbound/fragment:authentication#1", 3, "return-response", "response", { exits: { explicitResponseCodes: ["503"], raisesError: false } }),
  step("inbound/fragment:authentication#1/4", "Validate JWT", "inbound", "inbound/fragment:authentication#1", 4, "validate-jwt", "validation", { span: { startLine: 4, startColumn: 5, endLine: 4, endColumn: 49 }, exits: { explicitResponseCodes: [], raisesError: true }, variablesRead: ["jwtRequired"], facts: [{ text: "Validation failure transfers to On-error.", provenance: "apim-rule", ruleId: "validate-jwt-error" }] }),
  step("inbound/fragment:authentication#1/5", "Remove API key header", "inbound", "inbound/fragment:authentication#1", 5, "set-header", "transformation"),

  element("inbound/1", "choose", "Choose bypass route", "inbound", "stage:inbound", 1),
  element("inbound/1/decision", "decision", "Bypass requested?", "inbound", "inbound/1", 0, {
    tag: "when",
    badges: ["configuration-dependent", "variable-not-assigned"],
    variablesRead: ["bypassEnabled"],
    expressions: [{ location: "@condition", text: "@(context.Variables.GetValueOrDefault<bool>(\"bypassEnabled\"))", span: { startLine: 8, startColumn: 19, endLine: 8, endColumn: 87 }, analysis: { opaque: false, summary: "bypass is enabled", variablesRead: ["bypassEnabled"], namedValues: [], contextMembers: [], returnPaths: [], hasLocalCatch: false, catchFallback: null, keyPrefix: null, diagnostics: [] } }],
  }),
  element("inbound/1/merge", "merge", "Continue after bypass choice", "inbound", "inbound/1", 1),

  fragment("inbound/fragment:responses-lookup#1", "Responses API cache lookup", "inbound", "stage:inbound", 2),
  step("inbound/fragment:responses-lookup#1/0", "Lookup response owner", "inbound", "inbound/fragment:responses-lookup#1", 0, "cache-lookup-value", "cache", { variablesWritten: ["responseOwner"], properties: [{ name: "key", value: "responses", isExpression: false }] }),
  step("inbound/3", "Resolve requested model", "inbound", "stage:inbound", 3, "set-variable", "routing", { variablesWritten: ["requestedModel"], variablesRead: ["responseOwner"] }),
  step("inbound/4", "Select backend pool", "inbound", "stage:inbound", 4, "set-backend-service", "routing"),
  element("inbound/5", "opaque", "Unknown extension policy", "inbound", "stage:inbound", 5, { tag: "mystery-policy", category: "unknown", badges: ["opaque", "has-diagnostics"], span: { startLine: 11, startColumn: 5, endLine: 11, endColumn: 23 } }),
  step("inbound/6", "Apply CORS", "inbound", "stage:inbound", 6, "cors", "cors"),

  element("backend/0", "loop", "Retry backend request", "backend", "stage:backend", 0, {
    tag: "retry",
    attributes: [{ name: "count", value: "3", isExpression: false }, { name: "interval", value: "1", isExpression: false }],
  }),
  fragment("backend/0/fragment:retry-prepare#1", "Prepare retry attempt", "backend", "backend/0", 0),
  step("backend/0/fragment:retry-prepare#1/0", "Select retry backend", "backend", "backend/0/fragment:retry-prepare#1", 0, "set-backend-service", "routing"),
  fragment("backend/0/fragment:retry-auth#1", "Sign backend request", "backend", "backend/0", 1),
  step("backend/0/fragment:retry-auth#1/0", "Set backend authorization", "backend", "backend/0/fragment:retry-auth#1", 0, "authentication-managed-identity", "authentication"),
  step("backend/0/2", "Forward request to backend", "backend", "backend/0", 2, "forward-request", "backend-call", { exits: { explicitResponseCodes: [], raisesError: true } }),
  element("backend/0/test", "loop-test", "Retry while backend unavailable", "backend", "backend/0", 3, {
    tag: "retry",
    expressions: [{ location: "@condition", text: "@(context.Response.StatusCode == 429)", span: { startLine: 14, startColumn: 20, endLine: 14, endColumn: 57 }, analysis: { opaque: false, summary: "response status is 429", variablesRead: [], namedValues: [], contextMembers: ["Response.StatusCode"], returnPaths: [], hasLocalCatch: false, catchFallback: null, keyPrefix: null, diagnostics: [] } }],
  }),

  step("outbound/0", "Store response owner", "outbound", "stage:outbound", 0, "cache-store-value", "cache", { variablesRead: ["responseOwner"], properties: [{ name: "key", value: "responses", isExpression: false }] }),
  fragment("outbound/fragment:set-response-headers#1", "Set response headers", "outbound", "stage:outbound", 1, 1, 2),
  step("outbound/fragment:set-response-headers#1/0", "Add diagnostic response header", "outbound", "outbound/fragment:set-response-headers#1", 0, "set-header", "observability", { observability: true }),

  element("entry:on-error", "entry", "Enter error handler", "on-error", "stage:on-error", 0),
  element("on-error/0", "choose", "Choose error response", "on-error", "stage:on-error", 1),
  element("on-error/0/decision", "decision", "Response status is 429?", "on-error", "on-error/0", 0, {
    tag: "when",
    expressions: [{ location: "@condition", text: "@(context.Response.StatusCode == 429)", span: { startLine: 16, startColumn: 26, endLine: 16, endColumn: 63 }, analysis: { opaque: false, summary: "response status is 429", variablesRead: [], namedValues: [], contextMembers: ["Response.StatusCode"], returnPaths: [], hasLocalCatch: false, catchFallback: null, keyPrefix: null, diagnostics: [] } }],
  }),
  step("on-error/0/1", "Emit AI throttling metric", "on-error", "on-error/0", 1, "emit-metric", "observability", { observability: true }),
  element("on-error/0/merge", "merge", "Continue error handling", "on-error", "on-error/0", 2),
  fragment("on-error/fragment:set-response-headers#2", "Set response headers", "on-error", "stage:on-error", 2, 2, 2),
  step("on-error/fragment:set-response-headers#2/0", "Add diagnostic response header", "on-error", "on-error/fragment:set-response-headers#2", 0, "set-header", "observability", { observability: true }),
];

function edge(
  from: string,
  to: string,
  kind: FlowEdge["kind"],
  label: string | null = null,
  priority: number | null = null,
  edgeCondition: FlowEdge["condition"] = null,
): FlowEdge {
  return {
    id: `e:${from}->${to}:${kind}${priority ? `:${priority}` : ""}`,
    from,
    to,
    kind,
    label,
    priority,
    condition: edgeCondition,
    facts: null,
  };
}

const auth = "inbound/fragment:authentication#1";
const lookup = "inbound/fragment:responses-lookup#1";
const loop = "backend/0";
const edges: FlowEdge[] = [
  edge("entry:request", `${auth}/0`, "sequence"),
  edge(`${auth}/0`, `${auth}/1`, "branch", "Missing subscription", 1, condition("subscription == null", "subscription is missing")),
  edge(`${auth}/0`, `${auth}/2`, "branch", "Role missing", 2, condition("!hasRole", "required role is missing")),
  edge(`${auth}/0`, `${auth}/3`, "branch", "Configuration missing", 3, condition("jwtConfig == null", "JWT configuration is missing")),
  edge(`${auth}/0`, `${auth}/4`, "sequence"),
  edge(`${auth}/1`, "terminal:explicit-response", "explicit-response"),
  edge(`${auth}/2`, "terminal:explicit-response", "explicit-response"),
  edge(`${auth}/3`, "terminal:explicit-response", "explicit-response"),
  edge(`${auth}/4`, `${auth}/5`, "sequence"),
  edge(`${auth}/4`, "entry:on-error", "raises-error"),
  edge(`${auth}/5`, "inbound/1/decision", "sequence"),
  edge("inbound/1/decision", "inbound/1/merge", "bypass", "Bypass", 1, condition("bypass", "bypass is enabled")),
  edge("inbound/1/decision", "inbound/1/merge", "no-match", "No match"),
  edge("inbound/1/merge", `${lookup}/0`, "sequence"),
  edge(`${lookup}/0`, "inbound/3", "sequence"),
  edge("inbound/3", "inbound/4", "sequence"),
  edge("inbound/4", "inbound/5", "sequence"),
  edge("inbound/5", "inbound/6", "sequence"),
  edge("inbound/6", `${loop}/fragment:retry-prepare#1/0`, "sequence"),
  edge("entry:preflight", "inbound/6", "preflight", "CORS preflight"),
  edge("inbound/6", "terminal:preflight-response", "preflight"),
  edge(`${loop}/fragment:retry-prepare#1/0`, `${loop}/fragment:retry-auth#1/0`, "sequence"),
  edge(`${loop}/fragment:retry-auth#1/0`, `${loop}/2`, "sequence"),
  edge(`${loop}/2`, `${loop}/test`, "sequence"),
  edge(`${loop}/2`, "entry:on-error", "raises-error"),
  edge(`${loop}/test`, `${loop}/fragment:retry-prepare#1/0`, "loop-back", "Retry while 429"),
  edge(`${loop}/test`, "outbound/0", "loop-exit", "Retry complete"),
  edge("outbound/0", "outbound/fragment:set-response-headers#1/0", "sequence"),
  edge("outbound/fragment:set-response-headers#1/0", "terminal:final-response", "sequence"),
  edge("outbound/0", `${lookup}/0`, "data-dependency", "affects subsequent requests"),
  edge("entry:on-error", "on-error/0/decision", "sequence"),
  edge("on-error/0/decision", "on-error/0/1", "branch", "1 · status 429", 1, condition("@(context.Response.StatusCode == 429)", "response status is 429")),
  edge("on-error/0/decision", "on-error/0/merge", "no-match", "No match"),
  edge("on-error/0/1", "on-error/0/merge", "merge"),
  edge("on-error/0/merge", "on-error/fragment:set-response-headers#2/0", "sequence"),
  edge("on-error/fragment:set-response-headers#2/0", "terminal:error-response", "sequence"),
  edge("stage:inbound", "entry:on-error", "stage-exception", "Unhandled inbound failure"),
  edge("stage:backend", "entry:on-error", "stage-exception", "Unhandled backend failure"),
  edge("stage:outbound", "entry:on-error", "stage-exception", "Unhandled outbound failure"),
];

export const syntheticModel: EffectivePolicyFlowModel = {
  schemaVersion: 2,
  scopeId: "synthetic",
  scopeKind: "Api",
  source: { text: source, lineCount: sourceLines.length },
  stages: [
    { id: "stage:inbound", name: "inbound", present: true },
    { id: "stage:backend", name: "backend", present: true },
    { id: "stage:outbound", name: "outbound", present: true },
    { id: "stage:on-error", name: "on-error", present: true },
  ],
  elements,
  edges,
  variables: [
    { name: "auth-type", writers: [{ elementId: `${auth}/0`, literal: false, dependencyClasses: ["runtime"] }], readers: [], dependencyClasses: ["runtime"] },
    { name: "bypassEnabled", writers: [], readers: ["inbound/1/decision"], dependencyClasses: ["configuration"] },
    { name: "responseOwner", writers: [{ elementId: `${lookup}/0`, literal: false, dependencyClasses: ["runtime"] }], readers: ["inbound/3", "outbound/0"], dependencyClasses: ["runtime"] },
    { name: "requestedModel", writers: [{ elementId: "inbound/3", literal: false, dependencyClasses: ["runtime"] }], readers: [], dependencyClasses: ["runtime"] },
  ],
  diagnostics: [
    { severity: "warning", code: "UNKNOWN_POLICY", message: "The mystery-policy element is shown as opaque.", span: { startLine: 11, startColumn: 5, endLine: 11, endColumn: 23 }, elementId: "inbound/5" },
  ],
};

export default syntheticModel;
