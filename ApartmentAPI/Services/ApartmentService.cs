using ApartmentAPI.DTOs; // PagedResult.
using ApartmentAPI.Entities; // Apartment, ApartmentStatus.
using ApartmentAPI.Repositories; // IApartmentRepository.
using ApartmentAPI.V1.DTOs; // ApartmentDto, Create/Update DTO.
using AutoMapper; // IMapper.

namespace ApartmentAPI.Services;

// Nghiệp vụ căn hộ: CRUD + phân trang có cache (không filter) + xóa mềm.
public interface IApartmentService
{
    Task<PagedResult<ApartmentDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        ApartmentStatus? status,
        string? roomNumberContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<ApartmentDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ApartmentDto> CreateAsync(CreateApartmentDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateApartmentDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// Triển khai CRUD căn hộ qua IApartmentRepository + AutoMapper + cache list/chi tiết.
public sealed class ApartmentService : IApartmentService
{
    private readonly IApartmentRepository _repository; // Truy vấn Apartment.
    private readonly IMapper _mapper; // Map DTO ↔ entity.
    private readonly IEntityResponseCache _cache; // JSON cache.
    private readonly ICacheListEpochStore _listEpoch; // Epoch danh sách căn hộ.

    public ApartmentService(
        IApartmentRepository repository,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
    { // Mở khối constructor.
        _repository = repository;
        _mapper = mapper;
        _cache = cache;
        _listEpoch = listEpoch;
    } // Kết thúc constructor.

    private static bool HasApartmentListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        ApartmentStatus? status,
        string? roomNumberContains) =>
        createdAtFrom.HasValue
        || createdAtTo.HasValue
        || status.HasValue
        || !string.IsNullOrWhiteSpace(roomNumberContains);

    public async Task<PagedResult<ApartmentDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        ApartmentStatus? status,
        string? roomNumberContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        var sortSpec = ListSortParsing.ParseApartmentSort(sort, sortDir);

        if (!HasApartmentListFilter(createdAtFrom, createdAtTo, status, roomNumberContains))
        {
            var epoch = await _listEpoch.GetApartmentsListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.ApartmentsPaged(
                epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await _cache.GetJsonAsync<PagedResult<ApartmentDto>>(cacheKey, ct);
            if (cached is not null)
                return cached;
        }

        var (items, total, p, s) = await _repository.GetPagedAsync(
            page,
            pageSize,
            createdAtFrom,
            createdAtTo,
            status,
            roomNumberContains,
            sortSpec,
            ct);

        var dtos = _mapper.Map<List<ApartmentDto>>(items);
        var result = new PagedResult<ApartmentDto>
        {
            Items = dtos,
            Page = p,
            PageSize = s,
            TotalCount = total,
        };

        if (!HasApartmentListFilter(createdAtFrom, createdAtTo, status, roomNumberContains))
        {
            var epoch = await _listEpoch.GetApartmentsListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.ApartmentsPaged(
                epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await _cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetPagedAsync.

    public async Task<ApartmentDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        var key = EntityCacheKeys.Apartment(id);
        var cached = await _cache.GetJsonAsync<ApartmentDto>(key, ct);
        if (cached is not null)
            return cached;

        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found.");
        var dto = _mapper.Map<ApartmentDto>(entity);
        await _cache.SetJsonAsync(key, dto, ct);
        return dto;
    } // Kết thúc GetByIdAsync.

    public async Task<ApartmentDto> CreateAsync(CreateApartmentDto dto, CancellationToken ct = default)
    { // Mở khối CreateAsync.
        var entity = _mapper.Map<Apartment>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var createdDto = _mapper.Map<ApartmentDto>(entity);
        await _cache.SetJsonAsync(EntityCacheKeys.Apartment(entity.Id), createdDto, ct);
        await _listEpoch.InvalidateApartmentsListsAsync(ct);
        return createdDto;
    } // Kết thúc CreateAsync.

    public async Task UpdateAsync(Guid id, UpdateApartmentDto dto, CancellationToken ct = default)
    { // Mở khối UpdateAsync.
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found.");
        _mapper.Map(dto, tracked);
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
        await _cache.RemoveAsync(EntityCacheKeys.Apartment(id), ct);
        await _listEpoch.InvalidateApartmentsListsAsync(ct);
    } // Kết thúc UpdateAsync.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
        await _cache.RemoveAsync(EntityCacheKeys.Apartment(id), ct);
        await _listEpoch.InvalidateApartmentsListsAsync(ct);
    } // Kết thúc SoftDeleteAsync.
}
