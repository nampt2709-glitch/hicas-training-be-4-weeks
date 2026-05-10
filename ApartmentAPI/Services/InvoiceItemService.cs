using ApartmentAPI.DTOs; // PagedResult.
using ApartmentAPI.V1.DTOs; // InvoiceItemDto, Create/Update.
using ApartmentAPI.Entities; // InvoiceItem.
using ApartmentAPI.Repositories; // IInvoiceItemRepository, IInvoiceRepository, IUtilityServiceRepository.
using AutoMapper; // IMapper.

namespace ApartmentAPI.Services;

// Nghiệp vụ dòng hóa đơn: danh sách phân trang + cache (không filter), CRUD + theo hóa đơn.
public interface IInvoiceItemService
{
    Task<PagedResult<InvoiceItemDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? invoiceId,
        Guid? serviceId,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<InvoiceItemDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<InvoiceItemDto> CreateAsync(CreateInvoiceItemDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateInvoiceItemDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// CRUD InvoiceItem: kiểm tra InvoiceId + ServiceId (UtilityService).
public sealed class InvoiceItemService : IInvoiceItemService
{
    private readonly IInvoiceItemRepository _repository; // Dòng hóa đơn.
    private readonly IInvoiceRepository _invoices; // FK InvoiceId.
    private readonly IUtilityServiceRepository _services; // FK dịch vụ tiện ích.
    private readonly IMapper _mapper; // Map InvoiceItem ↔ DTO.
    private readonly IEntityResponseCache _cache; // Cache JSON.
    private readonly ICacheListEpochStore _listEpoch; // Epoch danh sách dòng hóa đơn.

    public InvoiceItemService(
        IInvoiceItemRepository repository,
        IInvoiceRepository invoices,
        IUtilityServiceRepository services,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
    { // Mở khối constructor.
        _repository = repository;
        _invoices = invoices;
        _services = services;
        _mapper = mapper;
        _cache = cache;
        _listEpoch = listEpoch;
    } // Kết thúc constructor.

    private static bool HasListFilter(
        DateTime? createdAtFrom, DateTime? createdAtTo, Guid? invoiceId, Guid? serviceId) =>
        createdAtFrom.HasValue || createdAtTo.HasValue || invoiceId.HasValue || serviceId.HasValue;

    public async Task<PagedResult<InvoiceItemDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? invoiceId,
        Guid? serviceId,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        var sortSpec = ListSortParsing.ParseInvoiceItemSort(sort, sortDir);

        if (!HasListFilter(createdAtFrom, createdAtTo, invoiceId, serviceId))
        {
            var epoch = await _listEpoch.GetInvoiceItemsListEpochAsync(ct);
            var key = EntityCacheKeys.InvoiceItemsPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await _cache.GetJsonAsync<PagedResult<InvoiceItemDto>>(key, ct);
            if (cached is not null)
                return cached;
        }

        var (items, total, p, s) = await _repository.GetPagedAsync(
            page, pageSize, createdAtFrom, createdAtTo, invoiceId, serviceId, sortSpec, ct);

        var dtos = _mapper.Map<List<InvoiceItemDto>>(items);
        var result = new PagedResult<InvoiceItemDto> { Items = dtos, Page = p, PageSize = s, TotalCount = total };

        if (!HasListFilter(createdAtFrom, createdAtTo, invoiceId, serviceId))
        {
            var epoch = await _listEpoch.GetInvoiceItemsListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.InvoiceItemsPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await _cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetPagedAsync.

    public async Task<InvoiceItemDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        var key = EntityCacheKeys.InvoiceItem(id);
        var cached = await _cache.GetJsonAsync<InvoiceItemDto>(key, ct);
        if (cached is not null)
            return cached;

        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice item not found.");
        var dto = _mapper.Map<InvoiceItemDto>(entity);
        await _cache.SetJsonAsync(key, dto, ct);
        return dto;
    } // Kết thúc GetByIdAsync.

    public async Task<InvoiceItemDto> CreateAsync(CreateInvoiceItemDto dto, CancellationToken ct = default)
    { // Mở khối CreateAsync.
        await EnsureFkAsync(dto.InvoiceId, dto.ServiceId, ct);

        var entity = _mapper.Map<InvoiceItem>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<InvoiceItemDto>(entity);
        await _cache.SetJsonAsync(EntityCacheKeys.InvoiceItem(entity.Id), dtoOut, ct);
        await _listEpoch.InvalidateInvoiceItemsListsAsync(ct);
        return dtoOut;
    } // Kết thúc CreateAsync.

    public async Task UpdateAsync(Guid id, UpdateInvoiceItemDto dto, CancellationToken ct = default)
    { // Mở khối UpdateAsync.
        await EnsureFkAsync(dto.InvoiceId, dto.ServiceId, ct);

        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice item not found.");
        _mapper.Map(dto, tracked);
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
        await _cache.RemoveAsync(EntityCacheKeys.InvoiceItem(id), ct);
        await _listEpoch.InvalidateInvoiceItemsListsAsync(ct);
    } // Kết thúc UpdateAsync.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
        await _cache.RemoveAsync(EntityCacheKeys.InvoiceItem(id), ct);
        await _listEpoch.InvalidateInvoiceItemsListsAsync(ct);
    } // Kết thúc SoftDeleteAsync.

    private async Task EnsureFkAsync(Guid invoiceId, Guid serviceId, CancellationToken ct)
    { // Mở khối EnsureFkAsync — cả hai FK bắt buộc (không nullable trong DTO).
        if (!await _invoices.ExistsAsync(invoiceId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice not found.");
        if (!await _services.ExistsAsync(serviceId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Utility service not found.");
    } // Kết thúc EnsureFkAsync.
}
