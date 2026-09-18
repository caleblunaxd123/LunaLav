namespace Lavanderia.Api.Services;

/// <summary>Contratos sin proveedor acoplado. Las implementaciones reales se habilitan solo con configuración segura.</summary>
public interface ILeadDiscoveryProvider { string Code { get; } Task<IReadOnlyList<ExternalLead>> SearchAsync(LeadDiscoveryQuery query, CancellationToken ct = default); }
public interface IEmailProvider { string Code { get; } Task<EmailSendResult> SendAsync(EmailSendCommand command, CancellationToken ct = default); }
public interface IInboundEmailProvider { string Code { get; } Task SyncAsync(int mailboxId, CancellationToken ct = default); }
public interface IMarketingAIProvider { string Code { get; } Task<AiSuggestion> SuggestAsync(AiSuggestionRequest request, CancellationToken ct = default); }
public record LeadDiscoveryQuery(string Query, string? District, int Limit);
public record ExternalLead(string Name, string Source, string? Phone, string? Website, string? Address);
public record EmailSendCommand(int MailboxId, string To, string Subject, string HtmlBody, string IdempotencyKey);
public record EmailSendResult(bool Accepted, string? ProviderMessageId, string? Error);
public record AiSuggestionRequest(string Purpose, string Context);
public record AiSuggestion(string Content, string Disclaimer = "Sugerencia IA: verifica esta información antes de usarla.");
