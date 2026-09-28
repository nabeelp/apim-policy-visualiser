using System.Text;
using System.Text.RegularExpressions;
using ApimPolicyVisualizer.Api.Policy.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ApimPolicyVisualizer.Api.Policy.Expressions;

public interface IPolicyExpressionAnalyzer
{
    /// <summary>Analyses one expression-bearing value (e.g. <c>@(...)</c> or <c>@{...}</c>) using syntax only.</summary>
    ExpressionAnalysis Analyze(string text);
}

/// <summary>
/// Roslyn <b>syntax-only</b> analysis of APIM policy expressions (SPF-FR-10). The text is parsed with
/// <see cref="SyntaxFactory.ParseExpression(string, int, ParseOptions?, bool)"/> /
/// <see cref="SyntaxFactory.ParseStatement(string, int, ParseOptions?, bool)"/> and walked; it is never compiled,
/// bound, emitted, scripted or executed (VIS-SEC-03). Every derived fact is <see cref="FlowProvenance.Inferred"/>;
/// anything outside the supported shapes stays opaque. A C# <c>return</c> or locally caught exception is
/// expression-local and never a pipeline termination or On-error transfer.
/// </summary>
public sealed partial class PolicyExpressionAnalyzer : IPolicyExpressionAnalyzer
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular);

    private static readonly HashSet<string> VariableReadMethods = new(StringComparer.Ordinal) { "GetValueOrDefault", "ContainsKey", "TryGetValue" };

    [GeneratedRegex(@"\{\{\s*(?<name>[^{}\s]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex NamedValuePattern();

    public ExpressionAnalysis Analyze(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            return AnalyzeCore(text);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return ExpressionAnalysis.OpaqueResult($"Expression could not be analysed ({ex.GetType().Name}); shown verbatim.");
        }
    }

    private static ExpressionAnalysis AnalyzeCore(string text)
    {
        var namedValues = new List<string>();
        foreach (Match match in NamedValuePattern().Matches(text))
        {
            AddDistinct(namedValues, match.Groups["name"].Value);
        }

        var trimmed = text.Trim();
        bool isBlock;
        string inner;
        if (trimmed.StartsWith("@(", StringComparison.Ordinal) && trimmed.EndsWith(')'))
        {
            isBlock = false;
            inner = trimmed[2..^1];
        }
        else if (trimmed.StartsWith("@{", StringComparison.Ordinal) && trimmed.EndsWith('}'))
        {
            isBlock = true;
            inner = trimmed[2..^1];
        }
        else
        {
            return Opaque(namedValues, "Expression is embedded in literal text (template); shown verbatim, not analysed.");
        }

        // {{named-value}} is substituted by APIM before C# parsing and is not C#; use same-length identifiers.
        var placeholders = new Dictionary<string, string>(StringComparer.Ordinal);
        var failedPlaceholder = false;
        var counter = 0;
        inner = NamedValuePattern().Replace(inner, match =>
        {
            var placeholder = "_n" + counter.ToString(System.Globalization.CultureInfo.InvariantCulture);
            counter++;
            if (placeholder.Length > match.Length)
            {
                failedPlaceholder = true;
                return match.Value;
            }

            placeholder = placeholder.PadRight(match.Length, '_');
            placeholders[placeholder] = "{{" + match.Groups["name"].Value + "}}";
            return placeholder;
        });

        if (failedPlaceholder)
        {
            return Opaque(namedValues, "Too many named values to analyse; shown verbatim.");
        }

        SyntaxNode root;
        if (isBlock)
        {
            root = SyntaxFactory.ParseStatement("{" + inner + "}", 0, ParseOptions, consumeFullText: true);
        }
        else
        {
            root = SyntaxFactory.ParseExpression(inner, 0, ParseOptions, consumeFullText: true);
        }

        var error = root.GetDiagnostics().FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
        if (error is not null || (isBlock && root is not BlockSyntax))
        {
            var message = error is null ? "not a statement block" : $"{error.Id} {error.GetMessage(System.Globalization.CultureInfo.InvariantCulture)}";
            return Opaque(namedValues, $"C# syntax error: {message}. Shown verbatim.");
        }

        var context = new AnalysisContext(placeholders);
        var walker = new FactWalker(context);
        walker.Visit(root);

        var returnPaths = new List<ReturnPath>();
        string? summary = null;
        string? keyPrefix;
        if (root is BlockSyntax block)
        {
            CollectReturnPaths(block, context, returnPaths);
            keyPrefix = SingleReturn(block) is { } single ? KeyPrefixOf(single, context) : null;
        }
        else
        {
            var expression = (ExpressionSyntax)root;
            summary = new Summarizer(context).Summarize(expression);
            keyPrefix = KeyPrefixOf(expression, context);
        }

        var (hasCatch, fallback) = FindLocalCatch(root, context);
        var diagnostics = new List<string>();
        var opaque = summary is null && returnPaths.Count == 0;
        if (opaque)
        {
            diagnostics.Add(walker.HasLambdaOrQuery
                ? "Expression uses lambdas or query syntax; no summary inferred. Shown verbatim."
                : "Expression uses constructs outside the summarised subset; no summary inferred. Shown verbatim.");
        }

        return new ExpressionAnalysis(
            opaque,
            summary,
            walker.VariablesRead,
            namedValues,
            walker.ContextMembers,
            returnPaths,
            hasCatch,
            fallback,
            keyPrefix,
            diagnostics);
    }

    private static ExpressionAnalysis Opaque(IReadOnlyList<string> namedValues, string diagnostic) =>
        new(true, null, [], namedValues, [], [], false, null, null, [diagnostic]);

    private static void CollectReturnPaths(BlockSyntax block, AnalysisContext context, List<ReturnPath> paths)
    {
        foreach (var statement in TopLevelStatements(block))
        {
            switch (statement)
            {
                case IfStatementSyntax ifStatement:
                {
                    var current = ifStatement;
                    while (current is not null)
                    {
                        if (DirectReturn(current.Statement) is { } conditional)
                        {
                            var condition = context.Restore(current.Condition.ToString());
                            var conditionSummary = new Summarizer(context).Summarize(current.Condition);
                            paths.Add(CreatePath(paths.Count + 1, condition, conditionSummary, conditional, context));
                        }

                        var next = current.Else?.Statement;
                        if (next is IfStatementSyntax elseIf)
                        {
                            current = elseIf;
                            continue;
                        }

                        if (next is not null && DirectReturn(next) is { } otherwise)
                        {
                            paths.Add(CreatePath(paths.Count + 1, null, null, otherwise, context));
                        }

                        current = null;
                    }

                    break;
                }

                case ReturnStatementSyntax returnStatement:
                    paths.Add(CreatePath(paths.Count + 1, null, null, returnStatement, context));
                    return; // Later top-level statements are not reached.
            }
        }
    }

    /// <summary>The block's statements, descending into a top-level <c>try</c> block (a common APIM pattern).</summary>
    private static IEnumerable<StatementSyntax> TopLevelStatements(BlockSyntax block)
    {
        foreach (var statement in block.Statements)
        {
            if (statement is TryStatementSyntax tryStatement)
            {
                foreach (var inner in tryStatement.Block.Statements)
                {
                    yield return inner;
                }
            }
            else
            {
                yield return statement;
            }
        }
    }

    private static ReturnStatementSyntax? DirectReturn(StatementSyntax statement) => statement switch
    {
        ReturnStatementSyntax r => r,
        BlockSyntax b => b.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault(),
        _ => null,
    };

    private static ExpressionSyntax? SingleReturn(BlockSyntax block) =>
        block.Statements is [ReturnStatementSyntax { Expression: { } expression }] ? expression : null;

    private static ReturnPath CreatePath(int order, string? condition, string? summary, ReturnStatementSyntax statement, AnalysisContext context)
    {
        var value = statement.Expression;
        if (value is null)
        {
            return new ReturnPath(order, condition, summary, "expression", "(no value)");
        }

        var unwrapped = Unwrap(value);
        var kind = unwrapped switch
        {
            LiteralExpressionSyntax => "literal",
            IdentifierNameSyntax => "variable",
            _ when VariableNameOf(unwrapped) is not null => "variable",
            _ => "expression",
        };
        return new ReturnPath(order, condition, summary, kind, context.Restore(value.ToString()));
    }

    private static (bool HasCatch, string? Fallback) FindLocalCatch(SyntaxNode root, AnalysisContext context)
    {
        foreach (var tryStatement in root.DescendantNodesAndSelf().OfType<TryStatementSyntax>())
        {
            if (tryStatement.Catches.Count == 0)
            {
                continue;
            }

            foreach (var catchClause in tryStatement.Catches)
            {
                var fallback = catchClause.Block.Statements.OfType<ReturnStatementSyntax>().FirstOrDefault()?.Expression;
                if (fallback is not null && Unwrap(fallback) is LiteralExpressionSyntax literal)
                {
                    return (true, context.Restore(literal.ToString()));
                }
            }

            return (true, null);
        }

        return (false, null);
    }

    private static string? KeyPrefixOf(ExpressionSyntax expression, AnalysisContext context)
    {
        var current = Unwrap(expression);
        while (current is BinaryExpressionSyntax binary && binary.IsKind(SyntaxKind.AddExpression))
        {
            current = Unwrap(binary.Left);
        }

        var prefix = current switch
        {
            LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.StringLiteralExpression) => literal.Token.ValueText,
            InterpolatedStringExpressionSyntax interpolated when interpolated.Contents.FirstOrDefault() is InterpolatedStringTextSyntax text =>
                text.TextToken.ValueText,
            _ => null,
        };

        return string.IsNullOrEmpty(prefix) ? null : context.Restore(prefix);
    }

    internal static ExpressionSyntax Unwrap(ExpressionSyntax expression)
    {
        while (true)
        {
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                    expression = parenthesized.Expression;
                    continue;
                case CastExpressionSyntax cast:
                    expression = cast.Expression;
                    continue;
                default:
                    return expression;
            }
        }
    }

    private static bool IsContext(ExpressionSyntax expression) =>
        expression is IdentifierNameSyntax { Identifier.ValueText: "context" };

    private static bool IsContextVariables(ExpressionSyntax expression) =>
        Unwrap(expression) is MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Variables" } member && IsContext(member.Expression);

    private static string? StringLiteral(ArgumentSyntax? argument) =>
        argument?.Expression is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression)
            ? literal.Token.ValueText
            : null;

    /// <summary>The context variable name when <paramref name="expression"/> reads <c>context.Variables</c> by a literal key.</summary>
    internal static string? VariableNameOf(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case ElementAccessExpressionSyntax elementAccess
                when IsContextVariables(elementAccess.Expression) && elementAccess.ArgumentList.Arguments.Count == 1:
                return StringLiteral(elementAccess.ArgumentList.Arguments[0]);
            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation
                when VariableReadMethods.Contains(member.Name.Identifier.ValueText) && IsContextVariables(member.Expression)
                     && invocation.ArgumentList.Arguments.Count > 0:
                return StringLiteral(invocation.ArgumentList.Arguments[0]);
            default:
                return null;
        }
    }

    /// <summary>
    /// A <c>context.A.B["key"]</c> chain as segments, or null when the expression is not rooted at <c>context</c>.
    /// Literal-keyed indexers and <c>GetValueOrDefault/ContainsKey/TryGetValue("key")</c> calls attach the key.
    /// </summary>
    internal static List<(string Name, string? Key)>? ChainOf(ExpressionSyntax expression)
    {
        switch (expression)
        {
            case IdentifierNameSyntax when IsContext(expression):
                return [];
            case MemberAccessExpressionSyntax member when member.IsKind(SyntaxKind.SimpleMemberAccessExpression):
            {
                var chain = ChainOf(member.Expression);
                chain?.Add((member.Name.Identifier.ValueText, null));
                return chain;
            }

            case ElementAccessExpressionSyntax elementAccess when elementAccess.ArgumentList.Arguments.Count == 1
                                                                 && StringLiteral(elementAccess.ArgumentList.Arguments[0]) is { } key:
            {
                var chain = ChainOf(elementAccess.Expression);
                return WithKey(chain, key);
            }

            case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation
                when VariableReadMethods.Contains(member.Name.Identifier.ValueText)
                     && invocation.ArgumentList.Arguments.Count > 0
                     && StringLiteral(invocation.ArgumentList.Arguments[0]) is { } key:
            {
                var chain = ChainOf(member.Expression);
                return WithKey(chain, key);
            }

            case ParenthesizedExpressionSyntax parenthesized:
                return ChainOf(parenthesized.Expression);
            default:
                return null;
        }

        static List<(string, string?)>? WithKey(List<(string Name, string? Key)>? chain, string key)
        {
            if (chain is null || chain.Count == 0 || chain[^1].Key is not null)
            {
                return null;
            }

            chain[^1] = (chain[^1].Name, key);
            return chain;
        }
    }

    internal static string FormatChain(IReadOnlyList<(string Name, string? Key)> chain)
    {
        var builder = new StringBuilder();
        foreach (var (name, key) in chain)
        {
            if (builder.Length > 0)
            {
                builder.Append('.');
            }

            builder.Append(name);
            if (key is not null)
            {
                builder.Append("[\"").Append(key).Append("\"]");
            }
        }

        return builder.ToString();
    }

    private static void AddDistinct(List<string> list, string value)
    {
        if (!list.Contains(value, StringComparer.Ordinal))
        {
            list.Add(value);
        }
    }

    private sealed class AnalysisContext(Dictionary<string, string> placeholders)
    {
        public string Restore(string text)
        {
            if (placeholders.Count == 0)
            {
                return text;
            }

            foreach (var (placeholder, original) in placeholders)
            {
                text = text.Replace(placeholder, original, StringComparison.Ordinal);
            }

            return text;
        }

        public string? NamedValue(string identifier) => placeholders.GetValueOrDefault(identifier);
    }

    /// <summary>Collects variable reads and context member paths; purely structural.</summary>
    private sealed class FactWalker(AnalysisContext context) : CSharpSyntaxWalker
    {
        public List<string> VariablesRead { get; } = [];

        public List<string> ContextMembers { get; } = [];

        public bool HasLambdaOrQuery { get; private set; }

        public override void VisitElementAccessExpression(ElementAccessExpressionSyntax node)
        {
            if (VariableNameOf(node) is { } name)
            {
                AddDistinct(VariablesRead, context.Restore(name));
            }

            base.VisitElementAccessExpression(node);
        }

        public override void VisitInvocationExpression(InvocationExpressionSyntax node)
        {
            if (VariableNameOf(node) is { } name)
            {
                AddDistinct(VariablesRead, context.Restore(name));
            }

            base.VisitInvocationExpression(node);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            if (IsContext(node.Expression) && node.Name.Identifier.ValueText != "Variables")
            {
                // Climb to the top of the context.A.B["k"] chain and record it once.
                ExpressionSyntax current = node;
                while (true)
                {
                    var parent = current.Parent;
                    if (parent is MemberAccessExpressionSyntax parentMember && parentMember.Expression == current)
                    {
                        if (parentMember.Parent is InvocationExpressionSyntax invocation && invocation.Expression == parentMember)
                        {
                            if (ChainOf(invocation) is not null)
                            {
                                current = invocation;
                                continue;
                            }

                            break;
                        }

                        current = parentMember;
                        continue;
                    }

                    if (parent is ElementAccessExpressionSyntax parentElement && parentElement.Expression == current && ChainOf(parentElement) is not null)
                    {
                        current = parentElement;
                        continue;
                    }

                    break;
                }

                if (ChainOf(current) is { Count: > 0 } chain)
                {
                    AddDistinct(ContextMembers, context.Restore(FormatChain(chain)));
                }
            }

            base.VisitMemberAccessExpression(node);
        }

        public override void VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
        {
            HasLambdaOrQuery = true;
            base.VisitSimpleLambdaExpression(node);
        }

        public override void VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
        {
            HasLambdaOrQuery = true;
            base.VisitParenthesizedLambdaExpression(node);
        }

        public override void VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node)
        {
            HasLambdaOrQuery = true;
            base.VisitAnonymousMethodExpression(node);
        }

        public override void VisitQueryExpression(QueryExpressionSyntax node)
        {
            HasLambdaOrQuery = true;
            base.VisitQueryExpression(node);
        }
    }

    /// <summary>
    /// Renders a readable summary for conditions built only from comparisons, logical operators, null checks, string
    /// Contains/StartsWith/EndsWith/Equals calls, literals and recognised reads; returns null for anything else.
    /// </summary>
    private sealed class Summarizer(AnalysisContext context)
    {
        private readonly HashSet<string> _mentionedRoots = new(StringComparer.Ordinal);

        public string? Summarize(ExpressionSyntax expression)
        {
            try
            {
                return Render(expression, negated: false);
            }
            catch (InsufficientExecutionStackException)
            {
                return null;
            }
        }

        private string? Render(ExpressionSyntax expression, bool negated)
        {
            System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
            switch (expression)
            {
                case ParenthesizedExpressionSyntax parenthesized:
                {
                    var inner = Render(parenthesized.Expression, negated);
                    return inner is null ? null : IsLogical(parenthesized.Expression) ? $"({inner})" : inner;
                }

                case PrefixUnaryExpressionSyntax prefix when prefix.IsKind(SyntaxKind.LogicalNotExpression):
                {
                    var operand = prefix.Operand;
                    if (Unwrap(operand) is InvocationExpressionSyntax invocation && RenderCall(invocation, negated: !negated) is { } call)
                    {
                        return call;
                    }

                    var inner = Render(operand, negated: false);
                    return inner is null ? null : $"NOT {inner}";
                }

                case BinaryExpressionSyntax binary:
                    return RenderBinary(binary);

                case InvocationExpressionSyntax invocation:
                    return RenderCall(invocation, negated) ?? (negated ? null : Operand(invocation));

                case LiteralExpressionSyntax literal:
                    return RenderLiteral(literal);

                case CastExpressionSyntax cast:
                    return Render(cast.Expression, negated);

                default:
                    return negated ? null : Operand(expression);
            }
        }

        private string? RenderBinary(BinaryExpressionSyntax binary)
        {
            var op = binary.Kind() switch
            {
                SyntaxKind.LogicalAndExpression => "AND",
                SyntaxKind.LogicalOrExpression => "OR",
                SyntaxKind.EqualsExpression => "=",
                SyntaxKind.NotEqualsExpression => "!=",
                SyntaxKind.LessThanExpression => "<",
                SyntaxKind.LessThanOrEqualExpression => "<=",
                SyntaxKind.GreaterThanExpression => ">",
                SyntaxKind.GreaterThanOrEqualExpression => ">=",
                _ => null,
            };

            if (op is null)
            {
                return null;
            }

            if (op is "=" or "!=")
            {
                var leftIsNull = Unwrap(binary.Left).IsKind(SyntaxKind.NullLiteralExpression);
                var rightIsNull = Unwrap(binary.Right).IsKind(SyntaxKind.NullLiteralExpression);
                if (leftIsNull != rightIsNull)
                {
                    var subject = Render(leftIsNull ? binary.Right : binary.Left, negated: false);
                    return subject is null ? null : op == "=" ? $"{subject} is null" : $"{subject} is not null";
                }
            }

            var left = Render(binary.Left, negated: false);
            if (left is null)
            {
                return null;
            }

            var right = Render(binary.Right, negated: false);
            return right is null ? null : $"{left} {op} {right}";
        }

        private string? RenderCall(InvocationExpressionSyntax invocation, bool negated)
        {
            if (invocation.Expression is not MemberAccessExpressionSyntax member)
            {
                return null;
            }

            var method = member.Name.Identifier.ValueText;
            var arguments = invocation.ArgumentList.Arguments;

            // context.Variables.ContainsKey("x")
            if (method == "ContainsKey" && IsContextVariables(member.Expression) && StringLiteral(arguments.FirstOrDefault()) is { } variable)
            {
                return $"variable {context.Restore(variable)} {(negated ? "is not set" : "is set")}";
            }

            var isStringType = member.Expression is PredefinedTypeSyntax { Keyword.ValueText: "string" }
                               || member.Expression is IdentifierNameSyntax { Identifier.ValueText: "String" };

            if (isStringType && method is "IsNullOrEmpty" or "IsNullOrWhiteSpace" && arguments.Count == 1)
            {
                var subject = Render(arguments[0].Expression, negated: false);
                return subject is null ? null : $"{subject} {(negated ? "is not empty" : "is empty")}";
            }

            if (isStringType && method == "Equals" && arguments.Count is 2 or 3)
            {
                var suffix = arguments.Count == 3 ? ComparisonSuffix(arguments[2].Expression) : string.Empty;
                var a = suffix is null ? null : Render(arguments[0].Expression, negated: false);
                var b = a is null ? null : Render(arguments[1].Expression, negated: false);
                return b is null ? null : $"{a} {(negated ? "does not equal" : "equals")} {b}{suffix}";
            }

            var verb = method switch
            {
                "Contains" => negated ? "does not contain" : "contains",
                "StartsWith" => negated ? "does not start with" : "starts with",
                "EndsWith" => negated ? "does not end with" : "ends with",
                "Equals" => negated ? "does not equal" : "equals",
                _ => null,
            };

            if (verb is null || isStringType || arguments.Count is < 1 or > 2)
            {
                return null;
            }

            var comparison = arguments.Count == 2 ? ComparisonSuffix(arguments[1].Expression) : string.Empty;
            if (comparison is null)
            {
                return null;
            }

            var target = Render(member.Expression, negated: false);
            var argument = target is null ? null : Render(arguments[0].Expression, negated: false);
            return argument is null ? null : $"{target} {verb} {argument}{comparison}";
        }

        private static string? ComparisonSuffix(ExpressionSyntax expression) =>
            expression is MemberAccessExpressionSyntax { Expression: IdentifierNameSyntax { Identifier.ValueText: "StringComparison" } } comparison
                ? comparison.Name.Identifier.ValueText.Contains("IgnoreCase", StringComparison.Ordinal) ? " (ignore case)" : string.Empty
                : null;

        private string? RenderLiteral(LiteralExpressionSyntax literal) => literal.Kind() switch
        {
            SyntaxKind.StringLiteralExpression => context.NamedValue(literal.Token.ValueText) ?? $"\"{context.Restore(literal.Token.ValueText)}\"",
            SyntaxKind.NumericLiteralExpression => literal.Token.Text,
            SyntaxKind.TrueLiteralExpression => "true",
            SyntaxKind.FalseLiteralExpression => "false",
            SyntaxKind.NullLiteralExpression => "null",
            SyntaxKind.CharacterLiteralExpression => $"'{literal.Token.ValueText}'",
            _ => null,
        };

        private string? Operand(ExpressionSyntax expression)
        {
            expression = Unwrap(expression);
            if (VariableNameOf(expression) is { } variable)
            {
                return $"variable {context.Restore(variable)}";
            }

            if (expression is IdentifierNameSyntax identifier)
            {
                return context.NamedValue(identifier.Identifier.ValueText) ?? identifier.Identifier.ValueText;
            }

            if (ChainOf(expression) is not { Count: > 0 } chain)
            {
                return null;
            }

            var root = chain[0].Name;
            var member = DescribeMember(root, chain.Skip(1).ToList());
            var first = _mentionedRoots.Add(root);
            if (member.Length == 0)
            {
                return root;
            }

            return first ? $"{root} {member}" : member;
        }

        private static string DescribeMember(string root, List<(string Name, string? Key)> rest)
        {
            if (rest.Count == 0)
            {
                return string.Empty;
            }

            var names = string.Join('.', rest.Select(r => r.Name));
            var key = rest[^1].Key;
            var known = (root, names) switch
            {
                (_, "StatusCode") => "status",
                (_, "StatusReason") => "reason",
                (_, "Method") => "method",
                (_, "OriginalUrl.Path" or "Url.Path") => "path",
                (_, "OriginalUrl" or "Url") => "URL",
                (_, "OriginalUrl.Host" or "Url.Host") => "host",
                (_, "IpAddress") => "IP address",
                (_, "Body") => "body",
                (_, "Headers") when key is not null => $"header {key}",
                (_, "OriginalUrl.Query" or "Url.Query") when key is not null => $"query parameter {key}",
                (_, "MatchedParameters") when key is not null => $"parameter {key}",
                _ => null,
            };

            if (known is not null)
            {
                return known;
            }

            var words = string.Join(' ', rest.Select(r => SplitPascal(r.Name)));
            return key is null ? words : $"{words} {key}";
        }

        private static string SplitPascal(string name)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(char.ToLowerInvariant(name[i]));
            }

            return builder.ToString();
        }

        private static bool IsLogical(ExpressionSyntax expression) =>
            expression.IsKind(SyntaxKind.LogicalAndExpression) || expression.IsKind(SyntaxKind.LogicalOrExpression);
    }
}
