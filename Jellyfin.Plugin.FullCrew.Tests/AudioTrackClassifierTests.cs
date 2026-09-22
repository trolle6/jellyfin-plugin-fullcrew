using Jellyfin.Plugin.FullCrew.Services;
using Xunit;

namespace Jellyfin.Plugin.FullCrew.Tests;

public class AudioTrackClassifierTests
{
    [Theory]
    [InlineData("Commentary")]
    [InlineData("Director Commentary")]
    [InlineData("Commentary with Dan Harmon")]
    [InlineData("Cast Commentary")]
    [InlineData("English commentary track")]
    [InlineData("Kommentar")]
    [InlineData("Commentaire réalisateur")]
    [InlineData("Comentario")]
    [InlineData("Trivia Track")]
    [InlineData("Filmmaker comments")]
    [InlineData("コメンタリー")]
    public void Classify_CommentaryTitles(string title)
    {
        Assert.Equal(AudioTrackKind.Commentary, AudioTrackClassifier.Classify(title));
    }

    [Theory]
    [InlineData("Audio Description")]
    [InlineData("Audio-Description")]
    [InlineData("Descriptive Audio")]
    [InlineData("Descriptive Narration")]
    [InlineData("DVS")]
    [InlineData("English AD")]
    [InlineData("Visually Impaired")]
    [InlineData("(AD) English")]
    public void Classify_AudioDescriptionTitles(string title)
    {
        Assert.Equal(AudioTrackKind.AudioDescription, AudioTrackClassifier.Classify(title));
    }

    [Theory]
    [InlineData("French Dub")]
    [InlineData("Dubbed")]
    [InlineData("German Synchronisation")]
    [InlineData("English dubbing")]
    public void Classify_DubTitles(string title)
    {
        Assert.Equal(AudioTrackKind.Dub, AudioTrackClassifier.Classify(title));
    }

    [Theory]
    [InlineData("Isolated Score")]
    [InlineData("Score Only")]
    [InlineData("Music Only")]
    [InlineData("Isolated Music")]
    public void Classify_IsolatedScoreTitles(string title)
    {
        Assert.Equal(AudioTrackKind.IsolatedScore, AudioTrackClassifier.Classify(title));
    }

    [Theory]
    [InlineData("Isolated Effects")]
    [InlineData("Effects Only")]
    [InlineData("Music and Effects")]
    [InlineData("M&E")]
    [InlineData("Clean Effects")]
    public void Classify_IsolatedEffectsTitles(string title)
    {
        Assert.Equal(AudioTrackKind.IsolatedEffects, AudioTrackClassifier.Classify(title));
    }

    [Fact]
    public void Classify_Karaoke()
    {
        Assert.Equal(AudioTrackKind.Karaoke, AudioTrackClassifier.Classify("Karaoke"));
    }

    [Theory]
    [InlineData("Isolated Dialogue")]
    [InlineData("Left/Right Mix")]
    [InlineData("Music Video Mix")]
    public void Classify_OtherLabeledTitles(string title)
    {
        Assert.Equal(AudioTrackKind.Labeled, AudioTrackClassifier.Classify(title));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("English")]
    [InlineData("eng")]
    [InlineData("Japanese")]
    [InlineData("Stereo")]
    [InlineData("5.1")]
    [InlineData("AAC")]
    [InlineData("English Stereo")]
    [InlineData("eng 5.1")]
    [InlineData("Audio")]
    [InlineData("Default")]
    [InlineData("Original")]
    public void Classify_IgnoresOrdinaryDialogue(string? title)
    {
        Assert.Null(AudioTrackClassifier.Classify(title));
    }

    [Fact]
    public void Classify_CommentaryBeatsDubInSameTitle()
    {
        Assert.Equal(AudioTrackKind.Commentary, AudioTrackClassifier.Classify("Commentary (German Dub)"));
    }

    [Fact]
    public void Classify_UsesCommentWhenTitleEmpty()
    {
        Assert.Equal(AudioTrackKind.Commentary, AudioTrackClassifier.Classify(null, "Director commentary"));
    }

    [Fact]
    public void Classify_DoesNotTreatCommercialAsCommentary()
    {
        Assert.Equal(AudioTrackKind.Labeled, AudioTrackClassifier.Classify("Commercial break bumper"));
    }

    [Fact]
    public void FormatDisplayTitle_IncludesTitleLanguageCodecAndChannels()
    {
        var display = AudioTrackClassifier.FormatDisplayTitle("Commentary", "eng", "ac3", 2);
        Assert.Contains("Commentary", display, StringComparison.Ordinal);
        Assert.Contains("English", display, StringComparison.Ordinal);
        Assert.Contains("AC3", display, StringComparison.Ordinal);
        Assert.Contains("Stereo", display, StringComparison.Ordinal);
    }

    [Fact]
    public void Find_ReturnsKnownKindAndNullForAll()
    {
        Assert.Equal(AudioTrackKind.Commentary, AudioTrackKind.Find("commentary"));
        Assert.Null(AudioTrackKind.Find("all"));
        Assert.Null(AudioTrackKind.Find("nope"));
    }
}
