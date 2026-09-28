using System.Xml;
using ApimPolicyVisualizer.Api.Policy.Model;

namespace ApimPolicyVisualizer.Api.Policy.Source;

/// <summary>A node of the source-mapped policy tree. Spans are 1-based and inclusive.</summary>
public abstract class SourceNode
{
    public SourceSpan Span { get; internal set; } = new(1, 1, 1, 1);

    public SourceElement? Parent { get; internal set; }

    /// <summary>Zero-based index within <see cref="SourceElement.Nodes"/> of <see cref="Parent"/>.</summary>
    public int IndexInParent { get; internal set; }
}

public sealed class SourceElement : SourceNode
{
    private readonly List<SourceNode> _nodes = [];
    private readonly List<SourceAttribute> _attributes = [];

    public required string Name { get; init; }

    public IReadOnlyList<SourceAttribute> Attributes => _attributes;

    /// <summary>Child elements, comments and non-whitespace text, in document order.</summary>
    public IReadOnlyList<SourceNode> Nodes => _nodes;

    public IEnumerable<SourceElement> Elements => _nodes.OfType<SourceElement>();

    /// <summary>Concatenated direct text/CDATA content (decoded), or null when there is none.</summary>
    public string? Text
    {
        get
        {
            string? result = null;
            foreach (var node in _nodes)
            {
                if (node is SourceText text)
                {
                    result = result is null ? text.Value : result + text.Value;
                }
            }

            return result;
        }
    }

    public SourceAttribute? GetAttribute(string name)
    {
        foreach (var attribute in _attributes)
        {
            if (string.Equals(attribute.Name, name, StringComparison.Ordinal))
            {
                return attribute;
            }
        }

        return null;
    }

    public string? AttributeValue(string name) => GetAttribute(name)?.Value;

    public SourceElement? FirstElement(string name)
    {
        foreach (var node in _nodes)
        {
            if (node is SourceElement element && string.Equals(element.Name, name, StringComparison.Ordinal))
            {
                return element;
            }
        }

        return null;
    }

    public IEnumerable<SourceElement> DescendantsAndSelf()
    {
        var stack = new Stack<SourceElement>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            yield return current;
            for (var i = current._nodes.Count - 1; i >= 0; i--)
            {
                if (current._nodes[i] is SourceElement child)
                {
                    stack.Push(child);
                }
            }
        }
    }

    internal void AddNode(SourceNode node)
    {
        node.Parent = this;
        node.IndexInParent = _nodes.Count;
        _nodes.Add(node);
    }

    internal void AddAttribute(SourceAttribute attribute) => _attributes.Add(attribute);
}

/// <param name="Name">Attribute local name.</param>
/// <param name="Value">XML-decoded value; for expression-bearing values ARM's extra escaping layer is removed too.</param>
/// <param name="IsExpression">True when the value contains <c>@(</c> or <c>@{</c>.</param>
/// <param name="Span">From the first character of the name through the closing quote.</param>
/// <param name="ValueSpan">The characters between the quotes (both quotes when the value is empty).</param>
public sealed record SourceAttribute(string Name, string Value, bool IsExpression, SourceSpan Span, SourceSpan ValueSpan);

public sealed class SourceComment : SourceNode
{
    public required string Text { get; init; }

    public bool IsSingleLine => !Text.Trim().Contains('\n');
}

public sealed class SourceText : SourceNode
{
    /// <summary>Decoded text; ARM's extra escaping layer is removed when the text is an expression.</summary>
    public required string Value { get; init; }

    public bool IsExpression { get; init; }

    public bool IsCData { get; init; }
}

/// <summary>A loaded effective policy: the unmodified source text plus its source-mapped tree. In memory only (VIS-SEC-02).</summary>
public sealed class PolicySourceDocument
{
    public required string Text { get; init; }

    public required int LineCount { get; init; }

    public required SourceElement Root { get; init; }
}

public interface IPolicySourceLoader
{
    PolicySourceDocument Load(string policyXml);
}

