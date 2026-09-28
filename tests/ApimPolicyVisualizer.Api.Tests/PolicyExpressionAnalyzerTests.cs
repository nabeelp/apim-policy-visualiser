using System.Reflection;
using System.Reflection.Emit;
using ApimPolicyVisualizer.Api.Policy.Expressions;
using ApimPolicyVisualizer.Api.Policy.Model;
using Microsoft.CodeAnalysis.CSharp;

namespace ApimPolicyVisualizer.Api.Tests;

public class PolicyExpressionAnalyzerTests
{
    private static readonly PolicyExpressionAnalyzer Analyzer = new();

    private const string SevenBranchBlock = """
        @{
          try {
            if (context.Request.Method == "GET") { return "none"; }
            var fromQuery = context.Request.Url.Query.GetValueOrDefault("model", "");
            if (!string.IsNullOrEmpty(fromQuery)) { return fromQuery; }
            var path = context.Request.OriginalUrl.Path;
            if (path.Contains("/deployments/")) { return path.Split('/')[3]; }
            if (path.EndsWith("/models")) return "catalog";
            var fromHeader = context.Request.Headers.GetValueOrDefault("x-model", "");
            if (fromHeader != "") { return fromHeader; }
            var body = context.Request.Body?.As<JObject>(preserveContent: true);
            if (body != null && body["model"] != null) { return body["model"].ToString(); }
            return "";
          } catch { return ""; }
        }
        """;

    [Fact]
    public void VariableReads_AreExtracted_ForAllFourAccessForms()
    {
        var analysis = Analyzer.Analyze(
            "@((string)context.Variables[\"a\"] == context.Variables.GetValueOrDefault<string>(\"b\", \"\") " +
            "&& context.Variables.ContainsKey(\"c\") && context.Variables.TryGetValue(\"d\", out var d))");

        Assert.Equal(["a", "b", "c", "d"], analysis.VariablesRead);
    }

    [Fact]
    public void NamedValues_AndContextMemberPaths_AreExtracted()
    {
        var analysis = Analyzer.Analyze(
            "@(context.Request.OriginalUrl.Path.StartsWith(\"{{route-prefix}}\") && context.Response.StatusCode == 200 " +
            "&& context.Request.Headers.GetValueOrDefault(\"x-tenant\", \"\") != \"\" && context.Subscription.Id != {{blocked-id}} " +
            "&& context.Product.Name == \"p\" && context.Deployment.Region != null && context.User.Email != null)");

        Assert.Equal(["route-prefix", "blocked-id"], analysis.NamedValues);
        Assert.Equal(
            ["Request.OriginalUrl.Path", "Response.StatusCode", "Request.Headers[\"x-tenant\"]", "Subscription.Id", "Product.Name", "Deployment.Region", "User.Email"],
            analysis.ContextMembers);
        Assert.False(analysis.Opaque);
        Assert.Contains("{{blocked-id}}", analysis.Summary, StringComparison.Ordinal);
        Assert.Contains("path starts with {{route-prefix}}", analysis.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void SevenBranchBlock_YieldsSevenOrderedReturnPaths_AndLocalCatch()
    {
        var analysis = Analyzer.Analyze(SevenBranchBlock);

        Assert.False(analysis.Opaque);
        Assert.Equal(7, analysis.ReturnPaths.Count);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7], analysis.ReturnPaths.Select(p => p.Order));
        Assert.Equal(
            ["context.Request.Method == \"GET\"", "!string.IsNullOrEmpty(fromQuery)", "path.Contains(\"/deployments/\")", "path.EndsWith(\"/models\")", "fromHeader != \"\"", "body != null && body[\"model\"] != null", null],
            analysis.ReturnPaths.Select(p => p.Condition));
        Assert.Equal(["literal", "variable", "expression", "literal", "variable", "expression", "literal"], analysis.ReturnPaths.Select(p => p.ValueKind));
        Assert.Equal("\"none\"", analysis.ReturnPaths[0].ValueText);
        Assert.Equal("Request method = \"GET\"", analysis.ReturnPaths[0].ConditionSummary);
        Assert.Equal("fromQuery is not empty", analysis.ReturnPaths[1].ConditionSummary);
        Assert.True(analysis.HasLocalCatch);
        Assert.Equal("\"\"", analysis.CatchFallback);
        Assert.Contains("Request.Url.Query[\"model\"]", analysis.ContextMembers);
        Assert.Contains("Request.Headers[\"x-model\"]", analysis.ContextMembers);
    }

