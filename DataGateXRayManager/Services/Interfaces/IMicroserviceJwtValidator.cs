using System.Security.Claims;

namespace DataGateXRayManager.Services.Interfaces;

public interface IMicroserviceJwtValidator
{
    bool ValidateToken(
        string token,
        out ClaimsPrincipal? principal,
        JwtValidationRequestContext? request = null);
    Task InitAsync();
}
