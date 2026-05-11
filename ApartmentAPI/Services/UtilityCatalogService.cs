using ApartmentAPI.DTOs; // PagedResult.
using ApartmentAPI.Entities; // UtilityService.
using ApartmentAPI.Repositories; // IUtilityServiceRepository.
using ApartmentAPI.V1.DTOs; // UtilityServiceDto, Create/Update.
using AutoMapper; // IMapper.

namespace ApartmentAPI.Services;

// Danh mục dịch vụ tiện ích (UtilityService): phân trang + cache khi không filter, CRUD + xóa mềm.
public interface IUtilityCatalogService
{
    Task<PagedResult<UtilityServiceDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        bool? isActive,
        string? nameContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<UtilityServiceDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UtilityServiceDto> CreateAsync(CreateUtilityServiceDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateUtilityServiceDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

public sealed class UtilityCatalogService : ServiceBase, IUtilityCatalogService
{
    private readonly IUtilityServiceRepository _repository; // Danh mục dịch vụ.
    private readonly IMapper _mapper; // Map UtilityService ↔ DTO.

    public UtilityCatalogService(
        IUtilityServiceRepository repository,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
        : base(cache, listEpoch)
    { // Mở khối constructor.
        _repository = repository;
        _mapper = mapper;
    } // Kết thúc constructor.

    private static bool HasUtilityListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        bool? isActive,
        string? nameContains) =>
        HasCreatedAtFilter(createdAtFrom, createdAtTo) || isActive.HasValue || HasTextFilter(nameContains);

    public async Task<PagedResult<UtilityServiceDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        bool? isActive,
        string? nameContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        var sortSpec = ListSortParsing.ParseUtilitySort(sort, sortDir);

        if (!HasUtilityListFilter(createdAtFrom, createdAtTo, isActive, nameContains))
        {
            var epoch = await ListEpoch.GetUtilitiesListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.UtilitiesPaged(
                epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await Cache.GetJsonAsync<PagedResult<UtilityServiceDto>>(cacheKey, ct);
            if (cached is not null)
                return cached;
        }

        var (items, total, p, s) = await _repository.GetPagedAsync(
            page, pageSize, createdAtFrom, createdAtTo, isActive, nameContains, sortSpec, ct);

        var dtos = _mapper.Map<List<UtilityServiceDto>>(items);
        var result = new PagedResult<UtilityServiceDto>
        {
            Items = dtos,
            Page = p,
            PageSize = s,
            TotalCount = total,
        };

        if (!HasUtilityListFilter(createdAtFrom, createdAtTo, isActive, nameContains))
        {
            var epoch = await ListEpoch.GetUtilitiesListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.UtilitiesPaged(
                epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await Cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetPagedAsync.

    public async Task<UtilityServiceDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        var key = EntityCacheKeys.UtilityService(id);
        var cached = await Cache.GetJsonAsync<UtilityServiceDto>(key, ct);
        if (cached is not null)
            return cached;

        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Utility service not found.");
        var dto = _mapper.Map<UtilityServiceDto>(entity);
        await Cache.SetJsonAsync(key, dto, ct);
        return dto;
    } // Kết thúc GetByIdAsync.

    public async Task<UtilityServiceDto> CreateAsync(CreateUtilityServiceDto dto, CancellationToken ct = default)
    { // Mở khối CreateAsync.
        var entity = _mapper.Map<UtilityService>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<UtilityServiceDto>(entity);
        await Cache.SetJsonAsync(EntityCacheKeys.UtilityService(entity.Id), dtoOut, ct);
        await ListEpoch.InvalidateUtilitiesListsAsync(ct);
        return dtoOut;
    } // Kết thúc CreateAsync.

    public async Task UpdateAsync(Guid id, UpdateUtilityServiceDto dto, CancellationToken ct = default)
    { // Mở khối UpdateAsync.
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Utility service not found.");
        _mapper.Map(dto, tracked);
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
        await Cache.RemoveAsync(EntityCacheKeys.UtilityService(id), ct);
        await ListEpoch.InvalidateUtilitiesListsAsync(ct);
    } // Kết thúc UpdateAsync.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
        await Cache.RemoveAsync(EntityCacheKeys.UtilityService(id), ct);
        await ListEpoch.InvalidateUtilitiesListsAsync(ct);
    } // Kết thúc SoftDeleteAsync.
}
