using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using AutoMapper;

namespace ApartmentAPI.Services;

// Nghiệp vụ căn hộ: CRUD + lọc theo trạng thái + xóa mềm.
public interface IApartmentService
{
    Task<List<ApartmentDto>> GetAllAsync(CancellationToken ct = default);
    Task<ApartmentDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<ApartmentDto>> GetByStatusAsync(ApartmentStatus status, CancellationToken ct = default);
    Task<ApartmentDto> CreateAsync(CreateApartmentDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateApartmentDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// Triển khai CRUD căn hộ qua IApartmentRepository + AutoMapper.
public sealed class ApartmentService : IApartmentService
{
    private readonly IApartmentRepository _repository;
    private readonly IMapper _mapper;

    public ApartmentService(IApartmentRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<ApartmentDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _repository.GetAllAsync(ct);
        return _mapper.Map<List<ApartmentDto>>(list);
    }

    public async Task<ApartmentDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found.");
        return _mapper.Map<ApartmentDto>(entity);
    }

    public async Task<List<ApartmentDto>> GetByStatusAsync(ApartmentStatus status, CancellationToken ct = default)
    {
        var list = await _repository.GetByStatusAsync(status, ct);
        return _mapper.Map<List<ApartmentDto>>(list);
    }

    public async Task<ApartmentDto> CreateAsync(CreateApartmentDto dto, CancellationToken ct = default)
    {
        var entity = _mapper.Map<Apartment>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        return _mapper.Map<ApartmentDto>(entity);
    }

    public async Task UpdateAsync(Guid id, UpdateApartmentDto dto, CancellationToken ct = default)
    {
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found.");
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