/// <summary>
/// Loads effective policy XML into a source-mapped tree (SPF-FR-01): DTDs prohibited, no resolver, comments retained,
/// 1-based spans for every element, comment, text node and attribute. Nothing is evaluated.
/// </summary>
public sealed class PolicySourceLoader : IPolicySourceLoader
{
    public PolicySourceDocument Load(string policyXml)
    {
        if (string.IsNullOrWhiteSpace(policyXml))
        {
            throw new PolicyParseException("Policy XML is empty.");
        }

        var lines = new LineIndex(policyXml);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = false,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = false,
        };

        SourceElement? root = null;
        try
        {
            using var stringReader = new StringReader(policyXml);
            using var reader = XmlReader.Create(stringReader, settings);
            var lineInfo = (IXmlLineInfo)reader;
            var stack = new Stack<SourceElement>();

            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Element:
                    {
                        var nameOffset = lines.Offset(lineInfo.LineNumber, lineInfo.LinePosition);
                        var tagStart = nameOffset - 1;
                        var element = new SourceElement { Name = reader.LocalName };
                        var isEmpty = reader.IsEmptyElement;

                        while (reader.MoveToNextAttribute())
                        {
                            if (reader.Prefix == "xmlns" || reader.LocalName == "xmlns")
                            {
                                continue;
                            }

                            element.AddAttribute(ReadAttribute(reader, lineInfo, lines, policyXml));
                        }

                        reader.MoveToElement();
                        var tagEnd = FindStartTagEnd(policyXml, tagStart);
                        // Provisional end (the start tag); replaced by the end tag position for non-empty elements.
                        element.Span = lines.Span(tagStart, tagEnd);

                        if (stack.Count == 0)
                        {
                            if (root is not null)
                            {
                                throw new PolicyParseException("Policy XML has more than one root element.");
                            }

                            if (!string.Equals(element.Name, "policies", StringComparison.Ordinal))
                            {
                                throw new PolicyParseException($"Expected a <policies> root element but found <{element.Name}>.");
                            }

                            root = element;
                        }
                        else
                        {
                            stack.Peek().AddNode(element);
                        }

                        if (!isEmpty)
                        {
                            stack.Push(element);
                        }

                        break;
                    }

                    case XmlNodeType.EndElement:
                    {
                        var element = stack.Pop();
                        var nameOffset = lines.Offset(lineInfo.LineNumber, lineInfo.LinePosition);
                        var close = policyXml.IndexOf('>', nameOffset);
                        var start = lines.Offset(element.Span.StartLine, element.Span.StartColumn);
                        element.Span = lines.Span(start, close < 0 ? policyXml.Length - 1 : close);
                        break;
                    }

                    case XmlNodeType.Comment when stack.Count > 0:
                    {
                        var contentOffset = lines.Offset(lineInfo.LineNumber, lineInfo.LinePosition);
                        var end = policyXml.IndexOf("-->", contentOffset, StringComparison.Ordinal);
                        stack.Peek().AddNode(new SourceComment
                        {
                            Text = reader.Value,
                            Span = lines.Span(contentOffset - 4, end < 0 ? policyXml.Length - 1 : end + 2),
                        });
                        break;
                    }

                    case XmlNodeType.Text when stack.Count > 0:
                    {
                        var startOffset = lines.Offset(lineInfo.LineNumber, lineInfo.LinePosition);
                        var next = policyXml.IndexOf('<', startOffset);
                        var endOffset = (next < 0 ? policyXml.Length : next) - 1;
                        while (startOffset < endOffset && char.IsWhiteSpace(policyXml[startOffset]))
                        {
                            startOffset++;
                        }

                        while (endOffset > startOffset && char.IsWhiteSpace(policyXml[endOffset]))
                        {
                            endOffset--;
                        }

                        stack.Peek().AddNode(CreateText(reader.Value, lines.Span(startOffset, endOffset), isCData: false));
                        break;
                    }

                    case XmlNodeType.CDATA when stack.Count > 0:
                    {
                        var contentOffset = lines.Offset(lineInfo.LineNumber, lineInfo.LinePosition);
                        var end = policyXml.IndexOf("]]>", contentOffset, StringComparison.Ordinal);
                        stack.Peek().AddNode(CreateText(
                            reader.Value,
                            lines.Span(contentOffset - 9, end < 0 ? policyXml.Length - 1 : end + 2),
                            isCData: true));
                        break;
                    }
                }
            }
        }
        catch (XmlException ex)
        {
            throw new PolicyParseException($"Policy XML is not well-formed: {ex.Message}", ex);
        }

        if (root is null)
        {
            throw new PolicyParseException("Policy XML has no root element.");
        }

        return new PolicySourceDocument { Text = policyXml, LineCount = lines.LineCount, Root = root };
    }

    private static SourceText CreateText(string value, SourceSpan span, bool isCData)
    {
        var isExpression = IsExpression(value);
        return new SourceText
        {
            Value = isExpression && !isCData ? RestoreExpressionText(value) : value,
            IsExpression = isExpression,
            IsCData = isCData,
            Span = span,
        };
    }

    private static SourceAttribute ReadAttribute(XmlReader reader, IXmlLineInfo lineInfo, LineIndex lines, string text)
    {
        var nameOffset = lines.Offset(lineInfo.LineNumber, lineInfo.LinePosition);
        var quote = reader.QuoteChar;
        var equals = text.IndexOf('=', nameOffset);
        var open = equals < 0 ? -1 : text.IndexOf(quote, equals);
        var close = open < 0 ? -1 : text.IndexOf(quote, open + 1);
        if (open < 0 || close < 0)
        {
            open = close = nameOffset;
        }

        var raw = reader.Value;
        var isExpression = IsExpression(raw);
        var valueSpan = close > open + 1 ? lines.Span(open + 1, close - 1) : lines.Span(open, close);
        return new SourceAttribute(
            reader.LocalName,
            isExpression ? RestoreExpressionText(raw) : raw,
            isExpression,
            lines.Span(nameOffset, close),
            valueSpan);
    }

    /// <summary>Finds the <c>&gt;</c> ending a start tag, skipping quoted attribute values (which may contain <c>&gt;</c>).</summary>
    private static int FindStartTagEnd(string text, int tagStart)
    {
        char quote = '\0';
        for (var i = tagStart + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (quote != '\0')
            {
                if (c == quote)
                {
                    quote = '\0';
                }
            }
            else if (c is '"' or '\'')
            {
                quote = c;
            }
            else if (c == '>')
            {
                return i;
            }
        }

        return text.Length - 1;
    }

    public static bool IsExpression(string value) =>
        value.Contains("@(", StringComparison.Ordinal) || value.Contains("@{", StringComparison.Ordinal);

    /// <summary>
    /// ARM <c>format=xml</c> escapes expression bodies one extra level (e.g. <c>&amp;amp;quot;</c>), so after normal
    /// XML decoding they still read <c>&amp;quot;</c>. This pure character substitution removes that layer from
    /// expression-bearing values only; the expression is never evaluated.
    /// </summary>
    public static string RestoreExpressionText(string value) => !value.Contains('&')
        ? value
        : value
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&apos;", "'", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);

    /// <summary>Maps between 1-based line/column positions (XmlReader convention) and string offsets.</summary>
    private sealed class LineIndex
    {
        private readonly List<int> _lineStarts = [0];

        public LineIndex(string text)
        {
            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                if (c == '\r')
                {
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }

                    _lineStarts.Add(i + 1);
                }
                else if (c == '\n')
                {
                    _lineStarts.Add(i + 1);
                }
            }
        }

        public int LineCount => _lineStarts.Count;

        public int Offset(int line, int column) => _lineStarts[Math.Clamp(line, 1, _lineStarts.Count) - 1] + column - 1;

        public SourceSpan Span(int startOffset, int endOffset)
        {
            var (startLine, startColumn) = Position(startOffset);
            var (endLine, endColumn) = Position(Math.Max(startOffset, endOffset));
            return new SourceSpan(startLine, startColumn, endLine, endColumn);
        }

        private (int Line, int Column) Position(int offset)
        {
            var index = _lineStarts.BinarySearch(offset);
            if (index < 0)
            {
                index = ~index - 1;
            }

            return (index + 1, offset - _lineStarts[index] + 1);
        }
    }
}
