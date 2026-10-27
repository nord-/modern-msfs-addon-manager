namespace AddonManager.Sim2020.Tests.Bgl;

public class BglShapeFidelityTests
{
    [Theory]
    [InlineData("generic")]
    [InlineData("eham-bbd")]
    [InlineData("yalr1")]
    [InlineData("asobo-airport")]
    [InlineData("lfcs-saucats")]
    public void Built_file_has_the_same_structure_as_the_real_one(string name)
    {
        var shape = BglBuilder.LoadShape(name);
        var rebuilt = BglShapes.Describe(new MemoryStream(BglBuilder.FromShape(shape)));
        Assert.Equivalent(shape, rebuilt, strict: true);
    }
}
