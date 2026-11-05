namespace JobOrchestrator.Mail.Features;

/// <summary>Servidores conhecidos: o assistente só pede o endereço e a senha de app.</summary>
internal sealed record MailPreset(string Id, string Name, string Smtp, int SmtpPort, bool SmtpSsl, string Imap, int ImapPort, string AppPasswordUrl, string Hint);

internal static class Presets
{
    public static readonly IReadOnlyList<MailPreset> All =
    [
        new("gmail", "Gmail", "smtp.gmail.com", 465, true, "imap.gmail.com", 993, "https://myaccount.google.com/apppasswords",
            "Precisa da verificação em duas etapas ligada. Crie uma senha de app com o nome Job Orchestrator e cole os 16 caracteres."),
        new("outlook", "Outlook", "smtp-mail.outlook.com", 587, false, "outlook.office365.com", 993, "https://account.live.com/proofs/AppPassword",
            "Crie uma senha de app na conta Microsoft. Algumas contas novas só aceitam login moderno; se a senha de app falhar, use outro provedor."),
        new("icloud", "iCloud", "smtp.mail.me.com", 587, false, "imap.mail.me.com", 993, "https://account.apple.com/account/manage",
            "Em Senhas específicas de app, gere uma para o Job Orchestrator."),
        new("zoho", "Zoho Mail", "smtp.zoho.com", 465, true, "imap.zoho.com", 993, "https://accounts.zoho.com/home#security/app_password",
            "Gere uma senha de app em Segurança, Senhas de aplicativo."),
    ];

    public static MailPreset? Find(string id) => All.FirstOrDefault(p => p.Id == id);
}
