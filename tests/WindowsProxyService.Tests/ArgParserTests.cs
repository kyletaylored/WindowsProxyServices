using WindowsProxyService;

namespace WindowsProxyService.Tests;

public class ArgParserTests
{
    [Fact]
    public void NoArgs_ReturnsEmpty()
    {
        var result = ArgParser.Parse([]);
        Assert.False(result.StartAll);
        Assert.Empty(result.Names);
    }

    [Fact]
    public void AllFlag_SetsStartAll()
    {
        var result = ArgParser.Parse(["--all"]);
        Assert.True(result.StartAll);
        Assert.Empty(result.Names);
    }

    [Fact]
    public void AllFlag_CaseInsensitive()
    {
        var result = ArgParser.Parse(["--ALL"]);
        Assert.True(result.StartAll);
    }

    [Fact]
    public void SingleName_ParsesCorrectly()
    {
        var result = ArgParser.Parse(["--name", "OpenMeteo"]);
        Assert.False(result.StartAll);
        Assert.Equal(["OpenMeteo"], result.Names);
    }

    [Fact]
    public void MultipleNames_SpaceSeparated_ParsesAll()
    {
        var result = ArgParser.Parse(["--name", "OpenMeteo", "JsonPlaceholder"]);
        Assert.False(result.StartAll);
        Assert.Equal(2, result.Names.Count);
        Assert.Contains("OpenMeteo",       result.Names);
        Assert.Contains("JsonPlaceholder", result.Names);
    }

    [Fact]
    public void MultipleNameFlags_AreAggregated()
    {
        var result = ArgParser.Parse(["--name", "OpenMeteo", "--name", "JsonPlaceholder"]);
        Assert.False(result.StartAll);
        Assert.Equal(2, result.Names.Count);
        Assert.Contains("OpenMeteo",       result.Names);
        Assert.Contains("JsonPlaceholder", result.Names);
    }

    [Theory]
    [InlineData("*")]
    [InlineData("all")]
    public void StarAndAllAliases_SetStartAll(string alias)
    {
        var result = ArgParser.Parse(["--name", alias]);
        Assert.True(result.StartAll);
        Assert.Empty(result.Names);
    }

    [Fact]
    public void NameFollowedByAllFlag_SetsStartAllAndKeepsName()
    {
        var result = ArgParser.Parse(["--name", "OpenMeteo", "--all"]);
        Assert.True(result.StartAll);
        Assert.Equal(["OpenMeteo"], result.Names);
    }

    [Fact]
    public void UnrelatedFlags_AreIgnored()
    {
        var result = ArgParser.Parse(["--urls", "http://+:5052", "--name", "OpenMeteo"]);
        Assert.Equal(["OpenMeteo"], result.Names);
    }
}
