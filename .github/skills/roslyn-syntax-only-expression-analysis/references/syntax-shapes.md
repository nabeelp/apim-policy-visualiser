# Expression Syntax Shapes

> Load when: implementing or changing a syntax walker fact extractor in `PolicyExpressionAnalyzer`.

| Fact | Syntax shape |
|------|--------------|
| Variable read | `context.Variables["x"]`, `.GetValueOrDefault<T>("x", …)`, `.ContainsKey("x")`, `.TryGetValue("x", out …)` with a string-literal argument |
| Named value | `{{name}}` in the raw text (APIM substitutes before C#) |
| Context member | Longest `context.A.B.C` member-access chain |
| Return paths | Top-level `if`/`else if`/`else` chains and `return` statements in the block, in order |
| Local catch | `TryStatementSyntax` with a catch that returns a literal |
| Key prefix | Leading string literal of a `+` concatenation or interpolated string |
