using Microsoft.Extensions.Logging;
using Nexus.DataModel;
using Nexus.Extensibility;

namespace Nexus.Extensions
{
    internal interface IJsonRpcServer
    {
        public Task<ApiVersionResponse> 
            GetApiVersionAsync(CancellationToken cancellationToken);

        public Task 
            SetContextAsync(DataSourceContext context, CancellationToken cancellationToken);

        public Task<CatalogRegistrationsResponse>
            GetCatalogRegistrationsAsync(string path, CancellationToken cancellationToken);

        public Task<CatalogResponse>
            GetCatalogAsync(string catalogId, CancellationToken cancellationToken);

        public Task<TimeRangeResponse>
            GetTimeRangeAsync(string catalogId, CancellationToken cancellationToken);

        public Task<AvailabilityResponse>
            GetAvailabilityAsync(string catalogId, DateTime begin, DateTime end, CancellationToken cancellationToken);

        public Task
            ReadSingleAsync(DateTime begin, DateTime end, CatalogItem catalogItem, CancellationToken cancellationToken);
    }

    internal record ApiVersionResponse(int ApiVersion);
    internal record CatalogRegistrationsResponse(CatalogRegistration[] Registrations);
    internal record CatalogResponse(ResourceCatalog Catalog);
    internal record TimeRangeResponse(DateTime Begin, DateTime End);
    internal record AvailabilityResponse(double Availability);
    internal record LogMessage(LogLevel LogLevel, string Message);

    internal class RemoteException : Exception
    {
        public RemoteException(string message, Exception? innerException = default)
            : base(message, innerException)
        {
            //
        }
    }
}
