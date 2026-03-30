namespace Paw.Polar;

public class PolarOptions
{
    public string ClientId { get; set; } = "";
    
    public string ClientSecret { get; set; } = "";
    
    public string RedirectUri { get; set; } = "";
    
    public string BaseUrl { get; set; } = "https://www.polaraccesslink.com";
    
    public string AuthorizationEndpoint { get; set; } = "https://flow.polar.com/oauth2/authorization";
    
    public string TokenEndpoint { get; set; } = "https://polarremote.com/v2/oauth2/token";
    
    public string UserRegistrationEndpoint { get; set; } = "https://www.polaraccesslink.com/v3/users";
    
    public string Scope { get; set; } = "accesslink.read_all";
    
    public string WebhookUrl { get; set; } = "";
    
    public string? WebhookSignatureSecret { get; set; }
    
    public string WebhooksEndpoint { get; set; } = "/v3/webhooks";
    
    public string WebhooksActivateEndpoint { get; set; } = "/v3/webhooks/activate";
}