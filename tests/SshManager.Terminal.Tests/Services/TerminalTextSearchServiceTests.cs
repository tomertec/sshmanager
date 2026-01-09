using FluentAssertions;
using SshManager.Terminal.Services;

namespace SshManager.Terminal.Tests.Services;

/// <summary>
/// Unit tests for TerminalTextSearchService - terminal output search functionality.
/// </summary>
public class TerminalTextSearchServiceTests
{
    private TerminalOutputBuffer CreateBufferWithLines(params string[] lines)
    {
        var buffer = new TerminalOutputBuffer();
        foreach (var line in lines)
        {
            buffer.AppendOutput(line + "\n");
        }
        return buffer;
    }

    [Fact]
    public void Constructor_WithNullBuffer_ThrowsArgumentNullException()
    {
        var action = () => new TerminalTextSearchService(null!);

        action.Should().Throw<ArgumentNullException>()
            .WithParameterName("buffer");
    }

    [Fact]
    public void Search_WithEmptyBuffer_FindsNoMatches()
    {
        var buffer = new TerminalOutputBuffer();
        var service = new TerminalTextSearchService(buffer);

        service.Search("test", caseSensitive: false);

        service.MatchCount.Should().Be(0);
        service.CurrentMatch.Should().BeNull();
    }

    [Fact]
    public void Search_WithEmptySearchTerm_FindsNoMatches()
    {
        var buffer = CreateBufferWithLines("Some text here");
        var service = new TerminalTextSearchService(buffer);

        service.Search("", caseSensitive: false);

        service.MatchCount.Should().Be(0);
    }

    [Fact]
    public void Search_WithMatchingText_FindsMatch()
    {
        var buffer = CreateBufferWithLines("Hello World");
        var service = new TerminalTextSearchService(buffer);

        service.Search("World", caseSensitive: false);

        service.MatchCount.Should().Be(1);
        service.CurrentMatch.Should().NotBeNull();
        service.CurrentMatch!.LineIndex.Should().Be(0);
        service.CurrentMatch.StartColumn.Should().Be(6);
        service.CurrentMatch.Length.Should().Be(5);
        service.CurrentMatch.MatchedText.Should().Be("World");
    }

    [Fact]
    public void Search_CaseSensitive_MatchesExactCase()
    {
        var buffer = CreateBufferWithLines("Hello World", "hello world");
        var service = new TerminalTextSearchService(buffer);

        service.Search("World", caseSensitive: true);

        service.MatchCount.Should().Be(1);
        service.CurrentMatch!.LineIndex.Should().Be(0);
    }

    [Fact]
    public void Search_CaseInsensitive_MatchesBothCases()
    {
        var buffer = CreateBufferWithLines("Hello WORLD", "hello world");
        var service = new TerminalTextSearchService(buffer);

        service.Search("world", caseSensitive: false);

        service.MatchCount.Should().Be(2);
    }

    [Fact]
    public void Search_MultipleMatchesOnSameLine_FindsAll()
    {
        var buffer = CreateBufferWithLines("test test test");
        var service = new TerminalTextSearchService(buffer);

        service.Search("test", caseSensitive: false);

        service.MatchCount.Should().Be(3);
        service.Matches[0].StartColumn.Should().Be(0);
        service.Matches[1].StartColumn.Should().Be(5);
        service.Matches[2].StartColumn.Should().Be(10);
    }

    [Fact]
    public void Search_AcrossMultipleLines_FindsAll()
    {
        var buffer = CreateBufferWithLines("error: line 1", "info: line 2", "error: line 3");
        var service = new TerminalTextSearchService(buffer);

        service.Search("error", caseSensitive: false);

        service.MatchCount.Should().Be(2);
        service.Matches[0].LineIndex.Should().Be(0);
        service.Matches[1].LineIndex.Should().Be(2);
    }

    [Fact]
    public void Search_SetsCurrentMatchToFirst()
    {
        var buffer = CreateBufferWithLines("first match", "second match");
        var service = new TerminalTextSearchService(buffer);

        service.Search("match", caseSensitive: false);

        service.CurrentMatchIndex.Should().Be(0);
        service.CurrentMatch!.LineIndex.Should().Be(0);
    }

    [Fact]
    public void NextMatch_MovesToNextMatch()
    {
        var buffer = CreateBufferWithLines("match1", "match2", "match3");
        var service = new TerminalTextSearchService(buffer);
        service.Search("match", caseSensitive: false);

        service.NextMatch();

        service.CurrentMatchIndex.Should().Be(1);
        service.CurrentMatch!.LineIndex.Should().Be(1);
    }

    [Fact]
    public void NextMatch_WrapsToBeginning()
    {
        var buffer = CreateBufferWithLines("match1", "match2");
        var service = new TerminalTextSearchService(buffer);
        service.Search("match", caseSensitive: false);
        service.NextMatch(); // Now at index 1

        service.NextMatch(); // Should wrap to index 0

        service.CurrentMatchIndex.Should().Be(0);
    }

    [Fact]
    public void NextMatch_WithNoMatches_ReturnsFalse()
    {
        var buffer = new TerminalOutputBuffer();
        var service = new TerminalTextSearchService(buffer);

        var result = service.NextMatch();

        result.Should().BeFalse();
    }

    [Fact]
    public void PreviousMatch_MovesToPreviousMatch()
    {
        var buffer = CreateBufferWithLines("match1", "match2", "match3");
        var service = new TerminalTextSearchService(buffer);
        service.Search("match", caseSensitive: false);
        service.NextMatch(); // Now at index 1

        service.PreviousMatch();

        service.CurrentMatchIndex.Should().Be(0);
    }

