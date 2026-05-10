using ApartmentAPI.DTOs; // PagedResult.
using ApartmentAPI.Entities; // Invoice, InvoiceStatus.
using ApartmentAPI.Repositories; // IInvoiceRepository, IApartmentRepository.
using ApartmentAPI.V1.DTOs; // InvoiceDto, Create/Update.
using AutoMapper; // IMapper.

namespace ApartmentAPI.Services;

// Nghiệp vụ hóa đơn: phân trang + cache khi không filter; mọi hóa đơn gắn căn hộ hợp lệ.
public interface IInvoiceService
{
    Task<PagedResult<InvoiceDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? apartmentId,
        InvoiceStatus? status,
        string? invoiceCode,
        string? sort,
        string? sortDir,
        CancellationToken ct = default);

    Task<InvoiceDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<InvoiceDto> CreateAsync(CreateInvoiceDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateInvoiceDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

public sealed class InvoiceService : IInvoiceService
{
    private readonly IInvoiceRepository _repository; // Hóa đơn.
    private readonly IApartmentRepository _apartments; // FK căn hộ.
    private readonly IMapper _mapper; // Map Invoice ↔ DTO.
    private readonly IEntityResponseCache _cache;
    private readonly ICacheListEpochStore _listEpoch; // Epoch danh sách hóa đơn.

    public InvoiceService(
        IInvoiceRepository repository,
        IApartmentRepository apartments,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
    { // Mở khối constructor.
        _repository = repository;
        _apartments = apartments;
        _mapper = mapper;
        _cache = cache;
        _listEpoch = listEpoch;
    } // Kết thúc constructor.

    private static bool HasInvoiceListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? apartmentId,
        InvoiceStatus? status,
        string? invoiceCode) =>
        createdAtFrom.HasValue
        || createdAtTo.HasValue
        || apartmentId.HasValue
        || status.HasValue
        || !string.IsNullOrWhiteSpace(invoiceCode);

    public async Task<PagedResult<InvoiceDto>> GetPagedAsync(
        int page,
        int pageSize,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? apartmentId,
        InvoiceStatus? status,
        string? invoiceCode,
        string? sort,
        string? sortDir,
        CancellationToken ct = default)
    { // Mở khối GetPagedAsync.
        var sortSpec = ListSortParsing.ParseInvoiceSort(sort, sortDir);

        if (!HasInvoiceListFilter(createdAtFrom, createdAtTo, apartmentId, status, invoiceCode))
        {
            var epoch = await _listEpoch.GetInvoicesListEpochAsync(ct);
            var key = EntityCacheKeys.InvoicesPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            var cached = await _cache.GetJsonAsync<PagedResult<InvoiceDto>>(key, ct);
            if (cached is not null)
                return cached;
        }

        var (items, total, p, s) = await _repository.GetPagedAsync(
            page, pageSize, createdAtFrom, createdAtTo, apartmentId, status, invoiceCode, sortSpec, ct);

        var dtos = _mapper.Map<List<InvoiceDto>>(items);
        var result = new PagedResult<InvoiceDto> { Items = dtos, Page = p, PageSize = s, TotalCount = total };

        if (!HasInvoiceListFilter(createdAtFrom, createdAtTo, apartmentId, status, invoiceCode))
        {
            var epoch = await _listEpoch.GetInvoicesListEpochAsync(ct);
            var cacheKey = EntityCacheKeys.InvoicesPaged(epoch, page, pageSize, sortSpec.CacheSegment, sortSpec.Descending);
            await _cache.SetJsonAsync(cacheKey, result, ct);
        }

        return result;
    } // Kết thúc GetPagedAsync.

    public async Task<InvoiceDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    { // Mở khối GetByIdAsync.
        var key = EntityCacheKeys.Invoice(id);
        var cached = await _cache.GetJsonAsync<InvoiceDto>(key, ct);
        if (cached is not null)
            return cached;

        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice not found.");
        var dto = _mapper.Map<InvoiceDto>(entity);
        await _cache.SetJsonAsync(key, dto, ct);
        return dto;
    } // Kết thúc GetByIdAsync.

    public async Task<InvoiceDto> CreateAsync(CreateInvoiceDto dto, CancellationToken ct = default)
    { // Mở khối CreateAsync.
        if (!await _apartments.ExistsAsync(dto.ApartmentId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found.");

        var entity = _mapper.Map<Invoice>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        var dtoOut = _mapper.Map<InvoiceDto>(entity);
        await _cache.SetJsonAsync(EntityCacheKeys.Invoice(entity.Id), dtoOut, ct);
        await _listEpoch.InvalidateInvoicesListsAsync(ct);
        return dtoOut;
    } // Kết thúc CreateAsync.

    public async Task UpdateAsync(Guid id, UpdateInvoiceDto dto, CancellationToken ct = default)
    { // Mở khối UpdateAsync.
        if (!await _apartments.ExistsAsync(dto.ApartmentId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Apartment not found.");

        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Invoice not found.");
        _mapper.Map(dto, tracked);
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
        await _cache.RemoveAsync(EntityCacheKeys.Invoice(id), ct);
        await _listEpoch.InvalidateInvoicesListsAsync(ct);
    } // Kết thúc UpdateAsync.

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    { // Mở khối SoftDeleteAsync.
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
        await _cache.RemoveAsync(EntityCacheKeys.Invoice(id), ct);
        await _listEpoch.InvalidateInvoicesListsAsync(ct);
    } // Kết thúc SoftDeleteAsync.
}
