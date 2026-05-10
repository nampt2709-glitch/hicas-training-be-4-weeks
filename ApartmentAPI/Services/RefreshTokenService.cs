using ApartmentAPI.DTOs; // PagedResult.
using ApartmentAPI.V1.DTOs; // RefreshTokenDto, Create/Update.
using ApartmentAPI.Entities; // RefreshToken, User.
using ApartmentAPI.Repositories; // IRefreshTokenRepository.
using AutoMapper; // IMapper.
using Microsoft.AspNetCore.Identity; // UserManager.

namespace ApartmentAPI.Services;

// Nghiệp vụ refresh token: danh sách phân trang + cache khi không filter, CRUD.
public interface IRefreshTokenService
{
    Task<PagedResult<RefreshTokenDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        bool? isRevoked,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<RefreshTokenDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<RefreshTokenDto> CreateAsync(CreateRefreshTokenDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateRefreshTokenDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// CRUD RefreshToken: map tạo mới; cập nhật trường điều khiển thu hồi thủ công (bảng lưu metadata; JWT refresh thực tế do AuthenticationService phát).
public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly IRefreshTokenRepository _repository; // Bản ghi refresh token (DB).
    private readonly UserManager<User> _users; // FK UserId khi tạo.
    private readonly IMapper _mapper; // Map RefreshToken ↔ DTO.
    private readonly IEntityResponseCache _cache;
    private readonly ICacheListEpochStore _listEpoch; // Epoch danh sách refresh token.

    public RefreshTokenService(
        IRefreshTokenRepository repository,
        UserManager<User> users,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
    { // Mở khối constructor.
        _repository = repository;
        _users = users;
        _mapper = mapper;
        _cache = cache;
        _listEpoch = listEpoch;
    } // Kết thúc constructor.

    private static bool HasListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        bool? isRevoked) =>
        createdAtFrom.HasValue || createdAtTo.HasValue || userId.HasValue || isRevoked.HasValue;

    public async Task<PagedResult<RefreshTokenDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        bool? isRevoked,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        var sortSpec = ListSortParsing.ParseRefreshTokenSort(sort, sortDir);

        if (!HasListFilter(createdAtFrom, createdAtTo, userId, isRevoked))
        {
            var epoch = await _listEpoch.GetRefreshTokensListEpochAsync(ct);
            var key = EntityCacheKeys.RefreshTokensPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await _cache.GetJsonAsync<PagedResult<RefreshTokenDto>>(key, ct);
            if (cached is not null)
                return cached;
        }

        var (items, total, p, s) = await _repository.GetPagedAsync(
            page, pageSize, createdAtFrom, createdAtTo, userId, isRevoked, sortSpec, ct);

        var dtos = _mapper.Map<List<RefreshTokenDto>>(items);
        var result = new PagedResult<RefreshTokenDto> { Items = dtos, Page = p, PageSize = s, TotalCount = total };

        if (!HasListFilter(createdAtFrom, createdAtTo, userId, isRevoked))
        {
            var epoch = await _listEpoch.GetRefreshTokensListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.RefreshTokensPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await _cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetPagedAsync.

    public async Task<RefreshTokenDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        var key = EntityCacheKeys.RefreshToken(id);
        var cached = await _cache.GetJsonAsync<RefreshTokenDto>(key, ct);
        if (cached is not null)
            return cached;

        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Refresh token not found.");
        var dto = _mapper.Map<RefreshTokenDto>(entity);
        await _cache.SetJsonAsync(key, dto, ct);
        return dto;
    } // Kết thúc GetByIdAsync.

    public async Task<RefreshTokenDto> CreateAsync(CreateRefreshTokenDto dto, CancellationToken ct = default)
    { // Mở khối CreateAsync.
        if (await _users.FindByIdAsync(dto.UserId.ToString()) is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");

        var entity = _mapper.Map<RefreshToken>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<RefreshTokenDto>(entity);
        await _cache.SetJsonAsync(EntityCacheKeys.RefreshToken(entity.Id), dtoOut, ct);
        await _listEpoch.InvalidateRefreshTokensListsAsync(ct);
        return dtoOut;
    } // Kết thúc CreateAsync.

    public async Task UpdateAsync(Guid id, UpdateRefreshTokenDto dto, CancellationToken ct = default)
    { // Mở khối UpdateAsync — chỉnh trạng thái thu hồi / thay thế (không đổi UserId qua DTO).
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Refresh token not found.");

        tracked.IsRevoked = dto.IsRevoked;
        tracked.RevokedAt = dto.RevokedAt;
        tracked.ReplacedByTokenHash = dto.ReplacedByTokenHash;
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
        await _cache.RemoveAsync(EntityCacheKeys.RefreshToken(id), ct);
        await _listEpoch.InvalidateRefreshTokensListsAsync(ct);
    } // Kết thúc UpdateAsync.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
        await _cache.RemoveAsync(EntityCacheKeys.RefreshToken(id), ct);
        await _listEpoch.InvalidateRefreshTokensListsAsync(ct);
    } // Kết thúc SoftDeleteAsync.
}