    [Fact]
    public void ElseIfChains_AreFlattenedInOrder()
    {
        var analysis = Analyzer.Analyze("@{ if (context.Request.Method == \"GET\") return 1; else if (context.Request.Method == \"POST\") return 2; else return 3; }");

        Assert.Equal(3, analysis.ReturnPaths.Count);
        Assert.Equal(["context.Request.Method == \"GET\"", "context.Request.Method == \"POST\"", null], analysis.ReturnPaths.Select(p => p.Condition));
        Assert.False(analysis.HasLocalCatch);
    }

    [Fact]
    public void RetryCondition_YieldsReadableSummary()
    {
        var analysis = Analyzer.Analyze(
            "@(context.Response.StatusCode == 429 || (context.Response.StatusCode >= 500 && !context.Response.StatusReason.Contains(\"Backend pool\")))");

        Assert.False(analysis.Opaque);
        Assert.Equal("Response status = 429 OR (status >= 500 AND reason does not contain \"Backend pool\")", analysis.Summary);
        Assert.Equal(["Response.StatusCode", "Response.StatusReason"], analysis.ContextMembers);
    }

    [Theory]
    [InlineData("@(context.Variables.GetValueOrDefault<string>(\"x\") == null)", "variable x is null")]
    [InlineData("@(context.Variables[\"x\"] != null)", "variable x is not null")]
    [InlineData("@(!context.Variables.ContainsKey(\"x\"))", "variable x is not set")]
    [InlineData("@(string.IsNullOrWhiteSpace((string)context.Variables[\"x\"]))", "variable x is empty")]
    [InlineData("@(context.Request.Headers.GetValueOrDefault(\"x-debug\", \"\").Equals(\"1\", StringComparison.OrdinalIgnoreCase))", "Request header x-debug equals \"1\" (ignore case)")]
    [InlineData("@(!(context.Request.Method == \"GET\"))", "NOT Request method = \"GET\"")]
    [InlineData("@(true)", "true")]
    public void SimpleConditions_AreSummarised(string expression, string expected)
    {
        Assert.Equal(expected, Analyzer.Analyze(expression).Summary);
    }

    [Theory]
    [InlineData("@(context.Request.Headers.Where(h => h.Key.StartsWith(\"x-\")).Select(h => h.Key).Any())")]
    [InlineData("@((from h in context.Request.Headers where h.Key == \"a\" select h).Count() > 0)")]
    public void LinqOrLambdaHeavyExpression_IsOpaque(string expression)
    {
        var analysis = Analyzer.Analyze(expression);

        Assert.True(analysis.Opaque);
        Assert.Null(analysis.Summary);
        Assert.Contains(analysis.Diagnostics, d => d.Contains("lambdas or query syntax", StringComparison.Ordinal));
    }

