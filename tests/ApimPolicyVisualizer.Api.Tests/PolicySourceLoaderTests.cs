using ApimPolicyVisualizer.Api.Policy;
using ApimPolicyVisualizer.Api.Policy.Model;
using ApimPolicyVisualizer.Api.Policy.Source;

namespace ApimPolicyVisualizer.Api.Tests;

public class PolicySourceLoaderTests
{
    private static readonly string[] Lines =
    [
        "<policies>",
        "  <inbound>",
        "    <!-- Step 1: note -->",
        "    <set-header name=\"x-a\" exists-action=\"override\">",
        "      <value>v</value>",
        "    </set-header>",
        "    <base />",
        "    <choose>",
        "      <when condition=\"@(context.Request.Method == &amp;quot;GET&amp;quot; &amp;amp;&amp;amp; 1 &amp;gt; 0)\">",
        "        <!-- <include-fragment fragment-id=\"x\" /> -->",
        "        <set-variable name=\"literal\" value=\"a &amp;gt; b &amp; c\" />",
        "      </when>",
        "    </choose>",
        "  </inbound>",
        "</policies>",
    ];

    private static readonly PolicySourceLoader Loader = new();

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Spans_AreExact_ForNestedSelfClosingCommentsAndAttributes(string newline)
    {
        var xml = string.Join(newline, Lines);
        var document = Loader.Load(xml);

        Assert.Equal(xml, document.Text);
        Assert.Equal(Lines.Length, document.LineCount);

        var root = document.Root;
        Assert.Equal(new SourceSpan(1, 1, 15, 11), root.Span);

        var inbound = root.FirstElement("inbound")!;
        Assert.Equal(new SourceSpan(2, 3, 14, 12), inbound.Span);

        var comment = Assert.IsType<SourceComment>(inbound.Nodes[0]);
        Assert.Equal(" Step 1: note ", comment.Text);
        Assert.Equal(new SourceSpan(3, 5, 3, 25), comment.Span);
        Assert.True(comment.IsSingleLine);

        var setHeader = inbound.FirstElement("set-header")!;
        Assert.Equal(new SourceSpan(4, 5, 6, 17), setHeader.Span);
        var name = setHeader.GetAttribute("name")!;
        Assert.Equal(new SourceSpan(4, 17, 4, 26), name.Span);
        Assert.Equal(new SourceSpan(4, 23, 4, 25), name.ValueSpan);
        var existsAction = setHeader.GetAttribute("exists-action")!;
        Assert.Equal(new SourceSpan(4, 28, 4, 51), existsAction.Span);
        Assert.Equal(new SourceSpan(4, 43, 4, 50), existsAction.ValueSpan);

        var value = setHeader.FirstElement("value")!;
        Assert.Equal(new SourceSpan(5, 7, 5, 22), value.Span);
        var text = Assert.IsType<SourceText>(Assert.Single(value.Nodes));
        Assert.Equal("v", text.Value);
        Assert.Equal(new SourceSpan(5, 14, 5, 14), text.Span);

        var selfClosing = inbound.FirstElement("base")!;
        Assert.Equal(new SourceSpan(7, 5, 7, 12), selfClosing.Span);

        var when = inbound.FirstElement("choose")!.FirstElement("when")!;
        var condition = when.GetAttribute("condition")!;
        var line9 = Lines[8];
        Assert.Equal(new SourceSpan(9, line9.IndexOf("@(", StringComparison.Ordinal) + 1, 9, line9.LastIndexOf('"')), condition.ValueSpan);
        Assert.Equal(new SourceSpan(9, line9.IndexOf("condition", StringComparison.Ordinal) + 1, 9, line9.LastIndexOf('"') + 1), condition.Span);
        Assert.Equal(new SourceSpan(9, 7, 12, 13), when.Span);
        Assert.True(condition.IsExpression);
    }

    [Fact]
    public void SourceSlices_ByElementSpan_MatchTheMarkup()
    {
        var xml = string.Join("\n", Lines);
        var document = Loader.Load(xml);
        var lines = xml.Split('\n');

        foreach (var element in document.Root.DescendantsAndSelf())
        {
            var start = lines[element.Span.StartLine - 1][(element.Span.StartColumn - 1)..];
            Assert.StartsWith("<" + element.Name, start, StringComparison.Ordinal);
            var endLine = lines[element.Span.EndLine - 1];
            Assert.Equal('>', endLine[element.Span.EndColumn - 1]);
        }
    }

