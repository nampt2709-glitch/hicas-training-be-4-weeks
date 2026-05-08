using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using AutoMapper;
using Microsoft.AspNetCore.Identity;

namespace ApartmentAPI.Services;

// Nghiệp vụ phản hồi: CRUD + theo user + chỉ gốc (ParentId null).
public interface IFeedbackService
{
    Task<List<FeedbackDto>> GetAllAsync(CancellationToken ct = default);
    Task<FeedbackDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<FeedbackDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<List<FeedbackDto>> GetRootsAsync(CancellationToken ct = default);
    Task<FeedbackDto> CreateAsync(CreateFeedbackDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateFeedbackDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// CRUD Feedback: UserId và ParentId (nếu có) phải hợp lệ.
public sealed class FeedbackService : IFeedbackService
{
    private readonly IFeedbackRepository _repository;
    private readonly UserManager<User> _users;
    private readonly IMapper _mapper;

    public FeedbackService(IFeedbackRepository repository, UserManager<User> users, IMapper mapper)
    {
        _repository = repository;
        _users = users;
        _mapper = mapper;
    }

    public async Task<List<FeedbackDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _repository.GetAllAsync(ct);
        return _mapper.Map<List<FeedbackDto>>(list);
    }

    public async Task<FeedbackDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Feedback not found.");
        return _mapper.Map<FeedbackDto>(entity);
    }

    public async Task<List<FeedbackDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await _repository.GetByUserIdAsync(userId, ct);
        return _mapper.Map<List<FeedbackDto>>(list);
    }

    public async Task<List<FeedbackDto>> GetRootsAsync(CancellationToken ct = default)
    {
        var list = await _repository.GetRootsAsync(ct);
        return _mapper.Map<List<FeedbackDto>>(list);
    }

    public async Task<FeedbackDto> CreateAsync(CreateFeedbackDto dto, CancellationToken ct = default)
    {
        if (await _users.FindByIdAsync(dto.UserId.ToString()) is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");
        if (dto.ParentId is { } pId && !await _repository.ExistsAsync(pId, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Parent feedback not found.");

        var entity = _mapper.Map<Feedback>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        return _mapper.Map<FeedbackDto>(entity);
    }

    public async Task UpdateAsync(Guid id, UpdateFeedbackDto dto, CancellationToken ct = default)
    {
        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Feedback not found.");
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
