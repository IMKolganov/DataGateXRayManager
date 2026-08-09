namespace DataGateXRayManager.Services.Interfaces;

public interface IExternalIpAddressService
{
    Task<string?> GetPublicIpAddressAsync(CancellationToken cancellationToken);
}
