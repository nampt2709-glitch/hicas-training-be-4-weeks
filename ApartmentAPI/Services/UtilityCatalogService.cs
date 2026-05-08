using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using AutoMapper;

namespace ApartmentAPI.Services;

// Nghiệp vụ bảng giá dịch vụ tiện ích (entity UtilityService).
public interface IUtilityCatalogService
{
    Task<List<UtilityServiceDto>> GetAllAsync(CancellationToken ct = default);
    Task<UtilityServiceDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<UtilityServiceDto>> GetActiveAsync(CancellationToken ct = default);
    Task<UtilityServiceDto> CreateAsync(CreateUtilityServiceDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateUtilityServiceDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// CRUD UtilityService qua repository.
public sealed class UtilityCatalogService : IUtilityCatalogService
{
    private readonly IUtilityServiceRepository _repository;
    private readonly IMapper _mapper;

    public UtilityCatalogService(IUtilityServiceRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<UtilityServiceDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _repository.GetAllAsync(ct);
        return _mapper.Map<List<UtilityServiceDto>>(list);
    }

    public async Task<UtilityServiceDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Utility service not found.");
        return _mapper.Map<UtilityServiceDto>(entity);
    }

    public async Task<List<UtilityServiceDto>> GetActiveAsync(CancellationToken ct = default)
    {
        var list = await _repository.GetActiveAsync(ct);
        return _mapper.Map<List<UtilityServiceDto>>(list);
    }

    public async Task<UtilityServiceDto> CreateAsync(CreateUtilityServiceDto dto, CancellationToken ct = default)
    {
        var entity = _mapper.Map<UtilityService>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        return _mapper.Map<UtilityServiceDto>(entity);
    }

    public async Task UpdateAsync(Guid id, UpdateUtilityServiceDto dto, CancellationToken ct = default)
    {
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Utility service not found.");
        _mapper.Map(dto, tracked);
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
    }

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
    }
}
