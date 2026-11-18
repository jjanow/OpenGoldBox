namespace GoldBox.Client.Tests;

public class ModuleOptionTests
{
    [Fact]
    public void Module_option_is_parsed_next_to_the_game_folder()
    {
        var o = ClientOptions.Parse(["--game", "/g", "--module", "/m", "--save", "J"]);
        Assert.Equal("/g", o.GameDirectory);
        Assert.Equal("/m", o.ModuleDirectory);
        Assert.Equal("J", o.Save);
        Assert.Null(ClientOptions.Parse([]).ModuleDirectory);
        Assert.Throws<ArgumentException>(() => ClientOptions.Parse(["--module"]));
        Assert.Contains("--module DIR", ClientOptions.Usage);
    }
}
