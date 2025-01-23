using BazaarCompanionWeb.Models.Pagination;

namespace BazaarCompanionWeb.Interfaces;

public interface IResourceQueryHelper<in TQuery, TResult> where TQuery : Pagination where TResult : class
{
    Task<PaginationContext<TResult>> QueryResourceAsync(TQuery request, CancellationToken cancellationToken);
}
