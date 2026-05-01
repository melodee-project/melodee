namespace Melodee.Tests.Cli;

public class MinimalTests
{
    [Fact]
    public void Simple_Test_Passes()
    {
        Assert.True(true);
    }

    [Fact]
    public void String_Test_Works()
    {
        var test = "Hello World";
        Assert.Equal("Hello World", test);
    }

    [Fact]
    public void Number_Test_Works()
    {
        var result = 2 + 2;
        Assert.Equal(4, result);
    }
}
