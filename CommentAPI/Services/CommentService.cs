using AutoMapper;
using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Http;

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

    public async Task<CommentDto> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.CommentNotFound,
                ApiMessages.CommentNotFound);
        }

        return _mapper.Map<CommentDto>(entity);
    }

    public async Task<CommentDto> CreateAsync(CreateCommentDto dto)
    {
        if (!await _repository.PostExistsAsync(dto.PostId))
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
        }

        if (!await _repository.UserExistsAsync(dto.UserId))
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.UserNotFound,
                ApiMessages.UserNotFound);
        }

        if (dto.ParentId is not null)
        {
            var parentExists = await _repository.ParentExistsAsync(dto.ParentId.Value, dto.PostId);
            if (!parentExists)
            {
                throw new ApiException(
                    StatusCodes.Status400BadRequest,
                    ApiErrorCodes.CommentParentInvalid,
                    ApiMessages.CommentParentInvalid);
            }
        }

        var entity = _mapper.Map<Comment>(dto);
        entity.Id = Guid.NewGuid();
        entity.CreatedAt = DateTime.UtcNow;

        await _repository.AddAsync(entity);
        await _repository.SaveChangesAsync();

        return _mapper.Map<CommentDto>(entity);
    }

    public async Task UpdateAsync(Guid id, UpdateCommentDto dto)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.CommentNotFound,
                ApiMessages.CommentNotFound);
        }

        if (!await _repository.PostExistsAsync(entity.PostId))
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
        }

        entity.Content = dto.Content;
        _repository.Update(entity);
        await _repository.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.CommentNotFound,
                ApiMessages.CommentNotFound);
        }

        if (!await _repository.PostExistsAsync(entity.PostId))
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
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
    }

    public async Task<List<CommentDto>> GetFlatByPostIdAsync(Guid postId)
    {
        await EnsurePostExistsAsync(postId);
        var entities = await _repository.GetByPostIdAsync(postId);
        return entities.Select(_mapper.Map<CommentDto>).ToList();
    }

    public async Task<List<CommentTreeDto>> GetTreeByPostIdAsync(Guid postId)
    {
        await EnsurePostExistsAsync(postId);
        var flat = await _repository.GetByPostIdAsync(postId);
        return BuildTreeFromComments(flat);
    }

    public async Task<List<CommentFlatDto>> GetCteFlatByPostIdAsync(Guid postId)
    {
        await EnsurePostExistsAsync(postId);
        return await _repository.GetTreeRowsByCteAsync(postId);
    }

    public async Task<List<CommentTreeDto>> GetCteTreeByPostIdAsync(Guid postId)
    {
        await EnsurePostExistsAsync(postId);
        var flat = await _repository.GetTreeRowsByCteAsync(postId);
        return BuildTreeFromFlatDtosForOnePost(flat);
    }

    public async Task<List<CommentDto>> GetAllFlatAsync()
    {
        var entities = await _repository.GetAllAsync();
        return entities
            .OrderBy(x => x.PostId)
            .ThenBy(x => x.CreatedAt)
            .ThenBy(x => x.Id)
            .Select(_mapper.Map<CommentDto>)
            .ToList();
    }

    public async Task<List<CommentTreeDto>> GetAllTreeAsync()
    {
        var entities = await _repository.GetAllAsync();
        return BuildForestFromComments(entities);
    }

    public async Task<List<CommentFlatDto>> GetAllCteFlatAsync()
    {
        return await _repository.GetTreeRowsByCteAllAsync();
    }

    public async Task<List<CommentTreeDto>> GetAllCteTreeAsync()
    {
        var flat = await _repository.GetTreeRowsByCteAllAsync();
        return BuildForestFromFlatDtos(flat);
    }

    public async Task<List<CommentFlatDto>> GetFlattenedFromEfAsync()
    {
        var forest = await GetAllTreeAsync();
        return FlattenForestPreorder(forest);
    }

    public Task<List<CommentFlatDto>> GetFlattenedForestAsync()
    {
        return GetFlattenedFromEfAsync();
    }

    public async Task<List<CommentFlatDto>> GetFlattenedTreeByPostIdAsync(Guid postId)
    {
        var tree = await GetTreeByPostIdAsync(postId);
        return FlattenForestPreorder(tree);
    }

    public async Task<List<CommentFlatDto>> GetFlattenedFromCteAsync()
    {
        return await _repository.GetTreeRowsByCteAllAsync();
    }

    private async Task EnsurePostExistsAsync(Guid postId)
    {
        if (!await _repository.PostExistsAsync(postId))
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.PostNotFound,
                ApiMessages.PostNotFound);
        }
    }

    private static List<CommentTreeDto> BuildForestFromComments(List<Comment> comments)
    {
        return comments
            .GroupBy(c => c.PostId)
            .OrderBy(g => g.Key)
            .SelectMany(g => BuildTreeFromComments(g.ToList()))
            .ToList();
    }

    private static List<CommentTreeDto> BuildForestFromFlatDtos(List<CommentFlatDto> rows)
    {
        return rows
            .GroupBy(r => r.PostId)
            .OrderBy(g => g.Key)
            .SelectMany(g => BuildTreeFromFlatDtosForOnePost(g.ToList()))
            .ToList();
    }

    private static List<CommentTreeDto> BuildTreeFromComments(List<Comment> comments)
    {
        var lookup = comments.ToDictionary(
            x => x.Id,
            x => new CommentTreeDto
            {
                Id = x.Id,
                Content = x.Content,
                CreatedAt = x.CreatedAt,
                PostId = x.PostId,
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

            if (CreatesCycleFromComments(comment.Id, comments))
            {
                roots.Add(node);
                continue;
            }

            parent.Children.Add(node);
        }

        return roots;
    }

    private static bool CreatesCycleFromComments(Guid commentId, List<Comment> comments)
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

    private static List<CommentTreeDto> BuildTreeFromFlatDtosForOnePost(List<CommentFlatDto> rows)
    {
        var lookup = rows.ToDictionary(
            x => x.Id,
            x => new CommentTreeDto
            {
                Id = x.Id,
                Content = x.Content,
                CreatedAt = x.CreatedAt,
                PostId = x.PostId,
                ParentId = x.ParentId
            });

        var roots = new List<CommentTreeDto>();

        foreach (var row in rows.OrderBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id))
        {
            var node = lookup[row.Id];

            if (row.ParentId is null)
            {
                roots.Add(node);
                continue;
            }

            if (!lookup.TryGetValue(row.ParentId.Value, out var parent))
            {
                roots.Add(node);
                continue;
            }

            if (CreatesCycleFromFlatRows(row.Id, rows))
            {
                roots.Add(node);
                continue;
            }

            parent.Children.Add(node);
        }

        return roots;
    }

    private static List<CommentFlatDto> FlattenForestPreorder(List<CommentTreeDto> roots)
    {
        var result = new List<CommentFlatDto>();
        foreach (var root in roots
                     .OrderBy(r => r.PostId)
                     .ThenBy(r => r.CreatedAt)
                     .ThenBy(r => r.Id))
        {
            VisitPreorder(root, 0, result);
        }

        return result;
    }

    private static void VisitPreorder(CommentTreeDto node, int level, List<CommentFlatDto> sink)
    {
        sink.Add(new CommentFlatDto
        {
            Id = node.Id,
            Content = node.Content,
            CreatedAt = node.CreatedAt,
            PostId = node.PostId,
            ParentId = node.ParentId,
            Level = level
        });

        foreach (var child in node.Children.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id))
        {
            VisitPreorder(child, level + 1, sink);
        }
    }

    private static bool CreatesCycleFromFlatRows(Guid commentId, List<CommentFlatDto> rows)
    {
        var parentById = rows.ToDictionary(x => x.Id, x => x.ParentId);
        if (!parentById.ContainsKey(commentId))
        {
            return false;
        }

        var visited = new HashSet<Guid>();
        Guid? parentId = parentById[commentId];

        while (parentId is not null)
        {
            if (parentId == commentId)
            {
                return true;
            }

            if (!visited.Add(parentId.Value))
            {
                return true;
            }

            if (!parentById.TryGetValue(parentId.Value, out var nextParent))
            {
                return false;
            }

            parentId = nextParent;
        }

        return false;
    }
}
