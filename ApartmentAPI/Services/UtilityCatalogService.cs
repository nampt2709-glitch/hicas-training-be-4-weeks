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

public sealed class UtilityCatalogService : IUtilityCatalogService
{
    private readonly IUtilityServiceRepository _repository; // Danh mục dịch vụ.
    private readonly IMapper _mapper; // Map UtilityService ↔ DTO.
    private readonly IEntityResponseCache _cache;
    private readonly ICacheListEpochStore _listEpoch; // Epoch danh mục tiện ích.

    public UtilityCatalogService(
        IUtilityServiceRepository repository,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
    { // Mở khối constructor.
        _repository = repository;
        _mapper = mapper;
        _cache = cache;
        _listEpoch = listEpoch;
    } // Kết thúc constructor.

    private static bool HasUtilityListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        bool? isActive,
        string? nameContains) =>
        createdAtFrom.HasValue || createdAtTo.HasValue || isActive.HasValue || !string.IsNullOrWhiteSpace(nameContains);

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
            var epoch = await _listEpoch.GetUtilitiesListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.UtilitiesPaged(
                epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await _cache.GetJsonAsync<PagedResult<UtilityServiceDto>>(cacheKey, ct);
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
            var epoch = await _listEpoch.GetUtilitiesListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.UtilitiesPaged(
                epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await _cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetPagedAsync.

    public async Task<UtilityServiceDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        var key = EntityCacheKeys.UtilityService(id);
        var cached = await _cache.GetJsonAsync<UtilityServiceDto>(key, ct);
        if (cached is not null)
            return cached;

        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Utility service not found.");
        var dto = _mapper.Map<UtilityServiceDto>(entity);
        await _cache.SetJsonAsync(key, dto, ct);
        return dto;
    } // Kết thúc GetByIdAsync.

    public async Task<UtilityServiceDto> CreateAsync(CreateUtilityServiceDto dto, CancellationToken ct = default)
    { // Mở khối CreateAsync.
        var entity = _mapper.Map<UtilityService>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<UtilityServiceDto>(entity);
        await _cache.SetJsonAsync(EntityCacheKeys.UtilityService(entity.Id), dtoOut, ct);
        await _listEpoch.InvalidateUtilitiesListsAsync(ct);
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
        await _cache.RemoveAsync(EntityCacheKeys.UtilityService(id), ct);
        await _listEpoch.InvalidateUtilitiesListsAsync(ct);
    } // Kết thúc UpdateAsync.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
        await _cache.RemoveAsync(EntityCacheKeys.UtilityService(id), ct);
        await _listEpoch.InvalidateUtilitiesListsAsync(ct);
    } // Kết thúc SoftDeleteAsync.
}
