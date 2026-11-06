using JobOrchestrator.Profile.Contracts;

namespace JobOrchestrator.Tests.Unit;

/// <summary>A candidatura sai na língua da vaga quando é português ou inglês, ou outra que você fala; senão, em inglês.</summary>
public sealed class LanguagesTests
{
    [Theory]
    [InlineData("Buscamos desenvolvedor .NET com experiência em AWS para o nosso time, com foco em qualidade e entregas.", "pt")]
    [InlineData("We are looking for a .NET developer with experience in AWS to join our team and grow with us.", "en")]
    [InlineData("Buscamos un desarrollador .NET con experiencia en AWS para el equipo, con foco en la calidad y en los resultados.", "es")]
    [InlineData("Wir suchen einen .NET Entwickler mit Erfahrung in AWS für unser Team, der die Plattform mit uns weiterentwickelt.", "de")]
    [InlineData("Vi söker en .NET utvecklare med erfarenhet av AWS som vill vara med och bygga vår plattform och det är på riktigt.", "sv")]
    public void Acha_a_lingua_da_vaga(string text, string expected) => Assert.Equal(expected, Languages.DetectAny(text));

    [Fact] public void Curriculo_e_respostas_so_em_portugues_ou_ingles() =>
        Assert.Equal(("pt", "en", "en"), (Languages.PtOrEn("Buscamos desenvolvedor com experiência para o nosso time"), Languages.PtOrEn("Vi söker en utvecklare med erfarenhet för att det är"), Languages.PtOrEn("We need a developer with experience")));

    [Fact]
    public void Outra_lingua_so_se_voce_fala()
    {
        string[] mine = ["Português (nativo) native", "Inglês (avançado) advanced", "Espanhol (intermediário) intermediate", "Alemão (básico) basic"];
        Assert.Equal("es", Languages.Writing("es", mine));
        Assert.Equal("en", Languages.Writing("de", mine));
        Assert.Equal("en", Languages.Writing("sv", mine));
        Assert.Equal("pt", Languages.Writing("pt", []));
        Assert.Equal("en", Languages.Writing("en", []));
    }
}
