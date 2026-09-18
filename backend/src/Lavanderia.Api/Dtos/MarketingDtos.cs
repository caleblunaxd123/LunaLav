using System.ComponentModel.DataAnnotations;

namespace Lavanderia.Api.Dtos;

public record MarketingLoginRequest([Required, StringLength(80)] string Usuario, [Required, StringLength(200)] string Password);
public record MarketingUserDto(int Id, string Usuario, string Nombre, string Rol, string? Email);
public record MarketingLoginResponse(string AccessToken, DateTime Expira, string RefreshToken, MarketingUserDto Usuario);
public record MarketingRefreshRequest([Required, StringLength(200)] string RefreshToken);
public record MarketingDashboardDto(int ProspectosTotales, int Nuevos, int Contactados, int Interesados, int Demos, int Ganados, int SeguimientosPendientes, int SeguimientosVencidos, int TareasPendientes, IReadOnlyList<MarketingNextActionDto> Prioridades);
public record MarketingNextActionDto(long ProspectId, string Negocio, string Estado, int Score, string? Distrito, DateTime? ProximaAccion, string Motivo);
public record MarketingTagDto(int Id, string Nombre, string Color);
public record MarketingActivityDto(long Id, string Tipo, string? Canal, string Titulo, string? Detalle, DateTime Fecha, string? Usuario);
public record MarketingFollowUpDto(long Id, long ProspectId, string Negocio, string Tipo, string Prioridad, string Descripcion, DateTime FechaProgramada, bool Completado);
public record MarketingTaskDto(long Id, long? ProspectId, string? Negocio, string Titulo, string Prioridad, DateTime? FechaVencimiento, string Estado, string? Notas);
public record MarketingProspectListItemDto(long Id, string NombreComercial, string? Distrito, string? Telefono, string? Whatsapp, string? Email, string Estado, string Prioridad, int Score, string Fuente, DateTime? FechaUltimoContacto, DateTime? ProximoSeguimiento, IReadOnlyList<MarketingTagDto> Tags, decimal? Latitud = null, decimal? Longitud = null, string? Direccion = null, int? NumeroSedesEstimado = null, bool TieneDelivery = false, decimal? Rating = null);
public record MarketingProspectDetailDto(MarketingProspectListItemDto Resumen, string? RazonSocial, string? Ruc, string? SitioWeb, string? Instagram, string? Facebook, string? TikTok, string? Direccion, string Pais, int? NumeroSedesEstimado, bool TieneDelivery, decimal? Rating, int? NumeroResenas, string? TipoNegocio, string FormaTrabajoActual, string? SoftwareActual, string? Observaciones, IReadOnlyList<MarketingActivityDto> Actividades, IReadOnlyList<MarketingFollowUpDto> Seguimientos);
public class MarketingProspectRequest
{
    [Required, StringLength(180)] public string NombreComercial { get; set; } = "";
    [StringLength(180)] public string? RazonSocial { get; set; } [StringLength(16)] public string? Ruc { get; set; }
    [StringLength(40)] public string? Telefono { get; set; } [StringLength(40)] public string? Whatsapp { get; set; } [EmailAddress, StringLength(180)] public string? Email { get; set; }
    [StringLength(300)] public string? SitioWeb { get; set; } [StringLength(160)] public string? Instagram { get; set; }
    [StringLength(300)] public string? Direccion { get; set; } [StringLength(100)] public string? Distrito { get; set; }
    [StringLength(80)] public string? TipoNegocio { get; set; } public int? NumeroSedesEstimado { get; set; } public bool TieneDelivery { get; set; }
    [Range(0, 5)] public decimal? Rating { get; set; } [Range(0, 10000000)] public int? NumeroResenas { get; set; }
    [Required, StringLength(30)] public string Estado { get; set; } = "NUEVO"; [Required, StringLength(15)] public string Prioridad { get; set; } = "MEDIA";
    [Required, StringLength(80)] public string Fuente { get; set; } = "MANUAL"; [Required, StringLength(40)] public string FormaTrabajoActual { get; set; } = "DESCONOCIDO";
    [StringLength(120)] public string? SoftwareActual { get; set; } [StringLength(2000)] public string? Observaciones { get; set; }
    public List<int> TagIds { get; set; } = [];
}
public record MarketingActivityRequest([Required, StringLength(30)] string Tipo, [StringLength(30)] string? Canal, [Required, StringLength(180)] string Titulo, [StringLength(2000)] string? Detalle);
public record MarketingStatusRequest([Required, StringLength(30)] string Estado);
public record MarketingFollowUpRequest(long ProspectId, [Required, StringLength(30)] string Tipo, [Required, StringLength(15)] string Prioridad, [Required, StringLength(500)] string Descripcion, DateTime FechaProgramada);
public record MarketingTaskRequest(long? ProspectId, [Required, StringLength(180)] string Titulo, [Required, StringLength(15)] string Prioridad, DateTime? FechaVencimiento, [StringLength(1000)] string? Notas);
public record MarketingTagRequest([Required, StringLength(60)] string Nombre, [Required, StringLength(12)] string Color);
public record MarketingCampaignDto(long Id, string Nombre, string Audiencia, string Canal, string? Asunto, string? Mensaje, string Estado, DateTime? FechaProgramada, DateTime FechaCreacion);
public record MarketingCampaignRequest([Required, StringLength(160)] string Nombre, [Required, StringLength(200)] string Audiencia, [Required, StringLength(30)] string Canal, [StringLength(200)] string? Asunto, string? Mensaje, DateTime? FechaProgramada);
public record MarketingDraftDto(long Id, long? ProspectId, string? Prospecto, string Canal, string Destinatario, string? Asunto, string Cuerpo, string Estado, DateTime FechaCreacion);
public record MarketingDraftRequest(long? ProspectId, [Required, StringLength(30)] string Canal, [Required, StringLength(200)] string Destinatario, [StringLength(200)] string? Asunto, [Required] string Cuerpo);
public record MarketingAgentSettingDto(string Nombre, bool AnalizarProspectos, bool GenerarBorradores, bool CrearSeguimientos, bool PrimerContactoAutomatico, bool SeguimientosAutomaticos, int LimiteDiario);
public record MarketingAutomationRuleDto(int Id,string Nombre,string JobType,int FrecuenciaMinutos,bool Activa,bool RequiereAprobacion,DateTime? UltimaEjecucion);
public record MarketingApprovalDto(long Id,string ActionType,string Risk,string Status,string? Reason,DateTime RequestedAt,string? ReviewedBy,DateTime? ReviewedAt);
