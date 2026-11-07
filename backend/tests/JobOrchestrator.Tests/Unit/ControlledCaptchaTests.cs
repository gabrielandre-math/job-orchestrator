using System.Net;
using System.Text;
using JobOrchestrator.Automation.Features;

namespace JobOrchestrator.Tests.Unit;

public sealed class ControlledCaptchaTests
{
    [NativeOcrFact]
    public async Task Local_ocr_reads_pixels_without_access_to_a_reference_alphabet()
    {
        using var stream = typeof(ControlledCaptchaTests).Assembly.GetManifestResourceStream("ocr-numeric.png.b64")!;
        using var reader = new StreamReader(stream);
        var image = Convert.FromBase64String(await reader.ReadToEndAsync());
        Assert.Equal("57392", await ControlledTests.OcrAsync(NativeOcrFactAttribute.Executable!, image, "0123456789", default));
    }
    [Theory]
    [InlineData("https://captcha.example.test", "https://captcha.example.test/form", true)]
    [InlineData("http://127.0.0.1:5471", "http://127.0.0.1:5471/controlled-test", true)]
    [InlineData("http://192.168.1.10:8000", "http://192.168.1.10:8000/form", true)]
    [InlineData("https://captcha.example.test", "https://captcha.example.test.evil.test/form", false)]
    [InlineData("https://captcha.example.test", "https://captcha.example.test:8443/form", false)]
    [InlineData("https://captcha.example.test", "https://other.test/?next=https://captcha.example.test", false)]
    [InlineData("https://captcha.example.test", "https://user:pass@captcha.example.test/form", false)]
    [InlineData("https://www.linkedin.com", "https://www.linkedin.com/jobs/view/123", false)]
    [InlineData("http://public.example.test", "http://public.example.test/form", false)]
    public void Controlled_tests_require_exact_owned_origin(string origin, string url, bool expected) =>
        Assert.Equal(expected, ControlledTests.Allows(new(true, [origin]), url));

    [Fact]
    public void Disabled_tests_cannot_navigate() => Assert.False(ControlledTests.Allows(new(false, ["https://captcha.example.test"]), "https://captcha.example.test/form"));

    [Theory]
    [InlineData("12 + 7", "19")]
    [InlineData("4 - 9", "-5")]
    [InlineData("1 + 2 * 3", null)]
    [InlineData("__import__('os')", null)]
    [InlineData("1000 + 1", null)]
    public void Arithmetic_has_a_strict_grammar(string expression, string? answer) => Assert.Equal(answer, ControlledTests.MathAnswer(expression));

    [Theory]
    [InlineData("{\"success\":true,\"hostname\":\"captcha.example.test\",\"action\":\"job_orchestrator_test\"}", true)]
    [InlineData("{\"success\":true,\"hostname\":\"other.test\",\"action\":\"job_orchestrator_test\"}", false)]
    [InlineData("{\"success\":true,\"hostname\":\"captcha.example.test\",\"action\":\"other\"}", false)]
    [InlineData("{\"success\":false,\"error-codes\":[\"timeout-or-duplicate\"]}", false)]
    [InlineData("{\"success\":true}", false)]
    [InlineData("not JSON", false)]
    public async Task Real_keys_require_provider_success_expected_host_and_action(string json, bool expected)
    {
        using var http = new HttpClient(new Handler(json));
        var result = await ControlledCaptchaVerifier.VerifyAsync(http, new("turnstile", "public-key", "private-key", false, "captcha.example.test"), "token-from-widget", default);
        Assert.Equal(expected, result.Success);
    }

    [Fact]
    public async Task Test_keys_are_an_explicit_mode_not_a_production_fallback()
    {
        using var http = new HttpClient(new Handler("{\"success\":true,\"hostname\":\"example.com\"}"));
        Assert.True((await ControlledCaptchaVerifier.VerifyAsync(http, new("turnstile", "test", "test", true, null), "dummy", default)).Success);
        Assert.False((await ControlledCaptchaVerifier.VerifyAsync(http, new("turnstile", "test", "test", false, "captcha.example.test"), "dummy", default)).Success);
    }
    private sealed class Handler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}

public sealed class NativeOcrFactAttribute : FactAttribute
{
    public static readonly string? Executable = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Select(p => Path.Combine(p, OperatingSystem.IsWindows() ? "tesseract.exe" : "tesseract")).FirstOrDefault(File.Exists);
    public NativeOcrFactAttribute() { if (Executable is null) Skip = "Instale Tesseract para executar o teste nativo de OCR."; }
}
