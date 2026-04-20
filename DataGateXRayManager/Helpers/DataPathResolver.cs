namespace DataGateXRayManager.Helpers;

public class DataPathResolver(IConfiguration configuration) : IDataPathResolver
{
    public string GetDataPath()
    {
        return Environment.GetEnvironmentVariable("DATA_DIR")
               ?? configuration["DataDir:MainPath"]
               ?? throw new InvalidOperationException("DATA_DIR or DataDir:MainPath is not set");
    }
}