    [Fact]
    public void Comments_AreRetained_AndTagShapedCommentText_CreatesNoElement()
    {
        var document = Loader.Load(string.Join("\n", Lines));
        var when = document.Root.FirstElement("inbound")!.FirstElement("choose")!.FirstElement("when")!;

        var comment = Assert.Single(when.Nodes.OfType<SourceComment>());
        Assert.Contains("<include-fragment fragment-id=\"x\" />", comment.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(document.Root.DescendantsAndSelf(), e => e.Name == "include-fragment");
        Assert.Equal(["set-variable"], when.Elements.Select(e => e.Name));
        Assert.Equal(0, comment.IndexInParent);
        Assert.Same(when, comment.Parent);
    }

    [Fact]
    public void ArmDoubleEscaping_IsRestored_OnlyInExpressionValues()
    {
        var document = Loader.Load(string.Join("\n", Lines));
        var when = document.Root.FirstElement("inbound")!.FirstElement("choose")!.FirstElement("when")!;

        Assert.Equal("@(context.Request.Method == \"GET\" && 1 > 0)", when.AttributeValue("condition"));

        // A non-expression value keeps its (single) XML decoding: &amp;gt; stays "&gt;", &amp; becomes "&".
        var literal = when.FirstElement("set-variable")!.GetAttribute("value")!;
        Assert.False(literal.IsExpression);
        Assert.Equal("a &gt; b & c", literal.Value);
    }

    [Fact]
    public void MultiLineExpression_EncodedWithCharacterReferences_KeepsLineBreaks_AndStaysOnOneSourceLine()
    {
        const string xml = "<policies><inbound><set-variable name=\"x\" value=\"@{&#xD;&#xA;  return &amp;quot;a&amp;quot;;&#xD;&#xA;}\" /></inbound></policies>";
        var document = Loader.Load(xml);
        var attribute = document.Root.FirstElement("inbound")!.FirstElement("set-variable")!.GetAttribute("value")!;

        Assert.Equal("@{\r\n  return \"a\";\r\n}", attribute.Value);
        Assert.Equal(1, attribute.ValueSpan.StartLine);
        Assert.Equal(1, attribute.ValueSpan.EndLine);
        Assert.Equal(1, document.LineCount);
    }

    [Fact]
    public void ExpressionText_InElementContent_IsRestored_AndSpanned()
    {
        const string xml = "<policies>\n<inbound>\n<set-body>\n  @(context.Request.Method == &amp;quot;GET&amp;quot;)\n</set-body>\n</inbound>\n</policies>";
        var document = Loader.Load(xml);
        var text = Assert.IsType<SourceText>(Assert.Single(document.Root.FirstElement("inbound")!.FirstElement("set-body")!.Nodes));

        Assert.True(text.IsExpression);
        Assert.Contains("@(context.Request.Method == \"GET\")", text.Value, StringComparison.Ordinal);
        Assert.Equal(new SourceSpan(4, 3, 4, 54), text.Span);
    }

    [Fact]
    public void SyntheticFixture_LoadsWithCommentsAndExpressions()
    {
        var document = Loader.Load(PolicyModelTestHelpers.FixtureXml());

        Assert.Equal(["inbound", "backend", "outbound", "on-error"], document.Root.Elements.Select(e => e.Name));
        Assert.Contains(document.Root.DescendantsAndSelf().SelectMany(e => e.Nodes).OfType<SourceComment>(), c => c.Text.Contains("include-fragment: Begin", StringComparison.Ordinal));
        var model = document.Root.DescendantsAndSelf().Single(e => e.AttributeValue("name") == "requestedModel");
        Assert.Contains("context.Request.Method == \"GET\"", model.AttributeValue("value"), StringComparison.Ordinal);
        Assert.Contains("\r\n", model.AttributeValue("value"), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "empty")]
    [InlineData("   \n ", "empty")]
    [InlineData("<!DOCTYPE policies [<!ENTITY boom \"x\">]><policies><inbound>&boom;</inbound></policies>", "DTD")]
    [InlineData("<policy><inbound /></policy>", "Expected a <policies> root element but found <policy>")]
    [InlineData("<policies><inbound></policies>", "not well-formed")]
    [InlineData("<policies><inbound>", "not well-formed")]
    public void InvalidInput_RaisesPolicyParseException_WithDescriptiveMessage(string xml, string expected)
    {
        var ex = Assert.Throws<PolicyParseException>(() => Loader.Load(xml));
        Assert.Contains(expected, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MalformedXml_Message_IncludesLineAndPosition()
    {
        var ex = Assert.Throws<PolicyParseException>(() => Loader.Load("<policies>\n  <inbound>\n</policies>"));
        Assert.Contains("Line 3", ex.Message, StringComparison.Ordinal);
        Assert.Contains("position", ex.Message, StringComparison.Ordinal);
    }
}
