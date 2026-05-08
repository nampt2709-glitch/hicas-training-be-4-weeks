using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using AutoMapper;
using Microsoft.AspNetCore.Identity;

namespace ApartmentAPI.Services;

// Nghiệp vụ cư dân: CRUD + danh sách theo căn hộ + xóa mềm.
public interface IResidentService
{
    Task<List<ResidentDto>> GetAllAsync(CancellationToken ct = default);
    Task<ResidentDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<ResidentDto>> GetByApartmentIdAsync(Guid apartmentId, CancellationToken ct = default);
    Task<ResidentDto> CreateAsync(CreateResidentDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateResidentDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// Triển khai CRUD Resident: kiểm tra FK căn hộ / user (Identity) trước khi lưu.
public sealed class ResidentService : IResidentService
{
    private readonly IResidentRepository _repository;
    private readonly IApartmentRepository _apartments;
    private readonly UserManager<User> _users;
    private readonly IMapper _mapper;

    public ResidentService(
        IResidentRepository repository,
        IApartmentRepository apartments,
        UserManager<User> users,
        IMapper mapper)
    {
        _repository = repository;
        _apartments = apartments;
        _users = users;
        _mapper = mapper;
    }

    public async Task<List<ResidentDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _repository.GetAllAsync(ct);
        return _mapper.Map<List<ResidentDto>>(list);
    }

    public async Task<ResidentDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Resident not found.");
        return _mapper.Map<ResidentDto>(entity);
    }

    public async Task<List<ResidentDto>> GetByApartmentIdAsync(Guid apartmentId, CancellationToken ct = default)
    {
        var list = await _repository.GetByApartmentIdAsync(apartmentId, ct);
        return _mapper.Map<List<ResidentDto>>(list);
    }

    public async Task<ResidentDto> CreateAsync(CreateResidentDto dto, CancellationToken ct = default)
    {
        await EnsureForeignKeysAsync(dto.ApartmentId, dto.UserId, ct);

        var entity = _mapper.Map<Resident>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        return _mapper.Map<ResidentDto>(entity);
    }

    public async Task UpdateAsync(Guid id, UpdateResidentDto dto, CancellationToken ct = default)
    {
        await EnsureForeignKeysAsync(dto.ApartmentId, dto.UserId, ct);

        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Resident not found.");
        _mapper.Map(dto, tracked);
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
    }

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
    }

    private async Task EnsureForeignKeysAsync(Guid? apartmentId, Guid? userId, CancellationToken ct)
    {
        if (apartmentId is { } aId && !await _apartments.ExistsAsync(aId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found.");
        if (userId is { } uId && await _users.FindByIdAsync(uId.ToString()) is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");
    }
}
