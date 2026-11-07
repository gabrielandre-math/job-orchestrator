using JobOrchestrator.Intelligence.Features;

namespace JobOrchestrator.Tests.Unit;

/// <summary>An answer counts only when it has the shape its schema asks for.</summary>
public sealed class JsonShapeTests
{
    private const string Verdicts = """
        {"type":"object","required":["verdicts"],"properties":{"verdicts":{"type":"array","items":{"type":"object","required":["claim","supported"],
          "properties":{"claim":{"type":"string"},"supported":{"type":"boolean"},"match":{"type":"string","enum":["direct","gap"]}}}}}}
        """;

    [Theory]
    [InlineData("""{"verdicts":[{"claim":"C1","supported":true}]}""", true)]
    [InlineData("""{"verdicts":[]}""", true)]
    [InlineData("""{"verdicts":[{"claim":"C1"}]}""", false)]
    [InlineData("""{"verdicts":[{"claim":"C1","supported":"sim"}]}""", false)]
    [InlineData("""{"verdicts":[{"claim":"C1","supported":true,"match":"talvez"}]}""", false)]
    [InlineData("""{"outra":1}""", false)]
    [InlineData("""[1,2]""", false)]
    [InlineData("não é json", false)]
    public void Shape_follows_the_schema(string json, bool expected) => Assert.Equal(expected, JsonShape.Matches(Verdicts, json));
}
