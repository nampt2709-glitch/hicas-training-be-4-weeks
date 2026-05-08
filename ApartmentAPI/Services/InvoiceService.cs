using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using AutoMapper;

namespace ApartmentAPI.Services;

// Nghiệp vụ hóa đơn: CRUD + theo căn hộ.
public interface IInvoiceService
{
    Task<List<InvoiceDto>> GetAllAsync(CancellationToken ct = default);
    Task<InvoiceDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<InvoiceDto>> GetByApartmentIdAsync(Guid apartmentId, CancellationToken ct = default);
    Task<InvoiceDto> CreateAsync(CreateInvoiceDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateInvoiceDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// CRUD Invoice: ApartmentId phải tồn tại.
public sealed class InvoiceService : IInvoiceService
{
    private readonly IInvoiceRepository _repository;
    private readonly IApartmentRepository _apartments;
    private readonly IMapper _mapper;

    public InvoiceService(IInvoiceRepository repository, IApartmentRepository apartments, IMapper mapper)
    {
        _repository = repository;
        _apartments = apartments;
        _mapper = mapper;
    }

    public async Task<List<InvoiceDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _repository.GetAllAsync(ct);
        return _mapper.Map<List<InvoiceDto>>(list);
    }

    public async Task<InvoiceDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice not found.");
        return _mapper.Map<InvoiceDto>(entity);
    }

    public async Task<List<InvoiceDto>> GetByApartmentIdAsync(Guid apartmentId, CancellationToken ct = default)
    {
        var list = await _repository.GetByApartmentIdAsync(apartmentId, ct);
        return _mapper.Map<List<InvoiceDto>>(list);
    }

    public async Task<InvoiceDto> CreateAsync(CreateInvoiceDto dto, CancellationToken ct = default)
    {
        if (!await _apartments.ExistsAsync(dto.ApartmentId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found.");

        var entity = _mapper.Map<Invoice>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        return _mapper.Map<InvoiceDto>(entity);
    }

    public async Task UpdateAsync(Guid id, UpdateInvoiceDto dto, CancellationToken ct = default)
    {
        if (!await _apartments.ExistsAsync(dto.ApartmentId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found.");

        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice not found.");
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
