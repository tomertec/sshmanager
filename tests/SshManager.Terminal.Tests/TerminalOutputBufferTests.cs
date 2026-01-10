using FluentAssertions;

namespace SshManager.Terminal.Tests;

/// <summary>
/// Unit tests for TerminalOutputBuffer - the text storage for terminal search functionality.
/// </summary>
public class TerminalOutputBufferTests
{
    [Fact]
    public void Constructor_WithDefaultMaxLines_SetsMaxLinesToDefault()
    {
        var buffer = new TerminalOutputBuffer();

        buffer.MaxLines.Should().Be(10000);
    }

    [Fact]
    public void Constructor_WithCustomMaxLines_SetsMaxLines()
    {
        var buffer = new TerminalOutputBuffer(500);

        buffer.MaxLines.Should().Be(500);
    }

    [Fact]
    public void Constructor_WithLowMaxLines_EnforcesMinimum()
    {
        var buffer = new TerminalOutputBuffer(50);

        buffer.MaxLines.Should().Be(100, "minimum should be enforced to 100");
    }

    [Fact]
    public void MaxLines_SetBelowMinimum_EnforcesMinimum()
    {
        var buffer = new TerminalOutputBuffer();
        buffer.MaxLines = 10;

        buffer.MaxLines.Should().Be(100);
    }

    [Fact]
    public void LineCount_InitiallyZero()
    {
        var buffer = new TerminalOutputBuffer();

        buffer.LineCount.Should().Be(0);
    }

    [Fact]
    public void AppendOutput_WithEmptyString_DoesNothing()
    {
        var buffer = new TerminalOutputBuffer();

        buffer.AppendOutput("");
        buffer.AppendOutput(null!);

        buffer.LineCount.Should().Be(0);
    }

    [Fact]
    public void AppendOutput_WithSingleLine_StoresLine()
    {
        var buffer = new TerminalOutputBuffer();

        buffer.AppendOutput("Hello World\n");

        buffer.LineCount.Should().Be(1);
        buffer.GetLine(0).Should().Be("Hello World");
    }

    [Fact]
    public void AppendOutput_WithMultipleLines_StoresAllLines()
    {
        var buffer = new TerminalOutputBuffer();

        buffer.AppendOutput("Line 1\nLine 2\nLine 3\n");

        buffer.LineCount.Should().Be(3);
        buffer.GetLine(0).Should().Be("Line 1");
        buffer.GetLine(1).Should().Be("Line 2");
        buffer.GetLine(2).Should().Be("Line 3");
    }

    [Fact]
    public void AppendOutput_WithPartialLine_DoesNotStoreLine()
    {
        var buffer = new TerminalOutputBuffer();

        buffer.AppendOutput("Partial line without newline");

        buffer.LineCount.Should().Be(0, "partial lines are buffered until newline");
    }

    [Fact]
    public void AppendOutput_WithPartialThenNewline_StoresCompleteLine()
    {
        var buffer = new TerminalOutputBuffer();

        buffer.AppendOutput("Partial ");
        buffer.AppendOutput("line\n");

        buffer.LineCount.Should().Be(1);
        buffer.GetLine(0).Should().Be("Partial line");
    }

    [Fact]
    public void AppendOutput_WithAnsiEscapeSequences_StripsEscapeSequences()
    {
        var buffer = new TerminalOutputBuffer();

        // ANSI color escape: ESC[32m = green, ESC[0m = reset
        buffer.AppendOutput("\x1B[32mGreen Text\x1B[0m\n");

        buffer.GetLine(0).Should().Be("Green Text");
    }

    [Fact]
    public void AppendOutput_WithComplexAnsiSequences_StripsAllSequences()
    {
        var buffer = new TerminalOutputBuffer();

        // Multiple ANSI sequences: bold, color, reset
        buffer.AppendOutput("\x1B[1m\x1B[31mBold Red\x1B[0m Normal\n");

        buffer.GetLine(0).Should().Be("Bold Red Normal");
    }

    [Fact]
    public void AppendOutput_WithCursorMovement_StripsEscapeSequences()
    {
        var buffer = new TerminalOutputBuffer();

        // Cursor movement: ESC[H = home, ESC[2J = clear screen
        buffer.AppendOutput("\x1B[H\x1B[2JCleared Screen\n");

        buffer.GetLine(0).Should().Be("Cleared Screen");
    }

