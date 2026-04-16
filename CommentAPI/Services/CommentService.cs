using AutoMapper;
using CommentAPI.DTOs.Comments;
using CommentAPI.Entities;
using CommentAPI.Repositories;

namespace CommentAPI.Services;

public class CommentService : ICommentService
{
    private readonly ICommentRepository _repository;
    private readonly IMapper _mapper;

    public CommentService(ICommentRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<CommentDto>> GetAllAsync()
    {
        var entities = await _repository.GetAllAsync();
        return entities.Select(_mapper.Map<CommentDto>).ToList();
    }

    public async Task<CommentDto?> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        return entity is null ? null : _mapper.Map<CommentDto>(entity);
    }

    public async Task<CommentDto?> CreateAsync(CreateCommentDto dto)
    {
        if (!await _repository.PostExistsAsync(dto.PostId))
        {
            return null;
        }

        if (!await _repository.UserExistsAsync(dto.UserId))
        {
            return null;
        }

        if (dto.ParentId is not null)
        {
            var parentExists = await _repository.ParentExistsAsync(dto.ParentId.Value, dto.PostId);
            if (!parentExists)
            {
                return null;
            }
        }

        var entity = _mapper.Map<Comment>(dto);
        entity.Id = Guid.NewGuid();
        entity.CreatedAt = DateTime.UtcNow;

        await _repository.AddAsync(entity);
        await _repository.SaveChangesAsync();

        return _mapper.Map<CommentDto>(entity);
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateCommentDto dto)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            return false;
        }

        entity.Content = dto.Content;
        _repository.Update(entity);
        await _repository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            return false;
        }

        var allCommentsInPost = await _repository.GetByPostIdAsync(entity.PostId);
        var toDelete = new HashSet<Guid> { entity.Id };
        var queue = new Queue<Guid>();
        queue.Enqueue(entity.Id);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            var children = allCommentsInPost.Where(x => x.ParentId == currentId).Select(x => x.Id).ToList();

            foreach (var childId in children)
            {
                if (toDelete.Add(childId))
                {
                    queue.Enqueue(childId);
                }
            }
        }

        var entitiesToRemove = allCommentsInPost.Where(x => toDelete.Contains(x.Id)).ToList();
        foreach (var comment in entitiesToRemove)
        {
            _repository.Remove(comment);
        }

        await _repository.SaveChangesAsync();
        return true;
    }

    public async Task<List<CommentDto>> GetFlatByPostIdAsync(Guid postId)
    {
        var entities = await _repository.GetByPostIdAsync(postId);
        return entities.Select(_mapper.Map<CommentDto>).ToList();
    }

    public async Task<List<CommentTreeDto>> GetTreeByPostIdAsync(Guid postId)
    {
        var flat = await _repository.GetByPostIdAsync(postId);
        return BuildTree(flat);
    }

    public async Task<List<CommentFlatDto>> GetTreeByPostIdCteAsync(Guid postId)
    {
        return await _repository.GetTreeRowsByCteAsync(postId);
    }

    private static List<CommentTreeDto> BuildTree(List<Comment> comments)
    {
        var lookup = comments.ToDictionary(
            x => x.Id,
            x => new CommentTreeDto
            {
                Id = x.Id,
                Content = x.Content,
                CreatedAt = x.CreatedAt,
                ParentId = x.ParentId
            });

        var roots = new List<CommentTreeDto>();

        foreach (var comment in comments.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id))
        {
            var node = lookup[comment.Id];

            if (comment.ParentId is null)
            {
                roots.Add(node);
                continue;
            }

            if (!lookup.TryGetValue(comment.ParentId.Value, out var parent))
            {
                roots.Add(node);
                continue;
            }

            if (CreatesCycle(comment.Id, comments))
            {
                roots.Add(node);
                continue;
            }

            parent.Children.Add(node);
        }

        return roots;
    }

    private static bool CreatesCycle(Guid commentId, List<Comment> comments)
    {
        var map = comments.ToDictionary(x => x.Id, x => x);
        var visited = new HashSet<Guid>();
        Guid? currentParentId = map[commentId].ParentId;

        while (currentParentId is not null)
        {
            if (!map.TryGetValue(currentParentId.Value, out var current))
            {
                return false;
            }

            if (current.Id == commentId)
            {
                return true;
            }

            if (!visited.Add(current.Id))
            {
                return true;
            }

            currentParentId = current.ParentId;
        }

        return false;
    }
}
