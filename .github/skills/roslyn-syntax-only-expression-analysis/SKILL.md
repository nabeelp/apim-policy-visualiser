---
name: roslyn-syntax-only-expression-analysis
description: "Analyse APIM @(...)/@{...} policy expressions with Roslyn syntax trees only to extract variable reads, return paths, catches and condition summaries, labelling results inferred and never compiling or executing code."
---

# Skill: Roslyn Syntax-Only Expression Analysis

Use this skill when working on `PolicyExpressionAnalyzer` (`SPF-3`) or any
code that derives facts from APIM policy expressions. The security boundary is
`VIS-SEC-03`: parsing is allowed, compiling, binding, evaluating or executing is
not. Every derived fact is labelled `inferred` and carries a source span.

---

## Process

### Step 1: Unwrap the APIM expression form

- `@( expr )` → parse the inner text with `SyntaxFactory.ParseExpression`.
- `@{ statements }` → parse `{ statements }` with `SyntaxFactory.ParseStatement`
  (a `BlockSyntax`).
- Text that merely contains `@(`/`@{` inside a larger literal (e.g. a
  `set-body` template) → treat as opaque.

Record the offset of the inner text so Roslyn line/character positions can be
mapped back to the XML span.

### Step 2: Reject on syntax diagnostics

If the parsed node contains an error diagnostic, return an **opaque** result
with the verbatim text and an analysis diagnostic. Never throw.

### Step 3: Walk the tree with a `CSharpSyntaxWalker`

Extract only what syntax proves:

Load `references/syntax-shapes.md` now for the exact syntax shape of each fact (variable reads, named values, context members, return paths, local catches, key prefixes). Extract a fact only when its shape matches exactly; otherwise record nothing for it.

### Step 4: Summarise simple conditions

Produce a readable summary only when the expression consists of comparisons,
`&&`/`||`/`!`, null checks, `Contains/StartsWith/EndsWith/Equals` on strings,
literals and the facts above. Otherwise leave `summary` null.

---

## Gotchas

- **Forbidden APIs:** `CSharpCompilation`, `SemanticModel`, `Compilation.Emit`,
  `Microsoft.CodeAnalysis.CSharp.Scripting`, reflection invoke. A test must
  scan the analyzer's IL/referenced members to enforce this.
- **ARM escaping first.** Analyse the text after `PolicySourceLoader` restores
  `&gt;`/`&quot;`; otherwise every expression is a syntax error.
- **`&#xD;&#xA;` line endings** mean Roslyn line numbers must be offset from
  the attribute's start line, not the element's.
- **A `return` is expression-local.** Report return paths as "value returned to
  this policy", never as pipeline termination.
- **Named values are not C#.** `{{x}}` is not valid syntax; replace each with a
  same-length identifier placeholder before parsing so spans stay aligned.
- **Don't guess.** Anything unmatched is opaque; never pattern-match on
  variable names or comment text to invent meaning.

---

## Validation

- [ ] `dotnet test ... --filter FullyQualifiedName~PolicyExpressionAnalyzerTests` selects and passes >0 tests.
- [ ] A seven-branch `if/return` block yields seven ordered return paths.
- [ ] A syntax error yields an opaque result and a diagnostic, not an exception.
- [ ] The forbidden-API test fails if `CSharpCompilation` is referenced.
