using ApartmentAPI.V1.DTOs;
using ApartmentAPI.Entities;
using ApartmentAPI.Repositories;
using AutoMapper;
using Microsoft.AspNetCore.Identity;

namespace ApartmentAPI.Services;

// Nghiệp vụ file đính kèm: CRUD + theo user / feedback / scope / post.
public interface IAttachmentService
{
    Task<List<AttachmentDto>> GetAllAsync(CancellationToken ct = default);
    Task<AttachmentDto> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<List<AttachmentDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task<List<AttachmentDto>> GetByFeedbackIdAsync(Guid feedbackId, CancellationToken ct = default);
    Task<List<AttachmentDto>> GetByPostIdAsync(Guid postId, CancellationToken ct = default);
    Task<List<AttachmentDto>> GetByScopeAsync(AttachmentScope scope, CancellationToken ct = default);
    Task<AttachmentDto> CreateAsync(CreateAttachmentDto dto, CancellationToken ct = default);
    Task UpdateAsync(Guid id, UpdateAttachmentDto dto, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default);
}

// CRUD Attachment: FK tuỳ chọn UserId / FeedbackId — chỉ kiểm tra khi có giá trị.
public sealed class AttachmentService : IAttachmentService
{
    private readonly IAttachmentRepository _repository;
    private readonly UserManager<User> _users;
    private readonly IFeedbackRepository _feedbacks;
    private readonly IMapper _mapper;

    public AttachmentService(
        IAttachmentRepository repository,
        UserManager<User> users,
        IFeedbackRepository feedbacks,
        IMapper mapper)
    {
        _repository = repository;
        _users = users;
        _feedbacks = feedbacks;
        _mapper = mapper;
    }

    public async Task<List<AttachmentDto>> GetAllAsync(CancellationToken ct = default)
    {
        var list = await _repository.GetAllAsync(ct);
        return _mapper.Map<List<AttachmentDto>>(list);
    }

    public async Task<AttachmentDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await _repository.GetByIdAsync(id, ct);
        if (entity is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Attachment not found.");
        return _mapper.Map<AttachmentDto>(entity);
    }

    public async Task<List<AttachmentDto>> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
    {
        var list = await _repository.GetByUserIdAsync(userId, ct);
        return _mapper.Map<List<AttachmentDto>>(list);
    }

    public async Task<List<AttachmentDto>> GetByFeedbackIdAsync(Guid feedbackId, CancellationToken ct = default)
    {
        var list = await _repository.GetByFeedbackIdAsync(feedbackId, ct);
        return _mapper.Map<List<AttachmentDto>>(list);
    }

    public async Task<List<AttachmentDto>> GetByPostIdAsync(Guid postId, CancellationToken ct = default)
    {
        var list = await _repository.GetByPostIdAsync(postId, ct);
        return _mapper.Map<List<AttachmentDto>>(list);
    }

    public async Task<List<AttachmentDto>> GetByScopeAsync(AttachmentScope scope, CancellationToken ct = default)
    {
        var list = await _repository.GetByScopeAsync(scope, ct);
        return _mapper.Map<List<AttachmentDto>>(list);
    }

    public async Task<AttachmentDto> CreateAsync(CreateAttachmentDto dto, CancellationToken ct = default)
    {
        await EnsureOptionalFkAsync(dto.UserId, dto.FeedbackId, ct);

        var entity = _mapper.Map<Attachment>(dto);
        await _repository.AddAsync(entity, ct);
        await _repository.SaveChangesAsync(ct);
        return _mapper.Map<AttachmentDto>(entity);
    }

    public async Task UpdateAsync(Guid id, UpdateAttachmentDto dto, CancellationToken ct = default)
    {
        await EnsureOptionalFkAsync(dto.UserId, dto.FeedbackId, ct);

        var tracked = await _repository.GetByIdTrackedAsync(id, ct);
        if (tracked is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Attachment not found.");
        _mapper.Map(dto, tracked);
        _repository.Update(tracked);
        await _repository.SaveChangesAsync(ct);
    }

    public async Task SoftDeleteAsync(Guid id, string? deletedBy, CancellationToken ct = default)
    {
        await _repository.SoftDeleteAsync(id, deletedBy, ct);
        await _repository.SaveChangesAsync(ct);
    }

    private async Task EnsureOptionalFkAsync(Guid? userId, Guid? feedbackId, CancellationToken ct)
    {
        if (userId is { } u && await _users.FindByIdAsync(u.ToString()) is null)
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "User not found.");
        if (feedbackId is { } f && !await _feedbacks.ExistsAsync(f, ct))
            throw ApiException.NotFound(ApiErrorCodes.NotFound, "Feedback not found.");
    }
}