    [Fact]
    public void AppendOutput_ExceedingMaxLines_TrimsOldLines()
    {
        var buffer = new TerminalOutputBuffer(100);

        // Add 150 lines
        for (int i = 0; i < 150; i++)
        {
            buffer.AppendOutput($"Line {i}\n");
        }

        buffer.LineCount.Should().Be(100);
        buffer.GetLine(0).Should().Be("Line 50", "oldest 50 lines should be trimmed");
        buffer.GetLine(99).Should().Be("Line 149");
    }

    [Fact]
    public void GetLine_WithValidIndex_ReturnsLine()
    {
        var buffer = new TerminalOutputBuffer();
        buffer.AppendOutput("Line 0\nLine 1\nLine 2\n");

        buffer.GetLine(1).Should().Be("Line 1");
    }

    [Fact]
    public void GetLine_WithNegativeIndex_ReturnsEmpty()
    {
        var buffer = new TerminalOutputBuffer();
        buffer.AppendOutput("Line 0\n");

        buffer.GetLine(-1).Should().BeEmpty();
    }

    [Fact]
    public void GetLine_WithOutOfRangeIndex_ReturnsEmpty()
    {
        var buffer = new TerminalOutputBuffer();
        buffer.AppendOutput("Line 0\n");

        buffer.GetLine(1).Should().BeEmpty();
        buffer.GetLine(100).Should().BeEmpty();
    }

    [Fact]
    public void GetLines_ReturnsRequestedRange()
    {
        var buffer = new TerminalOutputBuffer();
        buffer.AppendOutput("Line 0\nLine 1\nLine 2\nLine 3\nLine 4\n");

        var lines = buffer.GetLines(1, 3);

        lines.Should().HaveCount(3);
        lines[0].Should().Be("Line 1");
        lines[1].Should().Be("Line 2");
        lines[2].Should().Be("Line 3");
    }

    [Fact]
    public void GetLines_WithOverflowCount_ReturnsAvailableLines()
    {
        var buffer = new TerminalOutputBuffer();
        buffer.AppendOutput("Line 0\nLine 1\n");

        var lines = buffer.GetLines(0, 100);

        lines.Should().HaveCount(2);
    }

    [Fact]
    public void GetAllText_ReturnsAllLinesWithNewlines()
    {
        var buffer = new TerminalOutputBuffer();
        buffer.AppendOutput("Line 0\nLine 1\n");

        var text = buffer.GetAllText();

        text.Should().Contain("Line 0");
        text.Should().Contain("Line 1");
    }

    [Fact]
    public void GetAllText_IncludesPartialLine()
    {
        var buffer = new TerminalOutputBuffer();
        buffer.AppendOutput("Line 0\nPartial");

        var text = buffer.GetAllText();

        text.Should().Contain("Line 0");
        text.Should().Contain("Partial");
    }

    [Fact]
    public void Clear_RemovesAllLines()
    {
        var buffer = new TerminalOutputBuffer();
        buffer.AppendOutput("Line 0\nLine 1\n");

        buffer.Clear();

        buffer.LineCount.Should().Be(0);
        buffer.GetAllText().Should().BeEmpty();
    }

    [Fact]
    public void AppendOutput_WithTabs_PreservesTabs()
    {
        var buffer = new TerminalOutputBuffer();

        buffer.AppendOutput("Column1\tColumn2\tColumn3\n");

        buffer.GetLine(0).Should().Be("Column1\tColumn2\tColumn3");
    }

    [Fact]
    public async Task AppendOutput_IsThreadSafe()
    {
        var buffer = new TerminalOutputBuffer(1000);
        var tasks = new List<Task>();

        for (int i = 0; i < 10; i++)
        {
            var threadId = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 50; j++)
                {
                    buffer.AppendOutput($"Thread {threadId} Line {j}\n");
                }
            }));
        }

        await Task.WhenAll(tasks);

        buffer.LineCount.Should().Be(500, "10 threads * 50 lines = 500 lines");
    }

    [Fact]
    public void AppendOutput_WithOSCEscape_StripsEscapeSequences()
    {
        var buffer = new TerminalOutputBuffer();

        // OSC (Operating System Command) sequence for setting window title
        buffer.AppendOutput("\x1B]0;Window Title\x07Some Text\n");

        buffer.GetLine(0).Should().Be("Some Text");
    }

    [Fact]
    public void AppendOutput_WithAlternateScreenBuffer_StripsEscapeSequences()
    {
        var buffer = new TerminalOutputBuffer();

        // Mode 1049: Enter alternate screen buffer, then exit
        buffer.AppendOutput("\x1B[?1049hAlternate Screen Content\x1B[?1049l\n");

        // The escape sequences should be stripped, content preserved
        buffer.GetLine(0).Should().Contain("Alternate Screen Content");
    }
}