    [Fact]
    public void PreviousMatch_WrapsToEnd()
    {
        var buffer = CreateBufferWithLines("match1", "match2", "match3");
        var service = new TerminalTextSearchService(buffer);
        service.Search("match", caseSensitive: false);

        service.PreviousMatch(); // Should wrap to last (index 2)

        service.CurrentMatchIndex.Should().Be(2);
    }

    [Fact]
    public void PreviousMatch_WithNoMatches_ReturnsFalse()
    {
        var buffer = new TerminalOutputBuffer();
        var service = new TerminalTextSearchService(buffer);

        var result = service.PreviousMatch();

        result.Should().BeFalse();
    }

    [Fact]
    public void ClearSearch_ResetsAllState()
    {
        var buffer = CreateBufferWithLines("match here");
        var service = new TerminalTextSearchService(buffer);
        service.Search("match", caseSensitive: false);

        service.ClearSearch();

        service.MatchCount.Should().Be(0);
        service.CurrentMatchIndex.Should().Be(-1);
        service.CurrentMatch.Should().BeNull();
    }

    [Fact]
    public void IsHighlighted_WithMatchingPosition_ReturnsTrue()
    {
        var buffer = CreateBufferWithLines("hello world");
        var service = new TerminalTextSearchService(buffer);
        service.Search("world", caseSensitive: false);

        var result = service.IsHighlighted(lineIndex: 0, column: 6, out bool isCurrentMatch);

        result.Should().BeTrue();
        isCurrentMatch.Should().BeTrue();
    }

    [Fact]
    public void IsHighlighted_WithNonMatchingPosition_ReturnsFalse()
    {
        var buffer = CreateBufferWithLines("hello world");
        var service = new TerminalTextSearchService(buffer);
        service.Search("world", caseSensitive: false);

        var result = service.IsHighlighted(lineIndex: 0, column: 0, out bool isCurrentMatch);

        result.Should().BeFalse();
        isCurrentMatch.Should().BeFalse();
    }

    [Fact]
    public void IsHighlighted_WithSecondMatch_DistinguishesCurrentMatch()
    {
        var buffer = CreateBufferWithLines("match match");
        var service = new TerminalTextSearchService(buffer);
        service.Search("match", caseSensitive: false);

        // First match is current
        service.IsHighlighted(0, 0, out bool isCurrentFirst);
        service.IsHighlighted(0, 6, out bool isCurrentSecond);

        isCurrentFirst.Should().BeTrue();
        isCurrentSecond.Should().BeFalse();

        // Move to second match
        service.NextMatch();
        service.IsHighlighted(0, 0, out isCurrentFirst);
        service.IsHighlighted(0, 6, out isCurrentSecond);

        isCurrentFirst.Should().BeFalse();
        isCurrentSecond.Should().BeTrue();
    }

    [Fact]
    public void GetMatchesInRange_ReturnsMatchesInViewport()
    {
        var buffer = CreateBufferWithLines(
            "line 0 match",
            "line 1",
            "line 2 match",
            "line 3",
            "line 4 match");
        var service = new TerminalTextSearchService(buffer);
        service.Search("match", caseSensitive: false);

        var matches = service.GetMatchesInRange(startLine: 1, lineCount: 3).ToList();

        matches.Should().HaveCount(1);
        matches[0].LineIndex.Should().Be(2);
    }

    [Fact]
    public void RefreshSearch_RerunsLastSearch()
    {
        var buffer = CreateBufferWithLines("match here");
        var service = new TerminalTextSearchService(buffer);
        service.Search("match", caseSensitive: false);
        service.MatchCount.Should().Be(1);

        // Add more content to buffer
        buffer.AppendOutput("another match\n");

        // Refresh should find new content
        service.RefreshSearch();

        service.MatchCount.Should().Be(2);
    }

    [Fact]
    public void RefreshSearch_WithNoLastSearch_DoesNothing()
    {
        var buffer = CreateBufferWithLines("match here");
        var service = new TerminalTextSearchService(buffer);

        // Should not throw
        service.RefreshSearch();

        service.MatchCount.Should().Be(0);
    }

    [Fact]
    public void RefreshSearch_TriesToRestorePosition()
    {
        var buffer = CreateBufferWithLines("match1", "match2", "match3");
        var service = new TerminalTextSearchService(buffer);
        service.Search("match", caseSensitive: false);
        service.NextMatch(); // Position at index 1

        service.RefreshSearch();

        // Should try to maintain position
        service.CurrentMatchIndex.Should().Be(1);
    }

    [Fact]
    public void Search_WithSpecialRegexCharacters_TreatsAsLiteral()
    {
        var buffer = CreateBufferWithLines("test.txt", "test*txt");
        var service = new TerminalTextSearchService(buffer);

        service.Search(".", caseSensitive: false);

        // Should find literal "." not regex wildcard
        service.MatchCount.Should().Be(1);
        service.CurrentMatch!.LineIndex.Should().Be(0);
    }

    [Fact]
    public void Matches_ReturnsReadOnlyList()
    {
        var buffer = CreateBufferWithLines("test match");
        var service = new TerminalTextSearchService(buffer);
        service.Search("match", caseSensitive: false);

        var matches = service.Matches;

        matches.Should().BeAssignableTo<IReadOnlyList<TextSearchMatch>>();
        matches.Should().HaveCount(1);
    }
}
