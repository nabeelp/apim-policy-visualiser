using System.Text;
using System.Text.RegularExpressions;
using ApimPolicyVisualizer.Api.Policy.Model;

namespace ApimPolicyVisualizer.Api.Policy.Source;

/// <summary>
/// One occurrence of an included policy fragment, delimited by sibling <c>include-fragment: Begin/End</c> marker
/// comments (SPF-FR-02). Occurrences are never merged: the same fragment in two places is two regions.
/// </summary>
public sealed class FragmentRegion
{
    public required string Name { get; init; }

    /// <summary><c>{name}#{n}</c>, numbered per name in document order (1-based).</summary>
    public required string OccurrenceId { get; init; }

    public required int Index { get; init; }

    public int Count { get; internal set; }

    /// <summary>inbound | backend | outbound | on-error, or null when the region is outside a stage.</summary>
    public string? Stage { get; init; }

    /// <summary>The element whose children include both markers.</summary>
    public required SourceElement Parent { get; init; }

    public required SourceComment BeginMarker { get; init; }

    public required SourceComment EndMarker { get; init; }

    public int BeginIndex => BeginMarker.IndexInParent;

    public int EndIndex => EndMarker.IndexInParent;

    /// <summary>The nearest enclosing region (same parent or an ancestor element's region).</summary>
    public FragmentRegion? ParentRegion { get; internal set; }

    public required string Label { get; init; }

    /// <summary><see cref="FlowProvenance.Comment"/> or <see cref="FlowProvenance.FragmentName"/>.</summary>
    public required string LabelProvenance { get; init; }

    /// <summary>The first descriptive comment inside the region (e.g. a Purpose paragraph), dedented.</summary>
    public string? Description { get; init; }

    /// <summary>From the start of the Begin marker through the end of the End marker.</summary>
    public SourceSpan Span => new(BeginMarker.Span.StartLine, BeginMarker.Span.StartColumn, EndMarker.Span.EndLine, EndMarker.Span.EndColumn);

    public bool Contains(int nodeIndex) => nodeIndex > BeginIndex && nodeIndex < EndIndex;
}

public sealed class FragmentRegionSet
{
    private readonly Dictionary<SourceComment, FragmentRegion> _byBegin;
    private readonly HashSet<SourceComment> _markers;

    internal FragmentRegionSet(IReadOnlyList<FragmentRegion> regions, IReadOnlyList<FlowDiagnostic> diagnostics, HashSet<SourceComment> markers)
    {
        Regions = regions;
        Diagnostics = diagnostics;
        _markers = markers;
        _byBegin = regions.ToDictionary(r => r.BeginMarker);
    }

    public static FragmentRegionSet Empty { get; } = new([], [], []);

    /// <summary>All valid regions in document order of their Begin markers.</summary>
    public IReadOnlyList<FragmentRegion> Regions { get; }

    /// <summary>Warnings for unmatched, crossed or cross-parent markers.</summary>
    public IReadOnlyList<FlowDiagnostic> Diagnostics { get; }

    public FragmentRegion? RegionStartingAt(SourceComment comment) => _byBegin.GetValueOrDefault(comment);

    /// <summary>True for any Begin/End marker comment, paired or not.</summary>
    public bool IsMarker(SourceComment comment) => _markers.Contains(comment);
}

public interface IFragmentRegionBuilder
{
    FragmentRegionSet Build(PolicySourceDocument document);
}

