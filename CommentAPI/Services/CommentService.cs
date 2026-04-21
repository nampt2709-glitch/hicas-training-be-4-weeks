using AutoMapper;
using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using Microsoft.AspNetCore.Http;

namespace CommentAPI.Services;

/// <summary>
/// Nghiệp vụ comment: gắn cache phân tán, gọi repository (EF/SQL), dựng cây và danh sách phẳng trong bộ nhớ.
/// Header <c>X-Sql-Query-Count</c> chỉ phản ánh truy vấn SQL khi thực sự gọi repository; thao tác cache HIT không tăng số query.
/// </summary>
public class CommentService : ICommentService
{
    /// <summary>Ngưỡng an toàn: nếu một lần nạp quá nhiều comment để tính Level, bỏ qua độ sâu (gán 0) tránh chi phí O(n) lớn.</summary>
    private const int MaxCommentsToComputeLevels = 25_000;

    private readonly ICommentRepository _repository;
    private readonly IMapper _mapper;
    private readonly IEntityResponseCache _cache;

    public CommentService(ICommentRepository repository, IMapper mapper, IEntityResponseCache cache)
    {
        _repository = repository;
        _mapper = mapper;
        _cache = cache;
    }

    /// <summary>
    /// Danh sách phân trang DTO: đọc cache → nếu miss thì <see cref="ICommentRepository.GetPagedAsync"/> (2 query) → map và ghi cache.
    /// </summary>
    public async Task<PagedResult<CommentDto>> GetAllPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = EntityCacheKeys.CommentsAll(page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            // Trả từ Redis/memory — không gọi SQL trong nhánh này.
            return cached;
        }

