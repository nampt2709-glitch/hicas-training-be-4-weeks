using ApartmentAPI.DTOs; // PagedResult.
using ApartmentAPI.Entities; // Resident, User.
using ApartmentAPI.Repositories; // IResidentRepository, IApartmentRepository.
using ApartmentAPI.V1.DTOs; // ResidentDto, Create/Update.
using AutoMapper; // IMapper.
using Microsoft.AspNetCore.Identity; // UserManager.

namespace ApartmentAPI.Services;

// Nghiệp vụ cư dân: phân trang + cache khi không filter; FK tuỳ chọn tới căn hộ và user.
public interface IResidentService
{
    Task<PagedResult<ResidentDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? apartmentId,
        string? fullNameContains,
        string? identityContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<ResidentDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<ResidentDto> CreateAsync(CreateResidentDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateResidentDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

public sealed class ResidentService : ServiceBase, IResidentService
{
    private readonly IResidentRepository _repository; // Cư dân.
    private readonly IApartmentRepository _apartments; // Kiểm tra căn hộ tuỳ chọn.
    private readonly UserManager<User> _users; // Kiểm tra user tuỳ chọn.
    private readonly IMapper _mapper; // Map Resident ↔ DTO.

    public ResidentService(
        IResidentRepository repository,
        IApartmentRepository apartments,
        UserManager<User> users,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
        : base(cache, listEpoch)
    { // Mở khối constructor.
        _repository = repository;
        _apartments = apartments;
        _users = users;
        _mapper = mapper;
    } // Kết thúc constructor.

    private static bool HasResidentListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? apartmentId,
        string? fullNameContains,
        string? identityContains) =>
        HasCreatedAtFilter(createdAtFrom, createdAtTo)
        || apartmentId.HasValue
        || HasTextFilter(fullNameContains)
        || HasTextFilter(identityContains);

    public async Task<PagedResult<ResidentDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? apartmentId,
        string? fullNameContains,
        string? identityContains,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        var sortSpec = ListSortParsing.ParseResidentSort(sort, sortDir);

        if (!HasResidentListFilter(createdAtFrom, createdAtTo, apartmentId, fullNameContains, identityContains))
        {
            var epoch = await ListEpoch.GetResidentsListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.ResidentsPaged(
                epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await Cache.GetJsonAsync<PagedResult<ResidentDto>>(cacheKey, ct);
            if (cached is not null)
                return cached;
        }

        var (items, total, p, s) = await _repository.GetPagedAsync(
            page,
            pageSize,
            createdAtFrom,
            createdAtTo,
            apartmentId,
            fullNameContains,
            identityContains,
            sortSpec,
            ct);

        var dtos = _mapper.Map<List<ResidentDto>>(items);
        var result = new PagedResult<ResidentDto>
        {
            Items = dtos,
            Page = p,
            PageSize = s,
            TotalCount = total,
        };

        if (!HasResidentListFilter(createdAtFrom, createdAtTo, apartmentId, fullNameContains, identityContains))
        {
            var epoch = await ListEpoch.GetResidentsListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.ResidentsPaged(
                epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await Cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetPagedAsync.

    public async Task<ResidentDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        var key = EntityCacheKeys.Resident(id);
        var cached = await Cache.GetJsonAsync<ResidentDto>(key, ct);
        if (cached is not null)
            return cached;

        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Resident not found.");
        var dto = _mapper.Map<ResidentDto>(entity);
        await Cache.SetJsonAsync(key, dto, ct);
        return dto;
    } // Kết thúc GetByIdAsync.

    public async Task<ResidentDto> CreateAsync(CreateResidentDto dto, CancellationToken ct = default)
    { // Mở khối CreateAsync.
        await EnsureForeignKeysAsync(dto.ApartmentId, dto.UserId, ct);

        var entity = _mapper.Map<Resident>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<ResidentDto>(entity);
        await Cache.SetJsonAsync(EntityCacheKeys.Resident(entity.Id), dtoOut, ct);
        await ListEpoch.InvalidateResidentsListsAsync(ct);
        return dtoOut;
    } // Kết thúc CreateAsync.

    public async Task UpdateAsync(Guid id, UpdateResidentDto dto, CancellationToken ct = default)
    { // Mở khối UpdateAsync.
        await EnsureForeignKeysAsync(dto.ApartmentId, dto.UserId, ct);

        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Resident not found.");
        _mapper.Map(dto, tracked);
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
        await Cache.RemoveAsync(EntityCacheKeys.Resident(id), ct);
        await ListEpoch.InvalidateResidentsListsAsync(ct);
    } // Kết thúc UpdateAsync.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
        await Cache.RemoveAsync(EntityCacheKeys.Resident(id), ct);
        await ListEpoch.InvalidateResidentsListsAsync(ct);
    } // Kết thúc SoftDeleteAsync.

    private async Task EnsureForeignKeysAsync(Guid? apartmentId, Guid? userId, CancellationToken ct)
    { // Mở khối EnsureForeignKeysAsync — chỉ kiểm tra khi FK có giá trị.
        if (apartmentId is { } aId && !await _apartments.ExistsAsync(aId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found.");
        if (userId is { } uId && await _users.FindByIdAsync(uId.ToString()) is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");
    } // Kết thúc EnsureForeignKeysAsync.
}
