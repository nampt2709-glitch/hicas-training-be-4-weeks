using CommentAPI;

namespace CommentAPI.Services;

// Base service gom helper dùng chung cho nhiều service.
public abstract class ServiceBase
{
    protected readonly IEntityResponseCache Cache;

    protected ServiceBase(IEntityResponseCache cache)
    {
        Cache = cache;
    }

    protected static bool HasCreatedAtFilter(DateTime? createdAtFrom, DateTime? createdAtTo) =>
        createdAtFrom.HasValue || createdAtTo.HasValue;
}