/// <summary>
/// Pairs <c>include-fragment: Begin {name} policy fragment scope</c> / <c>... End ...</c> sibling comments into nested
/// fragment-occurrence regions and derives their labels (SPF-FR-02). Comment text supplies labels and descriptions only;
/// it is never parsed as markup and never changes control flow.
/// </summary>
public sealed partial class FragmentRegionBuilder : IFragmentRegionBuilder
{
    [GeneratedRegex(@"^\s*include-fragment\s*:\s*(?<kind>Begin|End)\s+(?<name>\S+)\s+policy\s+fragment\s+scope\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex MarkerPattern();

    [GeneratedRegex(@"^\s*Step\s+[0-9]+(?:\.[0-9]+)*[A-Za-z]?(?:\s*[-–]\s*[0-9]+(?:\.[0-9]+)*[A-Za-z]?)?\s*[:.)\-–]\s*", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StepPrefixPattern();

    [GeneratedRegex(@"^\s*Fragment\s*:\s*(?<title>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
    private static partial Regex FragmentTitlePattern();

    public FragmentRegionSet Build(PolicySourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var markers = new HashSet<SourceComment>();
        var pairs = new List<(SourceElement Parent, SourceComment Begin, SourceComment End, string Name)>();
        var unmatchedBegins = new List<(SourceComment Comment, string Name)>();
        var unmatchedEnds = new List<(SourceComment Comment, string Name)>();
        var diagnostics = new List<FlowDiagnostic>();

        foreach (var element in document.Root.DescendantsAndSelf())
        {
            PairMarkers(element, markers, pairs, unmatchedBegins, unmatchedEnds, diagnostics);
        }

        ReportUnmatched(unmatchedBegins, unmatchedEnds, diagnostics);

        pairs.Sort((a, b) => Compare(a.Begin.Span, b.Begin.Span));
        var perName = new Dictionary<string, int>(StringComparer.Ordinal);
        var regions = new List<FragmentRegion>(pairs.Count);
        foreach (var (parent, begin, end, name) in pairs)
        {
            var index = perName.GetValueOrDefault(name) + 1;
            perName[name] = index;
            var (label, provenance, description) = DeriveLabel(parent, begin, end, name, markers);
            regions.Add(new FragmentRegion
            {
                Name = name,
                OccurrenceId = $"{name}#{index}",
                Index = index,
                Stage = StageOf(parent, document.Root),
                Parent = parent,
                BeginMarker = begin,
                EndMarker = end,
                Label = label,
                LabelProvenance = provenance,
                Description = description,
            });
        }

        foreach (var region in regions)
        {
            region.Count = perName[region.Name];
        }

        AssignParentRegions(document.Root, null, regions.GroupBy(r => r.Parent)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.BeginIndex).ToList()));

        return new FragmentRegionSet(regions, diagnostics, markers);
    }

    /// <summary>Returns the marker kind and fragment name when <paramref name="text"/> is a Begin/End marker.</summary>
    public static (bool IsBegin, string Name)? ParseMarker(string text)
    {
        var match = MarkerPattern().Match(text);
        if (!match.Success)
        {
            return null;
        }

        return (match.Groups["kind"].Value.Equals("Begin", StringComparison.OrdinalIgnoreCase), match.Groups["name"].Value);
    }

    /// <summary>Turns <c>set-response-headers</c> into <c>Set response headers</c>.</summary>
    public static string Humanize(string name)
    {
        var words = name.Split(['-', '_', ' ', '.'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return name;
        }

        var text = string.Join(' ', words);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static void PairMarkers(
        SourceElement parent,
        HashSet<SourceComment> markers,
        List<(SourceElement, SourceComment, SourceComment, string)> pairs,
        List<(SourceComment, string)> unmatchedBegins,
        List<(SourceComment, string)> unmatchedEnds,
        List<FlowDiagnostic> diagnostics)
    {
        List<(SourceComment Comment, string Name)>? stack = null;
        List<string>? crossedNames = null;

        foreach (var node in parent.Nodes)
        {
            if (node is not SourceComment comment || ParseMarker(comment.Text) is not { } marker)
            {
                continue;
            }

            markers.Add(comment);
            stack ??= [];
            if (marker.IsBegin)
            {
                stack.Add((comment, marker.Name));
                continue;
            }

            var position = stack.FindLastIndex(entry => string.Equals(entry.Name, marker.Name, StringComparison.Ordinal));
            if (position == stack.Count - 1 && position >= 0)
            {
                pairs.Add((parent, stack[position].Comment, comment, marker.Name));
                stack.RemoveAt(position);
            }
            else if (position >= 0)
            {
                // Crossed: Begin A, Begin B, End A. Neither A nor the markers opened after it form a region.
                for (var i = stack.Count - 1; i > position; i--)
                {
                    diagnostics.Add(Warning(
                        "fragment-marker-crossed",
                        $"Fragment marker 'Begin {stack[i].Name}' is crossed by 'End {marker.Name}'; the affected elements are left ungrouped.",
                        stack[i].Comment.Span));
                    (crossedNames ??= []).Add(stack[i].Name);
                }

                diagnostics.Add(Warning(
                    "fragment-marker-crossed",
                    $"Fragment markers for '{marker.Name}' cross another fragment's markers; the affected elements are left ungrouped.",
                    comment.Span));
                stack.RemoveRange(position, stack.Count - position);
            }
            else if (crossedNames is not null && crossedNames.Remove(marker.Name))
            {
                // End of a region already reported as crossed.
            }
            else
            {
                unmatchedEnds.Add((comment, marker.Name));
            }
        }

        if (stack is not null)
        {
            unmatchedBegins.AddRange(stack);
        }
    }

    private static void ReportUnmatched(
        List<(SourceComment Comment, string Name)> begins,
        List<(SourceComment Comment, string Name)> ends,
        List<FlowDiagnostic> diagnostics)
    {
        foreach (var begin in begins)
        {
            var endIndex = ends.FindIndex(e => string.Equals(e.Name, begin.Name, StringComparison.Ordinal) && !ReferenceEquals(e.Comment.Parent, begin.Comment.Parent));
            if (endIndex >= 0)
            {
                diagnostics.Add(Warning(
                    "fragment-marker-cross-parent",
                    $"Fragment markers for '{begin.Name}' are not siblings (Begin and End have different parent elements); the affected elements are left ungrouped.",
                    begin.Comment.Span));
                ends.RemoveAt(endIndex);
                continue;
            }

            diagnostics.Add(Warning(
                "fragment-marker-unmatched",
                $"Fragment marker 'Begin {begin.Name}' has no matching End marker among its siblings; the affected elements are left ungrouped.",
                begin.Comment.Span));
        }

        foreach (var end in ends)
        {
            diagnostics.Add(Warning(
                "fragment-marker-unmatched",
                $"Fragment marker 'End {end.Name}' has no matching Begin marker among its siblings.",
                end.Comment.Span));
        }
    }

    private static (string Label, string Provenance, string? Description) DeriveLabel(
        SourceElement parent, SourceComment begin, SourceComment end, string name, HashSet<SourceComment> markers)
    {
        // (1) The immediately preceding single-line sibling comment that is not a marker, minus any "Step N:" prefix.
        if (begin.IndexInParent > 0
            && parent.Nodes[begin.IndexInParent - 1] is SourceComment preceding
            && !markers.Contains(preceding)
            && preceding.IsSingleLine)
        {
            var text = StepPrefixPattern().Replace(preceding.Text.Trim(), string.Empty).Trim();
            if (text.Length > 0)
            {
                return (text, FlowProvenance.Comment, InnerDescription(parent, begin, end, markers));
            }
        }

        var description = InnerDescription(parent, begin, end, markers);

        // (2) The "Fragment: {title}" line of the first inner comment.
        if (description is not null && FragmentTitlePattern().Match(description) is { Success: true } title)
        {
            return (title.Groups["title"].Value, FlowProvenance.Comment, description);
        }

        // (3) The humanised fragment name.
        return (Humanize(name), FlowProvenance.FragmentName, description);
    }

    /// <summary>The first non-marker comment inside the region, before its first element.</summary>
    private static string? InnerDescription(SourceElement parent, SourceComment begin, SourceComment end, HashSet<SourceComment> markers)
    {
        for (var i = begin.IndexInParent + 1; i < end.IndexInParent; i++)
        {
            switch (parent.Nodes[i])
            {
                case SourceElement:
                    return null;
                case SourceComment comment when !markers.Contains(comment):
                    return Dedent(comment.Text);
            }
        }

        return null;
    }

    internal static string? Dedent(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[0].Length == 0)
        {
            lines.RemoveAt(0);
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        if (lines.Count == 0)
        {
            return null;
        }

        var indent = lines.Where(l => l.Length > 0).Min(l => l.Length - l.TrimStart().Length);
        var builder = new StringBuilder();
        for (var i = 0; i < lines.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            builder.Append(lines[i].Length >= indent ? lines[i][indent..] : lines[i].TrimStart());
        }

        return builder.ToString();
    }

    private static string? StageOf(SourceElement parent, SourceElement root)
    {
        var current = parent;
        while (current.Parent is not null && !ReferenceEquals(current.Parent, root))
        {
            current = current.Parent;
        }

        return ReferenceEquals(current, root) ? null : current.Name;
    }

    private static void AssignParentRegions(
        SourceElement element,
        FragmentRegion? enclosing,
        Dictionary<SourceElement, List<FragmentRegion>> regionsByParent)
    {
        var regions = regionsByParent.GetValueOrDefault(element);
        var open = new List<FragmentRegion>();
        var next = 0;

        foreach (var node in element.Nodes)
        {
            var index = node.IndexInParent;
            while (open.Count > 0 && open[^1].EndIndex < index)
            {
                open.RemoveAt(open.Count - 1);
            }

            if (regions is not null && next < regions.Count && regions[next].BeginIndex == index)
            {
                var region = regions[next++];
                region.ParentRegion = open.Count > 0 ? open[^1] : enclosing;
                open.Add(region);
                continue;
            }

            if (node is SourceElement child)
            {
                AssignParentRegions(child, open.Count > 0 ? open[^1] : enclosing, regionsByParent);
            }
        }
    }

    private static int Compare(SourceSpan a, SourceSpan b) =>
        a.StartLine != b.StartLine ? a.StartLine.CompareTo(b.StartLine) : a.StartColumn.CompareTo(b.StartColumn);

    private static FlowDiagnostic Warning(string code, string message, SourceSpan span) =>
        new(FlowDiagnosticSeverities.Warning, code, message, span);
}
