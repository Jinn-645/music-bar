using System;
using MusicBar.Services;
using Xunit;

namespace YouTubeCaptions.Tests;

public sealed class CueTests
{
    [Fact]
    public void SelectsCueAtTimestamp()
    {
        var state = new YouTubeCaptionState(TimeSpan.FromSeconds(3));
        state.Accept(new YouTubeCaptionPayload("v", "Song", "Artist", 10, 2, "hello", 0, 1));
        state.Accept(new YouTubeCaptionPayload("v", "Song", "Artist", 10, 4, "world", 3, 1));

        Assert.Equal("world", state.GetActive(TimeSpan.FromSeconds(3.5))?.Text);
    }

    [Fact]
    public void RejectsEmptyCue()
    {
        var state = new YouTubeCaptionState(TimeSpan.FromSeconds(3));
        Assert.False(state.Accept(new YouTubeCaptionPayload("v", "Song", "Artist", 10, 0, " ", 0, 1)));
    }

    [Fact]
    public void ExpiresStaleHeartbeats()
    {
        var state = new YouTubeCaptionState(TimeSpan.FromSeconds(1));
        state.Accept(new YouTubeCaptionPayload("v", "Song", "Artist", 10, 0, "hello", 0, 1));
        Assert.Null(state.GetActive(TimeSpan.FromSeconds(2)));
    }
}