        // Miss: repository thực hiện COUNT + SELECT trang (xem chú thích trong CommentRepository.GetPagedAsync).
        var (items, total) = await _repository.GetPagedAsync(page, pageSize, cancellationToken);
        var result = new PagedResult<CommentDto>
        {
            Items = items.Select(_mapper.Map<CommentDto>).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    /// <summary>Tìm theo nội dung toàn cục: cache theo băm term + phân trang; miss → SearchByContentPagedAsync repository.</summary>
    public async Task<PagedResult<CommentDto>> SearchByContentPagedAsync(
        string? content,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var term = RequireSearchTerm(content);
        var cacheKey = EntityCacheKeys.CommentsSearchContent(EntityCacheHash.SearchTerm(term), page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (items, total) = await _repository.SearchByContentPagedAsync(term, page, pageSize, cancellationToken);
        var result = new PagedResult<CommentDto>
        {
            Items = items.Select(_mapper.Map<CommentDto>).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    /// <summary>
    /// Hai bước SQL có thể xảy ra: <see cref="ICommentRepository.PostExistsAsync"/> (Any) rồi projection một comment (FirstOrDefault).
    /// </summary>
    public async Task<CommentDto> GetByIdInPostAsync(Guid postId, Guid commentId, CancellationToken cancellationToken = default)
    {
        // Đảm bảo post tồn tại rồi đọc đúng một comment thuộc post đó (không dùng cache theo id toàn cục).
        await EnsurePostExistsAsync(postId);
        var dto = await _repository.GetByIdForReadInPostAsync(postId, commentId, cancellationToken);
        if (dto is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.CommentNotFound,
                ApiMessages.CommentNotFound);
        }

        return dto;
    }

    /// <summary>Giống search toàn cục nhưng scoped post: EnsurePostExists + SearchByContentInPostPaged (COUNT + SELECT).</summary>
    public async Task<PagedResult<CommentDto>> SearchByContentInPostPagedAsync(
        Guid postId,
        string? content,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var term = RequireSearchTerm(content);
        var cacheKey = EntityCacheKeys.CommentsSearchContentInPost(postId, EntityCacheHash.SearchTerm(term), page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        await EnsurePostExistsAsync(postId);
        var (items, total) = await _repository.SearchByContentInPostPagedAsync(postId, term, page, pageSize, cancellationToken);
        var result = new PagedResult<CommentDto>
        {
            Items = items.Select(_mapper.Map<CommentDto>).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    /// <summary>Một SELECT projection khi cache miss; HIT thì không SQL.</summary>
    public async Task<CommentDto> GetByIdAsync(Guid id)
    {
        var cacheKey = EntityCacheKeys.Comment(id);
        var cached = await _cache.GetJsonAsync<CommentDto>(cacheKey, CancellationToken.None);
        if (cached is not null)
        {
            return cached;
        }

        var dto = await _repository.GetByIdForReadAsync(id, default);
        if (dto is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.CommentNotFound,
                ApiMessages.CommentNotFound);
        }

        await _cache.SetJsonAsync(cacheKey, dto, default);
        return dto;
    }

    /// <summary>
    /// Chuỗi kiểm tra: PostExists, UserExists, tùy chọn ParentExists — mỗi cái một truy vấn Any.
    /// Sau đó Add + SaveChanges (một hoặc nhiều lệnh ghi).
    /// </summary>
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

    /// <summary>GetById tracked, PostExists, cập nhật entity, SaveChanges — xóa cache comment theo id.</summary>
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

        await _cache.RemoveAsync(EntityCacheKeys.Comment(id), default);
    }

    /// <summary>
    /// Xóa cả cây con trong bộ nhớ: GetById, PostExists, GetByPostId (nạp toàn post), rồi Remove nhiều entity + SaveChanges.
    /// Số query phụ thuộc số bản ghi xóa và cách EF batch.
    /// </summary>
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

        var keys = toDelete.Select(EntityCacheKeys.Comment).ToList();
        await _cache.RemoveManyAsync(keys, default);
    }

    /// <summary>Alias của <see cref="GetAllPagedAsync"/> — cùng SQL, khác ý nghĩa route “flat list”.</summary>
    public Task<PagedResult<CommentDto>> GetAllFlatPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return GetAllPagedAsync(page, pageSize, cancellationToken);
    }

    /// <summary>
    /// Cây toàn cục theo trang gốc: GetRootCommentsPaged (COUNT+SELECT gốc), rồi <see cref="BuildSubtreesForRootsAsync"/>
    /// (thêm một SELECT lớn cho mọi comment thuộc các PostId của gốc trang hiện tại).
    /// </summary>
    public async Task<PagedResult<CommentTreeDto>> GetAllTreePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = EntityCacheKeys.CommentsAllTreeFlat(page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<CommentTreeDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (roots, total) = await _repository.GetRootCommentsPagedAsync(page, pageSize, cancellationToken);
        var trees = await BuildSubtreesForRootsAsync(roots, cancellationToken);
        var result = new PagedResult<CommentTreeDto>
        {
            Items = trees,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    /// <summary>
    /// “CteFlat” route demo: thực chất phân trang EF + <see cref="ToCommentFlatDtosAsync"/> (có thể thêm query nạp đủ post để tính Level).
    /// </summary>
    public async Task<PagedResult<CommentFlatDto>> GetAllCteFlatPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = EntityCacheKeys.CommentsAllCteFlat(page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        var (items, total) = await _repository.GetPagedAsync(page, pageSize, cancellationToken);
        var flats = await ToCommentFlatDtosAsync(items, cancellationToken);
        var result = new PagedResult<CommentFlatDto>
        {
            Items = flats,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    /// <summary>Hiện ủy quyền sang cây EF flat — không gọi file SQL CTE.</summary>
    public Task<PagedResult<CommentTreeDto>> GetAllCteTreePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return GetAllTreePagedAsync(page, pageSize, cancellationToken);
    }

    /// <summary>
    /// Làm phẳng preorder cây EF: cùng chi phí query như <see cref="GetAllTreePagedAsync"/>, phần CPU thêm để <see cref="FlattenForestPreorder"/>.
    /// </summary>
    public async Task<PagedResult<CommentFlatDto>> GetFlattenedForestPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = EntityCacheKeys.CommentsAllFlattenEfTree(page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        // Trang = gốc: lấy N comment gốc, dựng cây bằng EF rồi preorder — không gọi CTE.
        var (roots, total) = await _repository.GetRootCommentsPagedAsync(page, pageSize, cancellationToken);
        var trees = await BuildSubtreesForRootsAsync(roots, cancellationToken);
        var flat = FlattenForestPreorder(trees);
        var result = new PagedResult<CommentFlatDto>
        {
            Items = flat,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    /// <summary>
    /// Một lệnh CTE lớn qua <see cref="ICommentRepository.GetTreeRowsByCteAllAsync"/> (ADO + đếm trong middleware),
    /// sau đó toàn bộ xử lý cây/flatten trong RAM; phân trang cắt danh sách phẳng — không thêm SQL.
    /// </summary>
    public async Task<PagedResult<CommentFlatDto>> GetFlattenedFromCtePagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = EntityCacheKeys.CommentsAllFlattenCteTree(page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        // CTE toàn bộ post → cây theo từng post → preorder; phân trang trên tổng số dòng phẳng.
        var allRows = await _repository.GetTreeRowsByCteAllAsync();
        var flatList = BuildGlobalFlatFromCteAllRows(allRows);
        var total = flatList.Count;
        var slice = flatList
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        var result = new PagedResult<CommentFlatDto>
        {
            Items = slice,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    /// <summary>PostExists + GetByPostIdPaged (COUNT+SELECT trong một post).</summary>
    public async Task<PagedResult<CommentDto>> GetFlatByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = EntityCacheKeys.CommentsFlatByPost(postId, page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        await EnsurePostExistsAsync(postId);
        var (items, total) = await _repository.GetByPostIdPagedAsync(postId, page, pageSize, cancellationToken);
        var result = new PagedResult<CommentDto>
        {
            Items = items.Select(_mapper.Map<CommentDto>).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    /// <summary>Giống flat theo post nhưng ánh xạ sang <see cref="CommentFlatDto"/> có Level qua <see cref="ToCommentFlatDtosAsync"/>.</summary>
    public async Task<PagedResult<CommentFlatDto>> GetCteFlatByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = EntityCacheKeys.CommentsCteFlatByPost(postId, page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        await EnsurePostExistsAsync(postId);
        var (items, total) = await _repository.GetByPostIdPagedAsync(postId, page, pageSize, cancellationToken);
        var flats = await ToCommentFlatDtosAsync(items, cancellationToken);
        var result = new PagedResult<CommentFlatDto>
        {
            Items = flats,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    /// <summary>
    /// Gốc phân trang trong post + một lần nạp toàn bộ comment post để dựng cây — có thể tốn bộ nhớ với post lớn.
    /// </summary>
    public async Task<PagedResult<CommentTreeDto>> GetTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = EntityCacheKeys.CommentsTreeByPost(postId, page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<CommentTreeDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        await EnsurePostExistsAsync(postId);
        var (roots, total) = await _repository.GetRootsByPostIdPagedAsync(postId, page, pageSize, cancellationToken);
        var allInPost = await _repository.GetByPostIdAsync(postId);
        var trees = new List<CommentTreeDto>();
        foreach (var root in roots)
        {
            // Lặp lại BuildTreeFromComments cho mỗi gốc — chi phí CPU cao; có thể tối ưu bằng một lần build ngoài vòng lặp.
            var forest = BuildTreeFromComments(allInPost);
            var node = forest.FirstOrDefault(t => t.Id == root.Id);
            if (node is not null)
            {
                trees.Add(node);
            }
        }

        var result = new PagedResult<CommentTreeDto>
        {
            Items = trees,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    public Task<PagedResult<CommentTreeDto>> GetCteTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        return GetTreeByPostIdPagedAsync(postId, page, pageSize, cancellationToken);
    }

    /// <summary>Một lần build cây từ toàn post, lấy subtree các gốc trang, preorder — SQL giống GetTreeByPostId.</summary>
    public async Task<PagedResult<CommentFlatDto>> GetFlattenedTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = EntityCacheKeys.CommentsFlattenedEfTreeByPost(postId, page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        await EnsurePostExistsAsync(postId);
        var (roots, total) = await _repository.GetRootsByPostIdPagedAsync(postId, page, pageSize, cancellationToken);
        var allInPost = await _repository.GetByPostIdAsync(postId);
        var forest = BuildTreeFromComments(allInPost);
        var trees = new List<CommentTreeDto>();
        foreach (var root in roots)
        {
            var node = forest.FirstOrDefault(t => t.Id == root.Id);
            if (node is not null)
            {
                trees.Add(node);
            }
        }

        var flat = FlattenForestPreorder(trees);
        var result = new PagedResult<CommentFlatDto>
        {
            Items = flat,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    /// <summary>
    /// CTE một post (file SQL) + dựng cây từ hàng phẳng + flatten; phân trang trên tổng số dòng phẳng — một ADO query + có thể thêm EnsurePostExists.
    /// </summary>
    public async Task<PagedResult<CommentFlatDto>> GetFlattenedCteTreeByPostIdPagedAsync(
        Guid postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = EntityCacheKeys.CommentsFlattenedCteTree(postId, page, pageSize);
        var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken);
        if (cached is not null)
        {
            return cached;
        }

        await EnsurePostExistsAsync(postId);

        // Lấy toàn bộ hàng phẳng từ CTE SQL cho một post, dựng cây rồi duyệt preorder để Level khớp DFS.
        var cteRows = await _repository.GetTreeRowsByCteAsync(postId);
        var roots = BuildTreeFromFlatDtosForOnePost(cteRows);
        var flatList = FlattenForestPreorder(roots);
        var total = flatList.Count;
        var slice = flatList
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var result = new PagedResult<CommentFlatDto>
        {
            Items = slice,
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<CommentLoadingDemoDto> GetCommentLazyLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await _repository.GetCommentLazyLoadingDemoAsync(id, cancellationToken);
        if (dto is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.CommentNotFound,
                ApiMessages.CommentNotFound);
        }

        return dto;
    }

    /// <inheritdoc />
    public async Task<CommentLoadingDemoDto> GetCommentEagerLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await _repository.GetCommentEagerLoadingDemoAsync(id, cancellationToken);
        if (dto is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.CommentNotFound,
                ApiMessages.CommentNotFound);
        }

        return dto;
    }

    /// <inheritdoc />
    public async Task<CommentLoadingDemoDto> GetCommentExplicitLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await _repository.GetCommentExplicitLoadingDemoAsync(id, cancellationToken);
        if (dto is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.CommentNotFound,
                ApiMessages.CommentNotFound);
        }

        return dto;
    }

    /// <inheritdoc />
    public async Task<CommentLoadingDemoDto> GetCommentProjectionDemoAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var dto = await _repository.GetCommentProjectionDemoAsync(id, cancellationToken);
        if (dto is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.CommentNotFound,
                ApiMessages.CommentNotFound);
        }

        return dto;
    }

    /// <inheritdoc />
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsLazyLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.Normalize(page, pageSize);
        var (items, total) = await _repository.GetCommentsLazyLoadingDemoPagedAsync(p, s, cancellationToken);
        return new PagedResult<CommentLoadingDemoDto>
        {
            Items = items,
            Page = p,
            PageSize = s,
            TotalCount = total
        };
    }

    /// <inheritdoc />
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsEagerLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.Normalize(page, pageSize);
        var (items, total) = await _repository.GetCommentsEagerLoadingDemoPagedAsync(p, s, cancellationToken);
        return new PagedResult<CommentLoadingDemoDto>
        {
            Items = items,
            Page = p,
            PageSize = s,
            TotalCount = total
        };
    }

    /// <inheritdoc />
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsExplicitLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.Normalize(page, pageSize);
        var (items, total) = await _repository.GetCommentsExplicitLoadingDemoPagedAsync(p, s, cancellationToken);
        return new PagedResult<CommentLoadingDemoDto>
        {
            Items = items,
            Page = p,
            PageSize = s,
            TotalCount = total
        };
    }

    /// <inheritdoc />
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsProjectionDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var (p, s) = PaginationQuery.Normalize(page, pageSize);
        var (items, total) = await _repository.GetCommentsProjectionDemoPagedAsync(p, s, cancellationToken);
        return new PagedResult<CommentLoadingDemoDto>
        {
            Items = items,
            Page = p,
            PageSize = s,
            TotalCount = total
        };
    }

    /// <summary>Chuẩn hóa chuỗi tìm kiếm; không có SQL — chỉ validate trước khi gọi repository.</summary>
    private static string RequireSearchTerm(string? raw)
    {
        var t = raw?.Trim();
        if (string.IsNullOrEmpty(t))
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.SearchTermRequired,
                ApiMessages.SearchTermRequired);
        }

        return t;
    }

    /// <summary>
    /// Một truy vấn <see cref="ICommentRepository.GetCommentsForPostsAsync"/> lấy mọi comment thuộc các post của các gốc;
    /// phần còn lại là nhóm và dựng cây trong RAM (không SQL).
    /// </summary>
    private async Task<List<CommentTreeDto>> BuildSubtreesForRootsAsync(
        List<Comment> roots,
        CancellationToken cancellationToken)
    {
        if (roots.Count == 0)
        {
            return new List<CommentTreeDto>();
        }

        var postIds = roots.Select(r => r.PostId).Distinct().ToList();
        var allInPosts = await _repository.GetCommentsForPostsAsync(postIds, cancellationToken);
        var trees = new List<CommentTreeDto>();
        foreach (var root in roots)
        {
            var inPost = allInPosts.Where(c => c.PostId == root.PostId).ToList();
            var forest = BuildTreeFromComments(inPost);
            var node = forest.FirstOrDefault(t => t.Id == root.Id);
            if (node is not null)
            {
                trees.Add(node);
            }
        }

        return trees;
    }

    /// <summary>
    /// Để gán <see cref="CommentFlatDto.Level"/> đúng độ sâu cây: nạp toàn bộ comment các post liên quan (một SELECT),
    /// rồi <see cref="BuildDepthById"/> đệ quy có nhớ trong RAM.
    /// </summary>
    private async Task<List<CommentFlatDto>> ToCommentFlatDtosAsync(
        List<Comment> pageItems,
        CancellationToken cancellationToken)
    {
        if (pageItems.Count == 0)
        {
            return new List<CommentFlatDto>();
        }

        var postIds = pageItems.Select(c => c.PostId).Distinct().ToList();
        var allForPosts = await _repository.GetCommentsForPostsAsync(postIds, cancellationToken);

        if (allForPosts.Count > MaxCommentsToComputeLevels)
        {
            // Quá nhiều bản ghi: tránh đệ quy sâu — gán Level = 0 cho an toàn hiệu năng.
            return pageItems.Select(c => new CommentFlatDto
            {
                Id = c.Id,
                Content = c.Content,
                CreatedAt = c.CreatedAt,
                PostId = c.PostId,
                ParentId = c.ParentId,
                Level = 0
            }).ToList();
        }

        var byPost = allForPosts.GroupBy(x => x.PostId).ToDictionary(g => g.Key, g => g.ToList());
        var depthByPost = byPost.ToDictionary(kv => kv.Key, kv => BuildDepthById(kv.Value));

        var list = new List<CommentFlatDto>(pageItems.Count);
        foreach (var c in pageItems)
        {
            depthByPost[c.PostId].TryGetValue(c.Id, out var lv);
            list.Add(new CommentFlatDto
            {
                Id = c.Id,
                Content = c.Content,
                CreatedAt = c.CreatedAt,
                PostId = c.PostId,
                ParentId = c.ParentId,
                Level = lv
            });
        }

        return list;
    }

    /// <summary>Tính độ sâu từ gốc tới từng Id bằng đệ quy có memo — không truy vấn DB.</summary>
    private static Dictionary<Guid, int> BuildDepthById(List<Comment> inPost)
    {
        var parentById = inPost.ToDictionary(c => c.Id, c => c.ParentId);
        var memo = new Dictionary<Guid, int>();

        int Depth(Guid id)
        {
            if (memo.TryGetValue(id, out var d))
            {
                return d;
            }

            if (!parentById.TryGetValue(id, out var p) || !p.HasValue)
            {
                memo[id] = 0;
                return 0;
            }

            var v = 1 + Depth(p.Value);
            memo[id] = v;
            return v;
        }

        foreach (var c in inPost)
        {
            _ = Depth(c.Id);
        }

        return memo;
    }

    /// <summary>Một truy vấn Any tới bảng Posts.</summary>
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

    /// <summary>
    /// Dựng danh sách gốc và liên kết Children từ danh sách phẳng đã nạp — thuật toán trong RAM, xử lý parent thiếu / chu kỳ.
    /// </summary>
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

    /// <summary>Leo cây parent từ một Id để phát hiện chu kỳ hoặc parent không hợp lệ — không SQL.</summary>
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

    /// <summary>
    /// Sau khi CTE đã trả hàng (SQL đã chạy ở repository): nhóm theo PostId, dựng cây và nối preorder — toàn bộ ở đây là CPU.
    /// </summary>
    private static List<CommentFlatDto> BuildGlobalFlatFromCteAllRows(List<CommentFlatDto> allRows)
    {
        var result = new List<CommentFlatDto>();
        foreach (var group in allRows.GroupBy(r => r.PostId).OrderBy(g => g.Key))
        {
            var roots = BuildTreeFromFlatDtosForOnePost(group.ToList());
            result.AddRange(FlattenForestPreorder(roots));
        }

        return result;
    }

    /// <summary>Dựng cây từ hàng có Level do SQL gán — sắp xếp theo Level trước khi gắn cha-con.</summary>
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

    /// <summary>Duyệt cây theo thứ tự tiền tố (cha trước con); Level tăng dần theo độ sâu DFS.</summary>
    private static List<CommentFlatDto> FlattenForestPreorder(List<CommentTreeDto> roots)
    {
        var result = new List<CommentFlatDto>();
        foreach (var root in roots.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id))
        {
            VisitPreorder(root, 0, result);
        }

        return result;
    }

    /// <summary>Một nút: thêm DTO phẳng rồi đệ quy các con đã sắp xếp theo CreatedAt, Id.</summary>
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

    /// <summary>Kiểm tra chu kỳ trên đồ thị cha được biểu diễn bởi danh sách hàng phẳng CTE.</summary>
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
