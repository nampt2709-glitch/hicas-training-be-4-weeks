using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using AutoMapper;

namespace ApartmentAPI.Services;

// Nghiệp vụ dòng hóa đơn: CRUD + theo hóa đơn.
public interface IInvoiceItemService
{
    Task<List<InvoiceItemDto>> GetAllAsync(CancellationToken ct = default);
    Task<InvoiceItemDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<InvoiceItemDto>> GetByInvoiceIdAsync(Guid invoiceId, CancellationToken ct = default);
    Task<InvoiceItemDto> CreateAsync(CreateInvoiceItemDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateInvoiceItemDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// CRUD InvoiceItem: kiểm tra InvoiceId + ServiceId (UtilityService).
public sealed class InvoiceItemService : IInvoiceItemService
{
    private readonly IInvoiceItemRepository _repository;
    private readonly IInvoiceRepository _invoices;
    private readonly IUtilityServiceRepository _services;
    private readonly IMapper _mapper;

    public InvoiceItemService(
        IInvoiceItemRepository repository,
        IInvoiceRepository invoices,
        IUtilityServiceRepository services,
        IMapper mapper)
    {
        _repository = repository;
        _invoices = invoices;
        _services = services;
        _mapper = mapper;
    }

    public async Task<List<InvoiceItemDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _repository.GetAllAsync(ct);
        return _mapper.Map<List<InvoiceItemDto>>(list);
    }

    public async Task<InvoiceItemDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice item not found.");
        return _mapper.Map<InvoiceItemDto>(entity);
    }

    public async Task<List<InvoiceItemDto>> GetByInvoiceIdAsync(Guid invoiceId, CancellationToken ct = default)
    {
        var list = await _repository.GetByInvoiceIdAsync(invoiceId, ct);
        return _mapper.Map<List<InvoiceItemDto>>(list);
    }

    public async Task<InvoiceItemDto> CreateAsync(CreateInvoiceItemDto dto, CancellationToken ct = default)
    {
        await EnsureFkAsync(dto.InvoiceId, dto.ServiceId, ct);

        var entity = _mapper.Map<InvoiceItem>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        return _mapper.Map<InvoiceItemDto>(entity);
    }

    public async Task UpdateAsync(Guid id, UpdateInvoiceItemDto dto, CancellationToken ct = default)
    {
        await EnsureFkAsync(dto.InvoiceId, dto.ServiceId, ct);

        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice item not found.");
        _mapper.Map(dto, tracked);
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
    }

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
    }

    private async Task EnsureFkAsync(Guid invoiceId, Guid serviceId, CancellationToken ct)
    {
        if (!await _invoices.ExistsAsync(invoiceId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice not found.");
        if (!await _services.ExistsAsync(serviceId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Utility service not found.");
    }
}
