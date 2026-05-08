using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using AutoMapper;
using Microsoft.AspNetCore.Identity;

namespace ApartmentAPI.Services;

// Nghiệp vụ refresh token: CRUD + theo user (chỉ hash, không lưu token thô qua DTO chi tiết).
public interface IRefreshTokenService
{
    Task<List<RefreshTokenDto>> GetAllAsync(CancellationToken ct = default);
    Task<RefreshTokenDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<RefreshTokenDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<RefreshTokenDto> CreateAsync(CreateRefreshTokenDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateRefreshTokenDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// CRUD RefreshToken: map tạo mới; cập nhật field điều khiển thu hồi thủ công.
public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly IRefreshTokenRepository _repository;
    private readonly UserManager<User> _users;
    private readonly IMapper _mapper;

    public RefreshTokenService(IRefreshTokenRepository repository, UserManager<User> users, IMapper mapper)
    {
        _repository = repository;
        _users = users;
        _mapper = mapper;
    }

    public async Task<List<RefreshTokenDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _repository.GetAllAsync(ct);
        return _mapper.Map<List<RefreshTokenDto>>(list);
    }

    public async Task<RefreshTokenDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Refresh token not found.");
        return _mapper.Map<RefreshTokenDto>(entity);
    }

    public async Task<List<RefreshTokenDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await _repository.GetByUserIdAsync(userId, ct);
        return _mapper.Map<List<RefreshTokenDto>>(list);
    }

    public async Task<RefreshTokenDto> CreateAsync(CreateRefreshTokenDto dto, CancellationToken ct = default)
    {
        if (await _users.FindByIdAsync(dto.UserId.ToString()) is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");

        var entity = _mapper.Map<RefreshToken>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        return _mapper.Map<RefreshTokenDto>(entity);
    }

    public async Task UpdateAsync(Guid id, UpdateRefreshTokenDto dto, CancellationToken ct = default)
    {
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Refresh token not found.");

        tracked.IsRevoked = dto.IsRevoked;
        tracked.RevokedAt = dto.RevokedAt;
        tracked.ReplacedByTokenHash = dto.ReplacedByTokenHash;
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
    }

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
    }
}