    [Fact]
    public void DangerousCall_IsAnalysedAsSyntaxOnly_AndNeverExecuted()
    {
        var path = Path.Combine(Path.GetTempPath(), $"spf-analyzer-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "keep");
        try
        {
            var analysis = Analyzer.Analyze($"@{{ System.IO.File.Delete(@\"{path}\"); return \"deleted\"; }}");

            Assert.True(File.Exists(path));
            Assert.Single(analysis.ReturnPaths);
            Assert.Equal("\"deleted\"", analysis.ReturnPaths[0].ValueText);

            var expression = Analyzer.Analyze($"@(System.IO.File.Exists(@\"{path}\") && System.Diagnostics.Process.Start(\"cmd\") != null)");
            Assert.True(expression.Opaque);
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("@(context.Request.Method == )")]
    [InlineData("@{ if (true { return 1; } }")]
    [InlineData("@(\"unterminated)")]
    public void SyntaxError_YieldsOpaqueResultAndDiagnostic_NotException(string expression)
    {
        var analysis = Analyzer.Analyze(expression);

        Assert.True(analysis.Opaque);
        Assert.Null(analysis.Summary);
        Assert.Empty(analysis.ReturnPaths);
        Assert.Contains(analysis.Diagnostics, d => d.StartsWith("C# syntax error", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("{\"model\": \"@(context.Variables[\\\"x\\\"])\"}")]
    [InlineData("prefix @(1) suffix")]
    public void EmbeddedTemplateText_IsOpaque(string text)
    {
        var analysis = Analyzer.Analyze(text);
        Assert.True(analysis.Opaque);
        Assert.Contains(analysis.Diagnostics, d => d.Contains("template", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("@(\"responses-owner-\" + context.Request.MatchedParameters.GetValueOrDefault(\"id\", \"\"))", "responses-owner-")]
    [InlineData("@(\"a-\" + \"b-\" + context.Subscription.Id)", "a-")]
    [InlineData("@($\"tenant-{context.Subscription.Id}\")", "tenant-")]
    [InlineData("@{ return \"block-\" + context.Subscription.Id; }", "block-")]
    [InlineData("@(context.Subscription.Id + \"-suffix\")", null)]
    public void KeyPrefix_IsTheLeadingStringLiteral(string expression, string? expected)
    {
        Assert.Equal(expected, Analyzer.Analyze(expression).KeyPrefix);
    }

    [Fact]
    public void NamedValuePlaceholders_KeepTheExpressionParseable()
    {
        var analysis = Analyzer.Analyze("@({{feature-flag}} == \"on\")");

        Assert.False(analysis.Opaque);
        Assert.Equal(["feature-flag"], analysis.NamedValues);
        Assert.Equal("{{feature-flag}} = \"on\"", analysis.Summary);
    }

    [Fact]
    public void ReturnAndCatch_AreReportedAsExpressionLocal()
    {
        // The analysis exposes return paths and catches as data only; it has no notion of pipeline termination.
        var analysis = Analyzer.Analyze("@{ try { return context.Request.Body.As<string>(); } catch (Exception) { return \"fallback\"; } }");

        Assert.True(analysis.HasLocalCatch);
        Assert.Equal("\"fallback\"", analysis.CatchFallback);
        Assert.Single(analysis.ReturnPaths);
        Assert.Equal("expression", analysis.ReturnPaths[0].ValueKind);
    }

    [Fact]
    public void ApiAssembly_ReferencesNoCompilationSemanticModelEmitScriptingOrReflectionInvoke()
    {
        var apiAssembly = typeof(PolicyExpressionAnalyzer).Assembly;

        Assert.DoesNotContain(apiAssembly.GetReferencedAssemblies(), a => a.Name!.Contains("Scripting", StringComparison.OrdinalIgnoreCase));

        var members = IlMemberScanner.ReferencedMembers(apiAssembly).ToList();
        var violations = members.Where(IlMemberScanner.IsForbidden).Select(IlMemberScanner.Describe).Distinct().ToList();
        Assert.True(violations.Count == 0, "Forbidden Roslyn/reflection APIs referenced: " + string.Join(", ", violations));

        // The scanner sees the analyzer's real Roslyn usage, so an empty violation list is meaningful.
        var analyzerMembers = typeof(PolicyExpressionAnalyzer).Assembly.GetTypes()
            .Where(t => t.FullName!.StartsWith(typeof(PolicyExpressionAnalyzer).FullName!, StringComparison.Ordinal))
            .SelectMany(IlMemberScanner.ReferencedMembers)
            .Select(IlMemberScanner.Describe)
            .ToHashSet();
        Assert.Contains("Microsoft.CodeAnalysis.CSharp.SyntaxFactory::ParseExpression", analyzerMembers);
        Assert.Contains("Microsoft.CodeAnalysis.CSharp.SyntaxFactory::ParseStatement", analyzerMembers);
    }

    [Fact]
    public void ForbiddenApiScanner_DetectsCompilationUsage()
    {
        var method = typeof(PolicyExpressionAnalyzerTests).GetMethod(nameof(ForbiddenSample), BindingFlags.NonPublic | BindingFlags.Static)!;
        var violations = IlMemberScanner.ReferencedMembers(method).Where(IlMemberScanner.IsForbidden).ToList();
        Assert.NotEmpty(violations);
    }

    // Never called: exists only so the scanner's positive control has a CSharpCompilation reference to find.
    private static object ForbiddenSample() => CSharpCompilation.Create("never-run");

    /// <summary>Minimal IL reader resolving metadata tokens of call/field/type operands.</summary>
    private static class IlMemberScanner
    {
        private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(o => o.Value);

        private static readonly string[] ForbiddenTypes =
        [
            "Microsoft.CodeAnalysis.Compilation",
            "Microsoft.CodeAnalysis.CSharp.CSharpCompilation",
            "Microsoft.CodeAnalysis.SemanticModel",
            "Microsoft.CodeAnalysis.CSharp.CSharpSemanticModel",
            "Microsoft.CodeAnalysis.CSharp.CSharpExtensions",
            "System.Activator",
        ];

        private static readonly string[] ForbiddenNamespaces =
        [
            "Microsoft.CodeAnalysis.Scripting", "Microsoft.CodeAnalysis.CSharp.Scripting", "Microsoft.CodeAnalysis.Emit", "System.Reflection.Emit",
        ];

        public static IEnumerable<MemberInfo> ReferencedMembers(Assembly assembly) =>
            assembly.GetTypes().SelectMany(ReferencedMembers);

        public static IEnumerable<MemberInfo> ReferencedMembers(Type type) =>
            type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                .Cast<MethodBase>()
                .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .SelectMany(ReferencedMembers);

        public static IEnumerable<MemberInfo> ReferencedMembers(MethodBase method)
        {
            var il = method.GetMethodBody()?.GetILAsByteArray();
            if (il is null)
            {
                return [];
            }

            var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
            var methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : null;
            var result = new List<MemberInfo>();
            var i = 0;
            while (i < il.Length)
            {
                OpCode opCode;
                if (il[i] == 0xFE)
                {
                    opCode = OpCodesByValue[unchecked((short)(0xFE00 | il[i + 1]))];
                    i += 2;
                }
                else
                {
                    opCode = OpCodesByValue[il[i]];
                    i += 1;
                }

                switch (opCode.OperandType)
                {
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineTok:
                    case OperandType.InlineType:
                        var token = BitConverter.ToInt32(il, i);
                        i += 4;
                        try
                        {
                            if (method.Module.ResolveMember(token, typeArguments, methodArguments) is { } member)
                            {
                                result.Add(member);
                            }
                        }
                        catch (ArgumentException)
                        {
                        }

                        break;
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        i += 1;
                        break;
                    case OperandType.InlineVar:
                        i += 2;
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        i += 8;
                        break;
                    case OperandType.InlineSwitch:
                        var count = BitConverter.ToInt32(il, i);
                        i += 4 + (4 * count);
                        break;
                    default:
                        i += 4;
                        break;
                }
            }

            return result;
        }

        public static bool IsForbidden(MemberInfo member)
        {
            var type = member as Type ?? member.DeclaringType;
            var name = type?.IsGenericType == true ? type.GetGenericTypeDefinition().FullName : type?.FullName;
            if (name is null)
            {
                return false;
            }

            if (ForbiddenTypes.Contains(name) || ForbiddenNamespaces.Any(ns => name.StartsWith(ns + ".", StringComparison.Ordinal)))
            {
                return true;
            }

            if (member.Name == "Emit" && name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
            {
                return true;
            }

            return member is MethodBase { Name: "Invoke" or "InvokeMember" or "CreateInstance" or "Load" or "LoadFrom" or "LoadFile" }
                   && name.StartsWith("System.Reflection.", StringComparison.Ordinal) || (name == "System.Type" && member.Name == "InvokeMember");
        }

        public static string Describe(MemberInfo member) =>
            member is Type type ? type.FullName ?? type.Name : $"{member.DeclaringType?.FullName}::{member.Name}";
    }
}
