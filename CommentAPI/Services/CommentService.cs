using AutoMapper; 
using CommentAPI; 
using CommentAPI.DTOs; 
using CommentAPI.Entities; 
using CommentAPI.Interfaces; 
using Microsoft.AspNetCore.Http; 

namespace CommentAPI.Services; 

public class CommentService : ICommentService // Lớp dịch vụ triển khai ICommentService.
{ // Mở khối thân lớp CommentService.

    private readonly ICommentRepository _repository; // Truy cập EF/SQL.
    private readonly IMapper _mapper; // Ánh xạ Entity ↔ DTO.
    private readonly IEntityResponseCache _cache; // Cache phân tán (Redis/memory).

    // Hàm tạo: tiêm repository, mapper và cache phân tán qua constructor.
    public CommentService(ICommentRepository repository, IMapper mapper, IEntityResponseCache cache) // Constructor tiêm repository, mapper và cache.
    { // Mở khối constructor.
        _repository = repository; // Tiêm repository.
        _mapper = mapper; // Tiêm AutoMapper.
        _cache = cache; // Tiêm cache.
    } // Kết thúc hàm tạo.

    // Thứ tự public bám CommentsController; bổ trợ ICommentService (không có action tương ứng) nằm region riêng trước private.
    #region CommentsController — GET /api/comments, user/{userId}, CRUD

    // Gom GET /api/comments: postId, content, khoảng CreatedAt — thứ tự ưu tiên content → post-only → toàn hệ (một id dùng GET /{id}).
    // [01] Route: GET /api/comments
    public async Task<PagedResult<CommentDto>> GetCommentListAsync( // Một hàm cho nhiều bộ lọc query.
        Guid? postId, // Lọc theo bài (tuỳ chọn).
        string? contentContains, // Tìm theo nội dung (tuỳ chọn).
        bool unpaged, // true = trả hết bản ghi khớp (không Skip/Take).
        int page, // Trang khi unpaged=false.
        int pageSize, // Cỡ trang khi unpaged=false.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetCommentListAsync.
        var hasContent = !string.IsNullOrWhiteSpace(contentContains); // Client có gửi content để search không.
        if (hasContent) // Nhánh tìm theo nội dung (thay route search/by-content).
        { // Mở khối search.
            var term = RequireSearchTerm(contentContains); // Chuẩn hoá term; 400 nếu rỗng.

            if (unpaged) // Trả toàn bộ khớp Contains.
            { // Mở unpaged search.
                List<Comment> entities = postId is { } spid // Có postId → giới hạn trong bài.
                    ? await SearchByContentInPostAllValidatedAsync(spid, term, cancellationToken, createdAtFrom, createdAtTo) // Kiểm tra post + SELECT.
                    : await _repository.SearchCommentsRouteAllAsync(null, term, cancellationToken, createdAtFrom, createdAtTo); // Toàn hệ thống.

                var list = entities.Select(_mapper.Map<CommentDto>).ToList(); // Map sang DTO.
                return ToUnpagedCommentDtoResult(list); // PageSize = số phần tử thực tế.
            } // Kết thúc unpaged search.

            if (postId is { } ppid) // Phân trang trong một post.
            { // Mở khối.
                return await SearchByContentInPostPagedAsync(ppid, term, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Cache + COUNT/SELECT.
            } // Kết thúc in-post paged.

            return await SearchByContentPagedAsync(term, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Toàn hệ phân trang.
        } // Kết thúc nhánh content.

        if (postId is { } onlyPost) // Chỉ lọc post, không search.
        { // Mở khối theo post.
            if (unpaged) // Thay route GET .../post/{id} không phân trang.
            { // Mở khối.
                var fullPostList = await GetAllByPostIdAsync(onlyPost, cancellationToken); // Đã EnsurePostExists bên trong.
                var list = fullPostList // Áp lọc ngày trong RAM (list một post thường nhỏ).
                    .Where(d => (!createdAtFrom.HasValue || d.CreatedAt >= createdAtFrom) && (!createdAtTo.HasValue || d.CreatedAt <= createdAtTo))
                    .ToList(); // Materialize sau lọc.
                return ToUnpagedCommentDtoResult(list); // List đầy đủ một bài (đã lọc ngày).
            } // Kết thúc unpaged theo post.

            return await GetFlatByPostIdPagedAsync(onlyPost, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Phân trang phẳng trong post.
        } // Kết thúc chỉ postId.

        if (unpaged) // Toàn bộ comment mọi bài — không cache (khối lượng lớn).
        { // Mở khối global unpaged.
            var entities = await _repository.GetCommentsRouteAllAsync(null, createdAtFrom, createdAtTo); // SELECT có lọc ngày ở SQL.
            var list = entities.Select(_mapper.Map<CommentDto>).ToList(); // Ánh xạ DTO.
            return ToUnpagedCommentDtoResult(list); // Trả PagedResult “giả” một trang đủ dài.
        } // Kết thúc global unpaged.

        return await GetAllPagedAsync(page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Mặc định: phân trang toàn hệ.
    } // Kết thúc GetCommentListAsync.

    // Đọc một comment theo Id với cache; 404 nếu không có — cùng thứ tự GET /api/comments/{id} trong CommentsController.
    // [02] Route: GET /api/comments/{id}
    public async Task<CommentDto> GetByIdAsync(Guid id) // Khóa chính comment.
    { // Mở khối GetByIdAsync.
        var cacheKey = EntityCacheKeys.Comment(id); // Khóa theo Guid comment.
        var cached = await _cache.GetJsonAsync<CommentDto>(cacheKey, CancellationToken.None); // Đọc cache; CancellationToken.None cố định như code cũ.
        if (cached is not null) // Đã có trong cache.
        { // Mở khối.
            return cached; // Trả ngay không truy vấn DB.
        } // Hết nhánh cache.

        var dto = await _repository.GetCommentByIdRouteReadAsync(id, default); // SELECT projection một comment.
        if (dto is null) // Không tồn tại.
        { // Mở khối.
            throw new ApiException( // Ném ngoại lệ thống nhất.
                StatusCodes.Status404NotFound, // HTTP 404.
                ApiErrorCodes.CommentNotFound, // Mã lỗi.
                ApiMessages.CommentNotFound); // Thông điệp.
        } // Hết nhánh null.

        await _cache.SetJsonAsync(cacheKey, dto, default); // Lưu DTO vào cache.
        return dto; // Trả DTO cho caller.
    } // Kết thúc GetByIdAsync.

    // Phân trang comment của một user (UserId); 404 nếu user không tồn tại.
    // [03] Route: GET /api/comments/user/{userId}
    public async Task<PagedResult<CommentDto>> GetCommentsByUserIdPagedAsync( // List by author.
        Guid userId, // Tác giả.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetCommentsByUserIdPagedAsync.
        await EnsureUserExistsAsync(userId); // User phải có trong DB.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        { // Mở khối cache.
            var cacheKey = EntityCacheKeys.CommentsByUser(userId, page, pageSize); // Khóa theo user + trang.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken); // Đọc cache.
            if (cached is not null) // Hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Kết thúc hit.
        } // Kết thúc nhánh cache.

        var (items, total) = await _repository.GetCommentsByUserRoutePagedAsync(userId, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // DB.
        var result = new PagedResult<CommentDto> // Gói phân trang.
        { // Mở khối.
            Items = items.Select(_mapper.Map<CommentDto>).ToList(), // Map DTO.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ.
            TotalCount = total // Tổng.
        }; // Kết thúc initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ set cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsByUser(userId, page, pageSize), result, cancellationToken); // Ghi cache.
        } // Kết thúc set.

        return result; // Trả controller.
    } // Kết thúc GetCommentsByUserIdPagedAsync.

    // Tạo comment mới sau khi kiểm tra post, user và parent hợp lệ.
    // [04] Route: POST /api/comments
    public async Task<CommentDto> CreateAsync(CreateCommentDto dto) // Payload tạo mới.
    { // Mở khối CreateAsync.
        if (!await _repository.PostExistsAsync(dto.PostId)) // Any trên Posts.
        { // Mở khối.
            throw new ApiException( // Post không tồn tại.
                StatusCodes.Status404NotFound, // 404.
                ApiErrorCodes.PostNotFound, // Mã post.
                ApiMessages.PostNotFound); // Thông báo.
        } // Hết kiểm tra post.

        if (!await _repository.UserExistsAsync(dto.UserId)) // Any trên Users.
        { // Mở khối.
            throw new ApiException( // User không tồn tại.
                StatusCodes.Status404NotFound, // 404.
                ApiErrorCodes.UserNotFound, // Mã user.
                ApiMessages.UserNotFound); // Thông báo.
        } // Hết kiểm tra user.

        if (dto.ParentId is not null) // Có chỉ định cha.
        { // Mở khối.
            var parentExists = await _repository.ParentExistsAsync(dto.ParentId.Value, dto.PostId); // Any comment cha cùng post.
            if (!parentExists) // Cha không hợp lệ.
            { // Mở khối.
                throw new ApiException( // 400 nghiệp vụ.
                    StatusCodes.Status400BadRequest, // HTTP 400.
                    ApiErrorCodes.CommentParentInvalid, // Mã parent.
                    ApiMessages.CommentParentInvalid); // Thông báo.
            } // Hết nhánh parent invalid.
        } // Hết nhánh có ParentId.

        var entity = _mapper.Map<Comment>(dto); // DTO → entity trong RAM.
        entity.Id = Guid.NewGuid(); // Sinh Id mới.
        entity.CreatedAt = DateTime.UtcNow; // Ghi mốc thời gian UTC.

        await _repository.AddAsync(entity); // Đánh dấu Added trong context.
        await _repository.SaveChangesAsync(); // INSERT xuống SQL.

        return _mapper.Map<CommentDto>(entity); // Trả DTO sau khi đã có Id trong RAM (không SELECT lại).
    } // Kết thúc CreateAsync.

    // Người dùng: chỉ tác giả (UserId trùng JWT) sửa nội dung; không đổi cây hay post.
    // [05] Route: PUT /api/comments/{id}
    public async Task UpdateAsAuthorAsync(Guid id, UpdateCommentDto dto, Guid currentUserId) // id comment, payload, user hiện tại.
    { // Mở khối UpdateAsAuthorAsync.
        var entity = await _repository.GetByIdAsync(id); // Nạp entity tracked.
        if (entity is null) // Không có bản ghi.
        { // Mở khối.
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // Mã HTTP.
                ApiErrorCodes.CommentNotFound, // Mã nghiệp vụ.
                ApiMessages.CommentNotFound); // Thông điệp.
        } // Hết null.

        if (entity.UserId != currentUserId) // Không phải tác giả.
        { // Mở khối.
            throw new ApiException( // 403.
                StatusCodes.Status403Forbidden, // Cấm.
                ApiErrorCodes.NotResourceAuthor, // Mã quyền.
                ApiMessages.NotResourceAuthor); // Thông điệp.
        } // Hết kiểm tra tác giả.

        if (!await _repository.PostExistsAsync(entity.PostId)) // Post đã mất (nhất quán logic).
        { // Mở khối.
            throw new ApiException( // 404 post.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.PostNotFound, // Mã.
                ApiMessages.PostNotFound); // Thông báo.
        } // Hết kiểm tra post.

        entity.Content = dto.Content; // Cập nhật nội dung.
        _repository.Update(entity); // Đánh dấu Modified.
        await _repository.SaveChangesAsync(); // Ghi DB.

        await _cache.RemoveAsync(EntityCacheKeys.Comment(id), default); // Vô hiệu cache theo Id.
    } // Kết thúc UpdateAsAuthorAsync.

    // Quản trị: cập nhật đủ trường; chuyển post cập nhật PostId cả cây con; chặn parent tạo chu trình hoặc sai post.
    // [06] Route: PUT /api/admin/comments/{id}
    public async Task UpdateAsAdminAsync(Guid id, AdminUpdateCommentDto dto) // id gốc và payload admin.
    { // Mở khối UpdateAsAdminAsync.
        if (!await _repository.UserExistsAsync(dto.UserId)) // User đích phải tồn tại.
        { // Mở khối.
            throw new ApiException( // 404 user.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.UserNotFound, // Mã.
                ApiMessages.UserNotFound); // Thông báo.
        } // Hết kiểm tra user.

        if (!await _repository.PostExistsAsync(dto.PostId)) // Post đích phải tồn tại.
        { // Mở khối.
            throw new ApiException( // 404 post.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.PostNotFound, // Mã.
                ApiMessages.PostNotFound); // Thông báo.
        } // Hết kiểm tra post.

        var root = await _repository.GetByIdAsync(id); // Nạp nút gốc cần sửa.
        if (root is null) // Không có comment.
        { // Mở khối.
            throw new ApiException( // 404 comment.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.CommentNotFound, // Mã.
                ApiMessages.CommentNotFound); // Thông báo.
        } // Hết null root.

        // Lấy toàn bộ comment phẳng của bài cũ (AsNoTracking) để suy ra tập Id cây con qua BFS.
        var inOldFlat = await _repository.GetCommentsRouteAllAsync(root.PostId); // List comment một post.
        var subtree = BuildSubtreeIdSet(inOldFlat, root.Id); // Tập Id gốc + hậu duệ.

        if (dto.ParentId is { } newParentId) // Có gán cha mới (pattern deconstruct).
        { // Mở khối.
            // Không cho comment làm cha chính nó; tránh cấu hình bất hợp lệ trước khi gọi SQL.
            if (newParentId == id) // Tự tham chiếu.
            { // Mở khối.
                throw new ApiException( // 400 chu trình.
                    StatusCodes.Status400BadRequest, // HTTP.
                    ApiErrorCodes.CommentReparentCausesCycle, // Mã.
                    ApiMessages.CommentReparentCausesCycle); // Thông báo.
            } // Hết self-parent.

            // Cha không được là nút đang sửa hay hậu duệ; chặn chu trình trên cây.
            if (subtree.Contains(newParentId)) // Cha nằm trong cây con của chính nó.
            { // Mở khối.
                throw new ApiException( // 400.
                    StatusCodes.Status400BadRequest, // HTTP.
                    ApiErrorCodes.CommentReparentCausesCycle, // Mã.
                    ApiMessages.CommentReparentCausesCycle); // Thông báo.
            } // Hết kiểm tra cycle.

            var parent = await _repository.GetByIdAsync(newParentId); // Nạp entity cha.
            if (parent is null) // Cha không tồn tại.
            { // Mở khối.
                // Cha không tồn tại: không tạo mồ côi hoặc tham chiếu lạc hướng.
                throw new ApiException( // 400.
                    StatusCodes.Status400BadRequest, // HTTP.
                    ApiErrorCodes.CommentParentInvalid, // Mã.
                    ApiMessages.CommentParentInvalid); // Thông báo.
            } // Hết null parent.

            // Cha phải cùng PostId với cấu hình đích (cùng bài viết).
            if (parent.PostId != dto.PostId) // Cha thuộc bài khác.
            { // Mở khối.
                throw new ApiException( // 400 sai post.
                    StatusCodes.Status400BadRequest, // HTTP.
                    ApiErrorCodes.CommentParentWrongPost, // Mã.
                    ApiMessages.CommentParentWrongPost); // Thông báo.
            } // Hết kiểm tra cùng post.
        } // Hết nhánh có ParentId mới.

        var oldPostId = root.PostId; // Ghi PostId cũ để so sánh và vô hiệu cache.
        if (oldPostId != dto.PostId) // Di chuyển cây sang bài khác.
        { // Mở khối.
            // Cập nhật PostId đồng loạt cho mọi entity tracked thuộc subtree.
            var tracked = await _repository.GetCommentsByPostTrackedForAdminRouteAsync(oldPostId, default); // Nạp tracked toàn post cũ.
            foreach (var c in tracked) // Duyệt từng entity.
            { // Mở khối.
                if (subtree.Contains(c.Id)) // Thuộc cây con đang chuyển.
                { // Mở khối.
                    c.PostId = dto.PostId; // Gán post mới.
                } // Hết nhánh thuộc subtree.
            } // Hết foreach.
        } // Hết chuyển post.

        root.Content = dto.Content; // Nội dung mới.
        root.UserId = dto.UserId; // Chủ sở hữu mới.
        root.PostId = dto.PostId; // Post (đã đồng bộ subtree nếu chuyển).
        root.ParentId = dto.ParentId; // Cha (nullable).
        _repository.Update(root); // Đánh dấu sửa gốc.
        await _repository.SaveChangesAsync(); // Flush thay đổi.

        var cacheKeys = new List<string> { EntityCacheKeys.Post(oldPostId) }; // Luôn xóa cache post cũ.
        if (oldPostId != dto.PostId) // Nếu đổi post.
        { // Mở khối.
            cacheKeys.Add(EntityCacheKeys.Post(dto.PostId)); // Thêm cache post mới.
        } // Hết nhánh đổi post.

        cacheKeys.AddRange(subtree.Select(EntityCacheKeys.Comment)); // Thêm khóa từng comment trong cây.
        await _cache.RemoveManyAsync(cacheKeys, default); // Xóa hàng loạt.
    } // Kết thúc UpdateAsAdminAsync.

    // Xóa một comment và toàn bộ hậu duệ trong cùng post; vô hiệu cache liên quan.
    // [07] Route: DELETE /api/comments/{id}
    public async Task DeleteAsync(Guid id) // Id comment gốc cần xóa.
    { // Mở khối DeleteAsync.
        var entity = await _repository.GetByIdAsync(id); // Nạp entity gốc cần xóa.
        if (entity is null) // Không tồn tại.
        { // Mở khối.
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.CommentNotFound, // Mã.
                ApiMessages.CommentNotFound); // Thông báo.
        } // Hết null.

        if (!await _repository.PostExistsAsync(entity.PostId)) // Kiểm tra post (logic nhất quán với code gốc).
        { // Mở khối.
            throw new ApiException( // 404 post.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.PostNotFound, // Mã.
                ApiMessages.PostNotFound); // Thông báo.
        } // Hết kiểm tra post.

        var allCommentsInPost = await _repository.GetCommentsRouteAllAsync(entity.PostId); // SELECT toàn comment của post vào RAM.
        var toDelete = new HashSet<Guid> { entity.Id }; // Tập Id sẽ xóa, khởi tạo với gốc.
        var queue = new Queue<Guid>(); // Hàng đợi BFS các Id con.
        queue.Enqueue(entity.Id); // Bắt đầu từ comment gốc.

        while (queue.Count > 0) // Lặp cho đến khi duyệt hết cây con.
        { // Mở khối.
            var currentId = queue.Dequeue(); // Lấy Id đang xử lý.
            var children = allCommentsInPost // LINQ to Objects trên list đã nạp.
                .Where(x => x.ParentId == currentId) // Tìm mọi comment có cha = currentId.
                .Select(x => x.Id) // Chỉ lấy Id con.
                .ToList(); // Materialize danh sách Id con.

            foreach (var childId in children) // Duyệt từng con.
            { // Mở khối.
                if (toDelete.Add(childId)) // Nếu Id chưa có trong tập (Add trả true).
                { // Mở khối.
                    queue.Enqueue(childId); // Đưa con vào hàng đợi để duyệt tiếp cháu.
                } // Hết nhánh Id mới.
            } // Hết foreach children.
        } // Hết while BFS.

        var entitiesToRemove = allCommentsInPost // Lọc entity cần Remove.
            .Where(x => toDelete.Contains(x.Id)) // Chỉ những Id đã gom.
            .ToList(); // List để foreach Remove.

        foreach (var comment in entitiesToRemove) // Duyệt entity cần xóa.
        { // Mở khối.
            _repository.Remove(comment); // Đánh dấu Deleted từng entity.
        } // Hết foreach Remove.

        await _repository.SaveChangesAsync(); // Gửi DELETE (hoặc batch) xuống SQL.

        var keys = toDelete.Select(EntityCacheKeys.Comment).ToList(); // Sinh danh sách khóa cache cho mọi Id đã xóa.
        await _cache.RemoveManyAsync(keys, default); // Xóa cache theo loạt.
    } // Kết thúc DeleteAsync.

    #endregion

    #region CommentsController — GET flat / cte / tree (post trước, toàn hệ sau, như nhánh if trong controller)

    // [08] Route: GET /api/comments/flat
    public async Task<PagedResult<CommentDto>> GetFlatRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null)
    {
        // Điều phối một hàm route duy nhất: có postId thì nhánh theo post, ngược lại toàn hệ.
        return postId is { } pid
            // Nhánh có post: phân trang phẳng trong phạm vi một bài.
            ? await GetFlatByPostIdPagedAsync(pid, page, pageSize, cancellationToken, createdAtFrom, createdAtTo)
            // Nhánh không post: phân trang phẳng toàn hệ.
            : await GetAllFlatPagedAsync(page, pageSize, cancellationToken, createdAtFrom, createdAtTo);
    }

    // Phân trang comment phẳng (DTO cơ bản) theo một post.
    private async Task<PagedResult<CommentDto>> GetFlatByPostIdPagedAsync( // Phân trang DTO phẳng theo post.
        Guid postId, // Bài viết.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetFlatByPostIdPagedAsync.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        { // Mở khối.
            var cacheKey = EntityCacheKeys.CommentsFlatByPost(postId, page, pageSize); // Khóa cache.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken); // Đọc cache.
            if (cached is not null) // Hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Hết cache.
        } // Kết thúc nhánh cache.

        var (roots, total) = await BuildFlatTreesPagedCoreAsync(postId, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Đồng bộ pipeline: raw -> tree.
        var items = new List<CommentFlatNoLevelDto>(); // Danh sách phẳng sau khi flatten từ tree.
        foreach (var root in roots) // Duyệt từng cây gốc trong trang.
        {
            FlattenTreeFlat(root, items); // Flatten preorder vào danh sách phẳng.
        }
        var result = new PagedResult<CommentDto> // Gói phân trang.
        { // Mở khối.
            Items = items.Select(x => new CommentDto // Map thủ công từ flat-no-level sang CommentDto.
            { // Mở initializer.
                Id = x.Id, // Id comment.
                Content = x.Content, // Nội dung.
                CreatedAt = x.CreatedAt, // Thời gian tạo.
                PostId = x.PostId, // Post chứa comment.
                UserId = x.UserId, // Tác giả.
                ParentId = x.ParentId // Cha (nullable).
            }).ToList(), // List DTO kết quả.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng số gốc của route tree-based paging.
        }; // Kết thúc initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsFlatByPost(postId, page, pageSize), result, cancellationToken); // Ghi cache.
        } // Kết thúc set.

        return result; // Trả về.
    } // Kết thúc GetFlatByPostIdPagedAsync.

    // Alias phân trang phẳng toàn cục: cùng implementation với GetAllPagedAsync (GET /api/comments/flat khi không có postId).
    private async Task<PagedResult<CommentDto>> GetAllFlatPagedAsync( // Route /api/comments/flat toàn hệ, không tái sử dụng route /api/comments.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetAllFlatPagedAsync.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        {
            var cacheKey = EntityCacheKeys.CommentsFlatAll(page, pageSize); // Key riêng cho route /api/comments/flat.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken);
            if (cached is not null)
            {
                return cached;
            }
        }

        var (roots, total) = await BuildFlatTreesPagedCoreAsync(null, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Đồng bộ pipeline: raw -> tree.
        var items = new List<CommentFlatNoLevelDto>(); // Danh sách phẳng sau flatten.
        foreach (var root in roots) // Duyệt từng cây gốc.
        {
            FlattenTreeFlat(root, items); // Flatten preorder.
        }
        var result = new PagedResult<CommentDto>
        {
            Items = items.Select(x => new CommentDto
            {
                Id = x.Id,
                Content = x.Content,
                CreatedAt = x.CreatedAt,
                PostId = x.PostId,
                UserId = x.UserId,
                ParentId = x.ParentId
            }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total
        };
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo))
        {
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsFlatAll(page, pageSize), result, cancellationToken);
        }

        return result;
    } // Kết thúc GetAllFlatPagedAsync.

    // [09] Route: GET /api/comments/cte
    public async Task<PagedResult<CommentFlatDto>> GetCteFlatRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null)
    {
        // Điều phối route CTE phẳng: một hàm xử lý cả postId/null theo rule 1-route-1-method.
        return postId is { } pid
            // Có postId: lấy dữ liệu CTE của một bài.
            ? await GetCteFlatByPostIdPagedAsync(pid, page, pageSize, cancellationToken, createdAtFrom, createdAtTo)
            // Không postId: lấy dữ liệu CTE toàn cục.
            : await GetAllCteFlatPagedAsync(page, pageSize, cancellationToken, createdAtFrom, createdAtTo);
    }

    // Phân trang phẳng có Level trong một post — hàng thô từ CTE @postId (không EF phân trang).
    private async Task<PagedResult<CommentFlatDto>> GetCteFlatByPostIdPagedAsync( // Phân trang CommentFlatDto theo post (CTE).
        Guid postId, // Bài viết.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetCteFlatByPostIdPagedAsync.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        { // Mở khối.
            var cacheKey = EntityCacheKeys.CommentsCteFlatByPost(postId, page, pageSize); // Khóa cache.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken); // Đọc cache.
            if (cached is not null) // Hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Hết cache.
        } // Kết thúc nhánh cache.

        var (roots, total) = await BuildCteTreesPagedCoreAsync(postId, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Đồng bộ pipeline: raw -> tree.
        var slice = new List<CommentFlatDto>(); // Danh sách phẳng CTE sau flatten.
        foreach (var root in roots) // Duyệt từng cây gốc của trang.
        {
            FlattenTreeCte(root, slice); // Flatten preorder, giữ Level.
        }
        var result = new PagedResult<CommentFlatDto> // Gói phân trang.
        { // Mở khối.
            Items = slice, // Hàng thô phẳng.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng hàng.
        }; // Kết thúc initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsCteFlatByPost(postId, page, pageSize), result, cancellationToken); // Ghi cache.
        } // Kết thúc set.

        return result; // Trả về.
    } // Kết thúc GetCteFlatByPostIdPagedAsync.

    // Phân trang danh sách phẳng có Level từ CTE SQL toàn hệ (GET /api/comments/cte khi không có postId).
    private async Task<PagedResult<CommentFlatDto>> GetAllCteFlatPagedAsync( // Phân trang hàng thô CTE toàn hệ.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt trên kết quả CTE.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetAllCteFlatPagedAsync.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        { // Mở khối.
            var cacheKey = EntityCacheKeys.CommentsAllCteFlat(page, pageSize); // Khóa cache.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken); // Đọc cache.
            if (cached is not null) // Hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Hết cache.
        } // Kết thúc nhánh cache.

        var (roots, total) = await BuildCteTreesPagedCoreAsync(null, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Đồng bộ pipeline: raw -> tree.
        var slice = new List<CommentFlatDto>(); // Danh sách phẳng sau flatten.
        foreach (var root in roots) // Duyệt từng cây gốc.
        {
            FlattenTreeCte(root, slice); // Flatten preorder CTE.
        }
        var result = new PagedResult<CommentFlatDto> // Gói phân trang.
        { // Mở khối.
            Items = slice, // Trang con hàng thô có Level.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng hàng CTE.
        }; // Kết thúc initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsAllCteFlat(page, pageSize), result, cancellationToken); // Ghi cache.
        } // Kết thúc set.

        return result; // Trả về.
    } // Kết thúc GetAllCteFlatPagedAsync.

    // [10] Route: GET /api/comments/tree/flat
    public async Task<PagedResult<CommentTreeFlatDto>> GetTreeFlatRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null)
    {
        // Điều phối route tree/flat: tách rõ nhánh một bài và toàn hệ.
        return postId is { } pid
            // Có postId: dựng cây trong một bài.
            ? await GetTreeByPostIdPagedAsync(pid, page, pageSize, cancellationToken, createdAtFrom, createdAtTo)
            // Không postId: dựng rừng gốc toàn hệ.
            : await GetAllTreePagedAsync(page, pageSize, cancellationToken, createdAtFrom, createdAtTo);
    }

    // Phân trang cây theo gốc trong một post (dựng cây EF; lặp BuildTree theo từng gốc trang).
    private async Task<PagedResult<CommentTreeFlatDto>> GetTreeByPostIdPagedAsync( // Cây theo post, phân trang gốc.
        Guid postId, // Bài viết.
        int page, // Trang gốc.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt trên gốc trong post.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetTreeByPostIdPagedAsync.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        { // Mở khối.
            var cacheKey = EntityCacheKeys.CommentsTreeByPost(postId, page, pageSize); // Khóa cache.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentTreeFlatDto>>(cacheKey, cancellationToken); // Đọc cache.
            if (cached is not null) // Hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Hết cache.
        } // Kết thúc nhánh cache.

        var (trees, total) = await BuildFlatTreesPagedCoreAsync(postId, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Đồng bộ pipeline: raw -> tree.

        var result = new PagedResult<CommentTreeFlatDto> // Gói phân trang.
        { // Mở khối.
            Items = trees.Select(MapTreeFlat).ToList(), // Danh sách cây flat không có Level.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng gốc trong post.
        }; // Kết thúc initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsTreeByPost(postId, page, pageSize), result, cancellationToken); // Ghi cache.
        } // Kết thúc set.

        return result; // Trả về.
    } // Kết thúc GetTreeByPostIdPagedAsync.

    // Phân trang comment dạng cây gốc toàn hệ (GET /api/comments/tree/flat khi không có postId).
    private async Task<PagedResult<CommentTreeFlatDto>> GetAllTreePagedAsync( // Phân trang cây toàn hệ thống (EF).
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt (chỉ áp lên comment gốc).
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetAllTreePagedAsync.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        { // Mở khối.
            var cacheKey = EntityCacheKeys.CommentsAllTreeFlat(page, pageSize); // Khóa cây EF theo trang.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentTreeFlatDto>>(cacheKey, cancellationToken); // Thử cache.
            if (cached is not null) // Hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Hết cache.
        } // Kết thúc nhánh cache.

        var (trees, total) = await BuildFlatTreesPagedCoreAsync(null, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Đồng bộ pipeline: raw -> tree.
        var result = new PagedResult<CommentTreeFlatDto> // Gói phân trang.
        { // Mở khối.
            Items = trees.Select(MapTreeFlat).ToList(), // Danh sách cây flat không có Level.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng số gốc.
        }; // Kết thúc initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsAllTreeFlat(page, pageSize), result, cancellationToken); // Lưu cache.
        } // Kết thúc set.

        return result; // Trả kết quả.
    } // Kết thúc GetAllTreePagedAsync.

    // [11] Route: GET /api/comments/tree/cte
    public async Task<PagedResult<CommentTreeDto>> GetTreeCteRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null)
    {
        // Điều phối route tree/cte: cùng chữ ký, xử lý mọi trạng thái input postId.
        return postId is { } pid
            // Có postId: cây CTE trong một bài.
            ? await GetCteTreeByPostIdPagedAsync(pid, page, pageSize, cancellationToken, createdAtFrom, createdAtTo)
            // Không postId: rừng CTE toàn hệ.
            : await GetAllCteTreePagedAsync(page, pageSize, cancellationToken, createdAtFrom, createdAtTo);
    }

    // Cây trong post từ CTE: hàng thô → BuildTreeCte → phân trang danh sách gốc (không dùng EF tree).
    private async Task<PagedResult<CommentTreeDto>> GetCteTreeByPostIdPagedAsync( // Cây CTE theo post.
        Guid postId, // Bài viết.
        int page, // Trang gốc.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetCteTreeByPostIdPagedAsync.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        { // Mở khối.
            var cacheKey = EntityCacheKeys.CommentsTreeCteByPost(postId, page, pageSize); // Khóa tách khỏi CommentsTreeByPost (EF).
            var cached = await _cache.GetJsonAsync<PagedResult<CommentTreeDto>>(cacheKey, cancellationToken); // Đọc cache.
            if (cached is not null) // Hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Hết cache.
        } // Kết thúc nhánh cache.

        var (items, total) = await BuildCteTreesPagedCoreAsync(postId, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Đồng bộ pipeline: raw -> tree.
        var result = new PagedResult<CommentTreeDto> // Gói phân trang.
        { // Mở khối.
            Items = items, // Mỗi mục là subtree đầy đủ từ CTE.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng gốc.
        }; // Kết thúc initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsTreeCteByPost(postId, page, pageSize), result, cancellationToken); // Ghi cache.
        } // Kết thúc set.

        return result; // Trả về.
    } // Kết thúc GetCteTreeByPostIdPagedAsync.

    // Phân trang cây gốc toàn hệ từ CTE (GET /api/comments/tree/cte khi không có postId).
    private async Task<PagedResult<CommentTreeDto>> GetAllCteTreePagedAsync( // Cây từ hàng CTE, phân trang theo gốc.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetAllCteTreePagedAsync.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        { // Mở khối.
            var cacheKey = EntityCacheKeys.CommentsAllTreeCte(page, pageSize); // Khóa tách biệt cây EF.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentTreeDto>>(cacheKey, cancellationToken); // Đọc cache.
            if (cached is not null) // Hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Hết cache.
        } // Kết thúc nhánh cache.

        var (items, total) = await BuildCteTreesPagedCoreAsync(null, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Đồng bộ pipeline: raw -> tree.
        var result = new PagedResult<CommentTreeDto> // Gói phân trang.
        { // Mở khối.
            Items = items, // Mỗi phần tử là một cây CommentTreeDto lồng nhau.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng gốc.
        }; // Kết thúc initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsAllTreeCte(page, pageSize), result, cancellationToken); // Ghi cache.
        } // Kết thúc set.

        return result; // Trả về.
    } // Kết thúc GetAllCteTreePagedAsync.

    // [12] Route: GET /api/comments/tree/flat/flatten
    public async Task<PagedResult<CommentFlatNoLevelDto>> GetTreeFlatFlattenRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null)
    {
        // Điều phối route flatten tree/flat: vẫn một hàm public duy nhất cho route.
        return postId is { } pid
            // Có postId: flatten cây của một bài.
            ? await GetFlattenedTreeByPostIdPagedAsync(pid, page, pageSize, cancellationToken, createdAtFrom, createdAtTo)
            // Không postId: flatten rừng toàn hệ.
            : await GetFlattenedForestPagedAsync(page, pageSize, cancellationToken, createdAtFrom, createdAtTo);
    }

    // Làm phẳng cây EF trong một post theo trang gốc (một lần dựng rừng, trích subtree theo gốc trang).
    private async Task<PagedResult<CommentFlatNoLevelDto>> GetFlattenedTreeByPostIdPagedAsync( // Làm phẳng cây EF trong post.
        Guid postId, // Bài viết.
        int page, // Trang gốc.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt trên gốc.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetFlattenedTreeByPostIdPagedAsync.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        { // Mở khối.
            var cacheKey = EntityCacheKeys.CommentsFlattenedEfTreeByPost(postId, page, pageSize); // Khóa cache.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatNoLevelDto>>(cacheKey, cancellationToken); // Đọc cache.
            if (cached is not null) // Hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Hết cache.
        } // Kết thúc nhánh cache.

        var (roots, total) = await BuildFlatTreesPagedCoreAsync(postId, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Đồng bộ pipeline: raw -> tree.
        var flat = new List<CommentFlatNoLevelDto>(); // Kết quả làm phẳng cuối cùng của route.
        foreach (var root in roots) // Duyệt các cây gốc đã build.
        {
            FlattenTreeFlat(root, flat); // Flatten trực tiếp vì roots đã là tree node đầy đủ.
        }
        var result = new PagedResult<CommentFlatNoLevelDto> // Gói phân trang.
        { // Mở khối.
            Items = flat, // Dòng phẳng.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng gốc post.
        }; // Kết thúc initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsFlattenedEfTreeByPost(postId, page, pageSize), result, cancellationToken); // Ghi cache.
        } // Kết thúc set.

        return result; // Trả về.
    } // Kết thúc GetFlattenedTreeByPostIdPagedAsync.

    // Làm phẳng rừng cây EF toàn hệ (GET /api/comments/tree/flat/flatten khi không có postId).
    private async Task<PagedResult<CommentFlatNoLevelDto>> GetFlattenedForestPagedAsync( // Rừng EF làm phẳng preorder, phân trang theo gốc.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt trên gốc.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetFlattenedForestPagedAsync.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        { // Mở khối.
            var cacheKey = EntityCacheKeys.CommentsAllFlattenEfTree(page, pageSize); // Khóa cache.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatNoLevelDto>>(cacheKey, cancellationToken); // Đọc cache.
            if (cached is not null) // Hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Hết cache.
        } // Kết thúc nhánh cache.

        var (roots, total) = await BuildFlatTreesPagedCoreAsync(null, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Đồng bộ pipeline: raw -> tree.
        var flat = new List<CommentFlatNoLevelDto>(); // Kết quả làm phẳng cuối cùng của route.
        foreach (var root in roots) // Duyệt các cây gốc đã build.
        {
            FlattenTreeFlat(root, flat); // Flatten trực tiếp từ node tree.
        }
        var result = new PagedResult<CommentFlatNoLevelDto> // Gói phân trang.
        { // Mở khối.
            Items = flat, // Danh sách phẳng có Level.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng gốc (theo phân trang gốc).
        }; // Kết thúc initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsAllFlattenEfTree(page, pageSize), result, cancellationToken); // Ghi cache.
        } // Kết thúc set.

        return result; // Trả về.
    } // Kết thúc GetFlattenedForestPagedAsync.

    // [13] Route: GET /api/comments/tree/cte/flatten
    public async Task<PagedResult<CommentFlatDto>> GetTreeCteFlattenRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null)
    {
        // Điều phối route flatten tree/cte: gom cả 2 mode postId/null vào một cổng.
        return postId is { } pid
            // Có postId: flatten tree CTE trong một bài.
            ? await GetFlattenedCteTreeByPostIdPagedAsync(pid, page, pageSize, cancellationToken, createdAtFrom, createdAtTo)
            // Không postId: flatten rừng CTE toàn cục.
            : await GetFlattenedFromCtePagedAsync(page, pageSize, cancellationToken, null, createdAtFrom, createdAtTo);
    }

    // CTE theo post: dựng cây từ hàng SQL, làm phẳng, phân trang trong RAM.
    private async Task<PagedResult<CommentFlatDto>> GetFlattenedCteTreeByPostIdPagedAsync( // CTE một post, phẳng, cắt trang.
        Guid postId, // Bài viết.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetFlattenedCteTreeByPostIdPagedAsync.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        { // Mở khối.
            var cacheKey = EntityCacheKeys.CommentsFlattenedCteTree(postId, page, pageSize); // Khóa cache.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken); // Đọc cache.
            if (cached is not null) // Hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Hết cache.
        } // Kết thúc nhánh cache.

        var (pagedRoots, total) = await BuildCteTreesPagedCoreAsync(postId, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Đồng bộ pipeline: raw -> tree.
        var slice = new List<CommentFlatDto>();
        foreach (var root in pagedRoots)
        {
            FlattenTreeCte(root, slice); // Quá trình flatten preorder sau khi build tree CTE.
        }
        var result = new PagedResult<CommentFlatDto> // Gói phân trang.
        { // Mở khối.
            Items = slice, // Trang con.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng gốc tree CTE theo trang hiện tại.
        }; // Kết thúc initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsFlattenedCteTree(postId, page, pageSize), result, cancellationToken); // Ghi cache.
        } // Kết thúc set.

        return result; // Trả về.
    } // Kết thúc GetFlattenedCteTreeByPostIdPagedAsync.

    // Làm phẳng CTE toàn hệ rồi cắt trang trong RAM (GET /api/comments/tree/cte/flatten khi không có postId).
    private async Task<PagedResult<CommentFlatDto>> GetFlattenedFromCtePagedAsync( // CTE toàn cục rồi cắt trang trong RAM.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Một bài: ủy quyền sang CTE-theo-post.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetFlattenedFromCtePagedAsync.
        if (postId is { } scopedPost) // Client lọc theo post.
        { // Mở khối.
            return await GetFlattenedCteTreeByPostIdPagedAsync(scopedPost, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // CTE một post + lọc ngày.
        } // Kết thúc nhánh post.

        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        { // Mở khối.
            var cacheKey = EntityCacheKeys.CommentsAllFlattenCteTree(page, pageSize); // Khóa cache.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken); // Đọc cache.
            if (cached is not null) // Hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Hết cache.
        } // Kết thúc nhánh cache.

        var (pagedRoots, total) = await BuildCteTreesPagedCoreAsync(null, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // Đồng bộ pipeline: raw -> tree.
        var slice = new List<CommentFlatDto>();
        foreach (var root in pagedRoots)
        {
            FlattenTreeCte(root, slice); // Quá trình flatten preorder sau khi build tree CTE.
        }
        var result = new PagedResult<CommentFlatDto> // Gói phân trang.
        { // Mở khối.
            Items = slice, // Trang con của danh sách phẳng.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng gốc tree CTE theo trang hiện tại.
        }; // Kết thúc initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsAllFlattenCteTree(page, pageSize), result, cancellationToken); // Ghi cache.
        } // Kết thúc set.

        return result; // Trả về.
    } // Kết thúc GetFlattenedFromCtePagedAsync.

    #endregion

    #region CommentsController — demo danh sách (lazy / eager / explicit / projection)

    // Demo phân trang lazy: normalize rồi gọi repository.
    // [14] Route: GET /api/comments/demo/lazy-loading (paged)
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsLazyLoadingDemoPagedAsync( // Demo phân trang lazy.
        int page, // Trang yêu cầu.
        int pageSize, // Cỡ trang yêu cầu.
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc một bài cho demo.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetCommentsLazyLoadingDemoPagedAsync.
        if (postId is { } pid) // Có postId → đảm bảo post tồn tại trước khi query demo.
        { // Mở khối.
            await EnsurePostExistsAsync(pid); // 404 nếu sai Id bài.
        } // Kết thúc kiểm tra post.

        var (p, s) = PaginationQuery.Normalize(page, pageSize); // Chuẩn hóa số trang/cỡ trang (không SQL).
        var (items, total) = await _repository.GetCommentsLazyLoadingDemoRouteAsync(true, p, s, cancellationToken, postId, createdAtFrom, createdAtTo); // COUNT + SELECT.
        return new PagedResult<CommentLoadingDemoDto> // Gói kết quả.
        { // Mở khối.
            Items = items, // Danh sách đã map từ repository.
            Page = p, // Trang đã chuẩn hóa.
            PageSize = s, // Cỡ trang đã chuẩn hóa.
            TotalCount = total // Tổng bản ghi.
        }; // Kết thúc object initializer.
    } // Kết thúc GetCommentsLazyLoadingDemoPagedAsync.

    // Demo phân trang eager: normalize rồi gọi repository.
    // [15] Route: GET /api/comments/demo/eager-loading (paged)
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsEagerLoadingDemoPagedAsync( // Demo phân trang eager.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc một bài.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetCommentsEagerLoadingDemoPagedAsync.
        if (postId is { } pid) // Post phải tồn tại.
        { // Mở khối.
            await EnsurePostExistsAsync(pid); // 404 nếu không có bài.
        } // Kết thúc kiểm tra.

        var (p, s) = PaginationQuery.Normalize(page, pageSize); // Chuẩn hóa tham số phân trang.
        var (items, total) = await _repository.GetCommentsEagerLoadingDemoRouteAsync(true, p, s, cancellationToken, postId, createdAtFrom, createdAtTo); // Include + phân trang.
        return new PagedResult<CommentLoadingDemoDto> // Gói kết quả.
        { // Mở khối.
            Items = items, // Dòng demo.
            Page = p, // Trang.
            PageSize = s, // Cỡ trang.
            TotalCount = total // Tổng.
        }; // Kết thúc initializer.
    } // Kết thúc GetCommentsEagerLoadingDemoPagedAsync.

    // Demo phân trang explicit loading.
    // [16] Route: GET /api/comments/demo/explicit-loading (paged)
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsExplicitLoadingDemoPagedAsync( // Demo phân trang explicit.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc một bài.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetCommentsExplicitLoadingDemoPagedAsync.
        if (postId is { } pid) // Kiểm tra post.
        { // Mở khối.
            await EnsurePostExistsAsync(pid); // 404 nếu không tồn tại.
        } // Kết thúc kiểm tra.

        var (p, s) = PaginationQuery.Normalize(page, pageSize); // Chuẩn hóa.
        var (items, total) = await _repository.GetCommentsExplicitLoadingDemoRouteAsync(true, p, s, cancellationToken, postId, createdAtFrom, createdAtTo); // LoadAsync sau phân trang.
        return new PagedResult<CommentLoadingDemoDto> // Gói kết quả.
        { // Mở khối.
            Items = items, // Dòng demo.
            Page = p, // Trang.
            PageSize = s, // Cỡ trang.
            TotalCount = total // Tổng.
        }; // Kết thúc initializer.
    } // Kết thúc GetCommentsExplicitLoadingDemoPagedAsync.

    // Demo phân trang projection (DTO ngay trong SQL).
    // [17] Route: GET /api/comments/demo/projection (paged)
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsProjectionDemoPagedAsync( // Demo phân trang projection.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc một bài.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối GetCommentsProjectionDemoPagedAsync.
        if (postId is { } pid) // Post hợp lệ.
        { // Mở khối.
            await EnsurePostExistsAsync(pid); // 404 nếu sai.
        } // Kết thúc kiểm tra.

        var (p, s) = PaginationQuery.Normalize(page, pageSize); // Chuẩn hóa.
        var (items, total) = await _repository.GetCommentsProjectionDemoRouteAsync(true, p, s, cancellationToken, postId, createdAtFrom, createdAtTo); // Select DTO phân trang.
        return new PagedResult<CommentLoadingDemoDto> // Gói kết quả.
        { // Mở khối.
            Items = items, // Dòng demo.
            Page = p, // Trang.
            PageSize = s, // Cỡ trang.
            TotalCount = total // Tổng.
        }; // Kết thúc initializer.
    } // Kết thúc GetCommentsProjectionDemoPagedAsync.

    // Demo toàn bộ comment + lazy: không COUNT/Skip/Take; ủy quyền repository (cảnh báo dữ liệu lớn).
    // [14] Route: GET /api/comments/demo/lazy-loading (unpaged)
    public async Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsLazyLoadingDemoAsync(
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc một bài.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối.
        if (postId is { } pid) // Kiểm tra post trước.
        { // Mở khối.
            await EnsurePostExistsAsync(pid); // 404 nếu không có.
        } // Kết thúc kiểm tra.

        var (items, _) = await _repository.GetCommentsLazyLoadingDemoRouteAsync(false, 1, 1, cancellationToken, postId, createdAtFrom, createdAtTo); // Unpaged cùng route function.
        return items; // List → IReadOnlyList.
    } // Kết thúc GetAllCommentsLazyLoadingDemoAsync.

    // Demo toàn bộ comment + eager.
    // [15] Route: GET /api/comments/demo/eager-loading (unpaged)
    public async Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsEagerLoadingDemoAsync(
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc một bài.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối.
        if (postId is { } pid) // Post tồn tại.
        { // Mở khối.
            await EnsurePostExistsAsync(pid); // 404.
        } // Kết thúc kiểm tra.

        var (items, _) = await _repository.GetCommentsEagerLoadingDemoRouteAsync(false, 1, 1, cancellationToken, postId, createdAtFrom, createdAtTo); // Unpaged cùng route function.
        return items; // Trả danh sách.
    } // Kết thúc GetAllCommentsEagerLoadingDemoAsync.

    // Demo toàn bộ comment + explicit.
    // [16] Route: GET /api/comments/demo/explicit-loading (unpaged)
    public async Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsExplicitLoadingDemoAsync(
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc một bài.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối.
        if (postId is { } pid) // Kiểm tra post.
        { // Mở khối.
            await EnsurePostExistsAsync(pid); // 404.
        } // Kết thúc kiểm tra.

        var (items, _) = await _repository.GetCommentsExplicitLoadingDemoRouteAsync(false, 1, 1, cancellationToken, postId, createdAtFrom, createdAtTo); // Unpaged cùng route function.
        return items; // Trả danh sách.
    } // Kết thúc GetAllCommentsExplicitLoadingDemoAsync.

    // Demo toàn bộ comment + projection.
    // [17] Route: GET /api/comments/demo/projection (unpaged)
    public async Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsProjectionDemoAsync(
        CancellationToken cancellationToken = default, // Hủy.
        Guid? postId = null, // Lọc một bài.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối.
        if (postId is { } pid) // Post hợp lệ.
        { // Mở khối.
            await EnsurePostExistsAsync(pid); // 404.
        } // Kết thúc kiểm tra.

        var (items, _) = await _repository.GetCommentsProjectionDemoRouteAsync(false, 1, 1, cancellationToken, postId, createdAtFrom, createdAtTo); // Unpaged cùng route function.
        return items; // Trả danh sách.
    } // Kết thúc GetAllCommentsProjectionDemoAsync.

    #endregion

    #region ICommentService — Bổ trợ (phân trang toàn hệ, tìm kiếm, đọc theo post; demo một Id không có route trong CommentsController)

    // Phân trang toàn hệ — dùng nội bộ GetCommentListAsync, GetAllFlatPagedAsync và unit test.
    private async Task<PagedResult<CommentDto>> GetAllPagedAsync( // Phân trang toàn cục CommentDto.
        int page, // Số trang (1-based).
        int pageSize, // Số bản ghi mỗi trang.
        CancellationToken cancellationToken = default, // Hủy bất đồng bộ.
        DateTime? createdAtFrom = null, // Lọc CreatedAt inclusive.
        DateTime? createdAtTo = null) // Lọc CreatedAt inclusive.
    { // Mở khối GetAllPagedAsync.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Không lọc ngày → được cache.
        { // Mở khối cache.
            var cacheKey = EntityCacheKeys.CommentsAll(page, pageSize); // Chuỗi khóa cache theo số trang và cỡ trang.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken); // Đọc JSON từ cache; không SQL.
            if (cached is not null) // Có bản trong cache.
            { // Mở khối.
                return cached; // Trả ngay, bỏ qua repository.
            } // Kết thúc nhánh cache hit.
        } // Kết thúc nhánh có thể dùng cache.

        var (items, total) = await _repository.GetCommentsRoutePagedAsync(null, null, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // COUNT + SELECT có lọc ngày.
        var result = new PagedResult<CommentDto> // Tạo object kết quả phân trang API.
        { // Mở khối.
            Items = items.Select(_mapper.Map<CommentDto>).ToList(), // Biến mỗi Comment thành CommentDto trong RAM (LINQ to Objects, không SQL).
            Page = page, // Ghi số trang hiện tại.
            PageSize = pageSize, // Ghi cỡ trang.
            TotalCount = total // Tổng bản ghi từ COUNT repository.
        }; // Kết thúc khởi tạo PagedResult.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsAll(page, pageSize), result, cancellationToken); // Ghi cache.
        } // Kết thúc set cache.

        return result; // Trả về cho caller.
    } // Kết thúc GetAllPagedAsync.

    // Toàn bộ comment phẳng một post một lần — dùng nội bộ GetCommentListAsync (unpaged + postId).
    private async Task<IReadOnlyList<CommentDto>> GetAllByPostIdAsync( // Toàn bộ DTO theo PostId.
        Guid postId, // Bài viết.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetAllByPostIdAsync.
        await EnsurePostExistsAsync(postId); // 404 nếu post không tồn tại.
        var entities = await _repository.GetCommentsRouteAllAsync(postId); // Một SELECT toàn comment thuộc post (AsNoTracking trong repo).
        return entities.Select(_mapper.Map<CommentDto>).ToList(); // Map sang DTO, trả IReadOnlyList qua List.
    } // Kết thúc GetAllByPostIdAsync.

    // Tìm theo nội dung toàn hệ — dùng nội bộ GetCommentListAsync (nhánh content + phân trang).
    private async Task<PagedResult<CommentDto>> SearchByContentPagedAsync( // Tìm kiếm nội dung toàn hệ thống.
        string? content, // Chuỗi tìm kiếm có thể null.
        int page, // Số trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Token hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối SearchByContentPagedAsync.
        var term = RequireSearchTerm(content); // Cắt khoảng trắng; ném lỗi nếu rỗng (không SQL).
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache chỉ khi không lọc ngày.
        { // Mở khối cache.
            var cacheKey = EntityCacheKeys.CommentsSearchContent(EntityCacheHash.SearchTerm(term), page, pageSize); // Khóa gồm băm term để khóa ngắn.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken); // Thử đọc cache.
            if (cached is not null) // Trúng cache.
            { // Mở khối.
                return cached; // Trả kết quả đã lưu.
            } // Hết nhánh cache.
        } // Kết thúc nhánh có cache.

        var (items, total) = await _repository.GetCommentsRoutePagedAsync(null, term, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // COUNT + SELECT.
        var result = new PagedResult<CommentDto> // Gói trang kết quả.
        { // Mở khối.
            Items = items.Select(_mapper.Map<CommentDto>).ToList(), // Map từng phần tử trong bộ nhớ.
            Page = page, // Chỉ số trang.
            PageSize = pageSize, // Kích thước trang.
            TotalCount = total // Tổng khớp tìm kiếm.
        }; // Kết thúc object initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ ghi cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsSearchContent(EntityCacheHash.SearchTerm(term), page, pageSize), result, cancellationToken); // Ghi cache.
        } // Kết thúc set.

        return result; // Trả về cho caller.
    } // Kết thúc SearchByContentPagedAsync.

    // Đọc một comment trong phạm vi post — mở rộng API / test (không có action riêng trong CommentsController hiện tại).
    private async Task<CommentDto> GetByIdInPostAsync( // Đọc comment trong một post.
        Guid postId, // Định danh bài viết chứa comment.
        Guid commentId, // Định danh comment cần đọc.
        CancellationToken cancellationToken = default) // Token hủy thao tác bất đồng bộ.
    { // Mở khối GetByIdInPostAsync.
        await EnsurePostExistsAsync(postId); // Gọi Any Post — một truy vấn SQL trong repository.
        var dto = await _repository.GetCommentByIdRouteReadAsync(commentId, postId, cancellationToken); // SELECT projection một dòng hoặc null.
        if (dto is null) // Không có comment đó trong post.
        { // Mở khối.
            throw new ApiException( // Ném lỗi HTTP 404 thống nhất API.
                StatusCodes.Status404NotFound, // Mã 404.
                ApiErrorCodes.CommentNotFound, // Mã lỗi nghiệp vụ.
                ApiMessages.CommentNotFound); // Thông điệp hiển thị.
        } // Kết thúc nhánh null.

        return dto; // Trả DTO đã đọc.
    } // Kết thúc GetByIdInPostAsync.

    // Tìm theo nội dung trong một post — dùng nội bộ GetCommentListAsync.
    private async Task<PagedResult<CommentDto>> SearchByContentInPostPagedAsync( // Tìm theo nội dung trong post.
        Guid postId, // Bài viết giới hạn phạm vi tìm kiếm.
        string? content, // Chuỗi tìm kiếm.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default, // Token hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối SearchByContentInPostPagedAsync.
        var term = RequireSearchTerm(content); // Chuẩn hóa term.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Cache khi không lọc ngày.
        { // Mở khối.
            var cacheKey = EntityCacheKeys.CommentsSearchContentInPost(postId, EntityCacheHash.SearchTerm(term), page, pageSize); // Khóa có postId.
            var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken); // Đọc cache nếu có.
            if (cached is not null) // Cache hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Hết nhánh cache.
        } // Kết thúc nhánh cache.

        await EnsurePostExistsAsync(postId); // Kiểm tra post tồn tại (SQL Any).
        var (items, total) = await _repository.GetCommentsRoutePagedAsync(postId, term, page, pageSize, cancellationToken, createdAtFrom, createdAtTo); // COUNT + SELECT trong post.
        var result = new PagedResult<CommentDto> // Đối tượng phân trang.
        { // Mở khối.
            Items = items.Select(_mapper.Map<CommentDto>).ToList(), // Ánh xạ sang DTO.
            Page = page, // Trang hiện tại.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng bản ghi khớp.
        }; // Kết thúc initializer.
        if (!HasCreatedAtFilter(createdAtFrom, createdAtTo)) // Chỉ cache khi không lọc ngày.
        { // Mở khối.
            await _cache.SetJsonAsync(EntityCacheKeys.CommentsSearchContentInPost(postId, EntityCacheHash.SearchTerm(term), page, pageSize), result, cancellationToken); // Lưu cache.
        } // Kết thúc set.

        return result; // Trả kết quả.
    } // Kết thúc SearchByContentInPostPagedAsync.

    #endregion

    #region Private helpers

    // Có lọc CreatedAt từ query → không dùng cache list (tránh khóa bùng nổ).
    private static bool HasCreatedAtFilter(DateTime? createdAtFrom, DateTime? createdAtTo) => // Hai biên tuỳ chọn.
        createdAtFrom.HasValue || createdAtTo.HasValue; // Chỉ cần một biên cũng bỏ cache.

    // Chuẩn hóa và bắt buộc chuỗi tìm kiếm không rỗng; ném 400 nếu không hợp lệ.
    private static string RequireSearchTerm(string? raw) // Chuỗi thô có thể null.
    { // Mở khối RequireSearchTerm.
        var t = raw?.Trim(); // Nullable string → bỏ khoảng đầu cuối; null vẫn null.
        if (string.IsNullOrEmpty(t)) // Rỗng hoặc null sau trim.
        { // Mở khối.
            throw new ApiException( // 400 thiếu term.
                StatusCodes.Status400BadRequest, // HTTP.
                ApiErrorCodes.SearchTermRequired, // Mã.
                ApiMessages.SearchTermRequired); // Thông báo.
        } // Hết nhánh lỗi.

        return t; // Chuỗi không rỗng cho repository.
    } // Kết thúc RequireSearchTerm.

    // Đảm bảo post tồn tại rồi mới search unpaged trong post (helper GetCommentListAsync).
    private async Task<List<Comment>> SearchByContentInPostAllValidatedAsync( // Helper cho GetCommentListAsync.
        Guid postId, // Bài viết.
        string term, // Đã trim.
        CancellationToken cancellationToken, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null) // Lọc CreatedAt.
    { // Mở khối.
        await EnsurePostExistsAsync(postId); // 404 nếu không có post.
        return await _repository.SearchCommentsRouteAllAsync(postId, term, cancellationToken, createdAtFrom, createdAtTo); // List khớp.
    } // Kết thúc SearchByContentInPostAllValidatedAsync.

    // Gói danh sách đầy đủ thành PagedResult (Page=1, PageSize=số phần tử hoặc mặc định khi rỗng).
    private static PagedResult<CommentDto> ToUnpagedCommentDtoResult(List<CommentDto> items) // Không cắt trang.
    { // Mở khối.
        var n = items.Count; // Số bản ghi.
        var ps = n == 0 ? PaginationQuery.DefaultPageSize : n; // Tránh PageSize=0 gây TotalPages lạ.
        return new PagedResult<CommentDto> // Một “trang” chứa toàn bộ.
        { // Mở initializer.
            Items = items, // Toàn bộ dòng.
            Page = 1, // Luôn 1.
            PageSize = ps, // Bằng số phần tử (hoặc default khi rỗng).
            TotalCount = n // Tổng khớp.
        }; // Kết thúc object.
    } // Kết thúc ToUnpagedCommentDtoResult.

    // Pipeline lõi cho toàn bộ route flat [08][10][12]: lấy raw EF theo gốc trang, dựng tree một lần, trả cây theo đúng gốc trang.
    private async Task<(List<CommentTreeDto> Roots, long TotalCount)> BuildFlatTreesPagedCoreAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null)
    {
        if (postId is { } pid)
        {
            await EnsurePostExistsAsync(pid); // Đồng bộ validation giữa tất cả route flat theo post.
        }

        var (pagedRoots, totalCount, rawComments) = await _repository.LoadRawFlatAsync(
            postId,
            page,
            pageSize,
            rootsOnly: true,
            loadCommentsForRootPosts: true,
            cancellationToken,
            createdAtFrom,
            createdAtTo); // Một nguồn raw duy nhất cho nhóm route flat benchmark.

        if (pagedRoots.Count == 0 || rawComments.Count == 0)
        {
            return (new List<CommentTreeDto>(), totalCount); // Không có dữ liệu thì trả rỗng sớm.
        }

        var forest = BuildTreeFlat(rawComments); // Build tree một lần duy nhất.
        var byRootId = forest.ToDictionary(root => root.Id, root => root); // Lookup subtree theo root id.
        var roots = new List<CommentTreeDto>(pagedRoots.Count); // Kết quả theo thứ tự phân trang gốc.
        foreach (var root in pagedRoots)
        {
            if (byRootId.TryGetValue(root.Id, out var node))
            {
                roots.Add(node); // Chỉ thêm subtree thuộc các root đã phân trang.
            }
        }

        return (roots, totalCount); // Trả cây đã đồng bộ quy trình.
    }

    // Pipeline lõi cho toàn bộ route cte [09][11][13]: lấy raw CTE, dựng tree, phân trang theo gốc.
    private async Task<(List<CommentTreeDto> Roots, long TotalCount)> BuildCteTreesPagedCoreAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null)
    {
        if (postId is { } pid)
        {
            await EnsurePostExistsAsync(pid); // Đồng bộ validation giữa tất cả route cte theo post.
        }

        var rows = await _repository.LoadRawCteAsync(postId, cancellationToken, createdAtFrom, createdAtTo); // Một nguồn raw duy nhất cho nhóm route cte benchmark.
        var roots = postId.HasValue
            ? BuildTreeCte(rows) // Một post.
            : BuildForestCte(rows); // Toàn hệ.

        var orderedRoots = roots
            .OrderBy(root => root.CreatedAt)
            .ThenBy(root => root.Id)
            .ToList(); // Thứ tự ổn định giống nhau cho mọi route cte.

        var totalCount = (long)orderedRoots.Count; // Tổng số gốc trước khi cắt trang.
        var pagedRoots = orderedRoots
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return (pagedRoots, totalCount); // Trả cây đã phân trang gốc.
    }

    // Ném 404 nếu post không tồn tại; dùng chung cho endpoint theo postId.
    private async Task EnsurePostExistsAsync(Guid postId) // Id bài viết cần kiểm tra.
    { // Mở khối EnsurePostExistsAsync.
        if (!await _repository.PostExistsAsync(postId)) // Any trong bảng Posts.
        { // Mở khối.
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.PostNotFound, // Mã.
                ApiMessages.PostNotFound); // Thông báo.
        } // Hết nhánh không tồn tại.
    } // Kết thúc EnsurePostExistsAsync.

    // Ném 404 nếu user không tồn tại; dùng cho GET comment theo tác giả.
    private async Task EnsureUserExistsAsync(Guid userId) // Id người dùng cần kiểm tra.
    { // Mở khối EnsureUserExistsAsync.
        if (!await _repository.UserExistsAsync(userId)) // Any trong bảng Users.
        { // Mở khối.
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.UserNotFound, // Mã.
                ApiMessages.UserNotFound); // Thông báo.
        } // Hết nhánh không tồn tại.
    } // Kết thúc EnsureUserExistsAsync.

    // Dựng rừng CommentTreeDto từ danh sách phẳng entity; xử lý dữ liệu lệch và chu kỳ bằng cách nâng nút lên gốc.
    private static List<CommentTreeDto> BuildTreeFlat(List<Comment> comments) // Danh sách comment flat (EF) một hoặc nhiều cây.
    { // Mở khối BuildTreeFlat.
        var lookup = comments.ToDictionary( // Từ Id → nút DTO trống chưa gắn con.
            x => x.Id, // Khóa dictionary.
            x => new CommentTreeDto // Khởi tạo nút lá/chưa có con.
            { // Mở khối.
                Id = x.Id, // Định danh.
                Content = x.Content, // Nội dung.
                CreatedAt = x.CreatedAt, // Thời gian.
                PostId = x.PostId, // Bài viết.
                UserId = x.UserId, // Người viết.
                ParentId = x.ParentId, // Tham chiếu cha.
                Level = 0 // Cây EF không mang sẵn level; sẽ là 0 nếu route không tính theo CTE.
            }); // Kết thúc ToDictionary.

        var roots = new List<CommentTreeDto>(); // Danh sách gốc trả về (có thể nhiều cây).

        foreach (var comment in comments.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)) // Duyệt ổn định theo thời gian rồi Id.
        { // Mở khối.
            var node = lookup[comment.Id]; // Lấy nút tương ứng Id hiện tại.

            if (comment.ParentId is null) // Không có cha → gốc logic.
            { // Mở khối.
                roots.Add(node); // Đưa vào danh sách gốc.
                continue; // Bỏ qua bước gắn con.
            } // Hết nhánh gốc.

            if (!lookup.TryGetValue(comment.ParentId.Value, out var parent)) // Cha không có trong tập (dữ liệu lệch).
            { // Mở khối.
                roots.Add(node); // Coi như gốc để không mất nút.
                continue; // Bỏ gắn.
            } // Hết cha thiếu.

            if (HasCycleFlat(comment.Id, comments)) // Phát hiện chu kỳ parent.
            { // Mở khối.
                roots.Add(node); // Tách nút ra gốc để tránh vòng lặp vô hạn.
                continue; // Không gắn vào cây có chu kỳ.
            } // Hết nhánh chu kỳ.

            parent.Children.Add(node); // Gắn nút hiện tại vào Children của cha.
        } // Hết foreach.

        return roots; // Rừng các cây độc lập.
    } // Kết thúc BuildTreeFlat.

    // Leo chuỗi ParentId từ commentId để phát hiện chu kỳ hoặc đường đi lặp trong tập hữu hạn.
    private static bool HasCycleFlat(Guid commentId, List<Comment> comments) // Id xuất phát và tập entity.
    { // Mở khối HasCycleFlat.
        var map = comments.ToDictionary(x => x.Id, x => x); // Id → entity để leo cha.
        var visited = new HashSet<Guid>(); // Đã thăm Id cha trên đường leo.
        Guid? currentParentId = map[commentId].ParentId; // Bắt đầu từ cha của commentId.

        while (currentParentId is not null) // Còn cha để leo.
        { // Mở khối.
            if (!map.TryGetValue(currentParentId.Value, out var current)) // Cha không tồn tại trong tập.
            { // Mở khối.
                return false; // Không coi là chu kỳ.
            } // Hết cha lạc.

            if (current.Id == commentId) // Gặp lại chính Id ban đầu.
            { // Mở khối.
                return true; // Chu kỳ.
            } // Hết nhánh đóng vòng.

            if (!visited.Add(current.Id)) // Add false → đã có trong set.
            { // Mở khối.
                return true; // Chu kỳ/đường đi lặp.
            } // Hết nhánh visited.

            currentParentId = current.ParentId; // Leo lên một tầng.
        } // Hết while.

        return false; // Hết chuỗi cha, không chu kỳ.
    } // Kết thúc HasCycleFlat.

    // Dựng tree CTE cho một post từ danh sách hàng phẳng có Level.
    private static List<CommentTreeDto> BuildTreeCte(List<CommentFlatDto> rows) // Input là các dòng phẳng CTE của một post hoặc đã tách theo post.
    { // Mở khối BuildTreeCte.
        // Tạo lookup Id -> node đích để gắn liên kết cha-con nhanh O(1).
        var lookup = rows.ToDictionary(
            x => x.Id, // Khóa dictionary theo Id comment.
            x => new CommentTreeDto // Tạo node cây từ mỗi row phẳng.
            { // Mở initializer node.
                Id = x.Id, // Ánh xạ khóa nút.
                Content = x.Content, // Ánh xạ nội dung.
                CreatedAt = x.CreatedAt, // Ánh xạ thời gian.
                PostId = x.PostId, // Ánh xạ bài viết.
                UserId = x.UserId, // Ánh xạ tác giả.
                ParentId = x.ParentId, // Ánh xạ liên kết cha.
                Level = x.Level // Giữ level CTE đã tính từ raw rows.
            }); // Kết thúc initializer node.

        var roots = new List<CommentTreeDto>(); // Danh sách gốc trả về sau cùng.
        // Duyệt theo Level trước để cha thường được xử lý trước con, rồi ổn định theo thời gian/Id.
        foreach (var row in rows.OrderBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id)) // Duyệt tất cả row theo thứ tự ổn định.
        { // Mở foreach dựng cây.
            var node = lookup[row.Id]; // Nút hiện tại tương ứng row.
            if (row.ParentId is null) // Nút gốc.
            { // Mở nhánh gốc.
                roots.Add(node); // Không có cha -> gốc.
                continue; // Đi node tiếp theo.
            } // Hết nhánh gốc.

            if (!lookup.TryGetValue(row.ParentId.Value, out var parent)) // Không tìm thấy cha trong lookup.
            { // Mở nhánh cha thiếu.
                roots.Add(node); // Cha thiếu trong tập -> nâng thành gốc để không mất dữ liệu.
                continue; // Bỏ gắn cha-con vì dữ liệu lệch.
            } // Hết nhánh cha thiếu.

            if (HasCycleCte(row.Id, rows)) // Phát hiện vòng lặp trên chuỗi parent.
            { // Mở nhánh cycle.
                roots.Add(node); // Có chu kỳ -> tách nút ra gốc, tránh gắn tạo vòng.
                continue; // Không gắn để tránh tạo vòng.
            } // Hết nhánh cycle.

            parent.Children.Add(node); // Gắn node vào danh sách con của cha.
        } // Hết foreach dựng cây.

        return roots; // Trả cây/rừng đã dựng.
    } // Kết thúc BuildTreeCte.

    // Dựng rừng tree CTE toàn cục từ raw rows của nhiều post.
    private static List<CommentTreeDto> BuildForestCte(List<CommentFlatDto> allRows) // Input là rows toàn cục nhiều post.
    { // Mở khối BuildForestCte.
        var forest = new List<CommentTreeDto>(); // Kết quả rừng toàn cục.
        // Tách theo từng post để không gắn chéo cây giữa các bài viết.
        foreach (var group in allRows.GroupBy(r => r.PostId).OrderBy(g => g.Key)) // Mỗi group đại diện một post.
        { // Mở foreach group theo post.
            var postRoots = BuildTreeCte(group.ToList()); // Dựng cây riêng cho từng post.
            foreach (var root in postRoots.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)) // Duyệt root theo thời gian/Id.
            { // Mở foreach root.
                forest.Add(root); // Thêm từng gốc vào rừng kết quả, giữ thứ tự ổn định.
            } // Hết foreach root.
        } // Hết foreach group.

        return forest; // Trả rừng gốc của nhiều post.
    } // Kết thúc BuildForestCte.

    // Kiểm tra cycle khi dựng tree CTE từ raw rows.
    private static bool HasCycleCte(Guid commentId, List<CommentFlatDto> rows) // commentId là nút cần kiểm tra chu kỳ.
    { // Mở khối HasCycleCte.
        var parentById = rows.ToDictionary(x => x.Id, x => x.ParentId); // Bản đồ Id -> ParentId để leo ngược.
        if (!parentById.ContainsKey(commentId)) // Không có node đích trong tập rows.
        { // Mở nhánh không có node.
            return false; // Id không có trong tập -> không đánh dấu cycle.
        } // Hết nhánh không có node.

        var visited = new HashSet<Guid>(); // Tập Id đã đi qua khi leo cha.
        Guid? parentId = parentById[commentId]; // Bắt đầu từ cha trực tiếp của nút đang kiểm tra.
        while (parentId is not null) // Leo dần lên cha đến khi gặp null.
        { // Mở while leo cha.
            if (parentId == commentId) // Quay về node ban đầu.
            { // Mở nhánh cycle trực tiếp.
                return true; // Quay lại chính nó -> cycle.
            } // Hết nhánh cycle trực tiếp.

            if (!visited.Add(parentId.Value)) // Id cha đã xuất hiện trước đó.
            { // Mở nhánh cycle do lặp.
                return true; // Gặp lại một cha đã thăm -> cycle/lặp vòng.
            } // Hết nhánh cycle do lặp.

            if (!parentById.TryGetValue(parentId.Value, out var nextParent)) // Không còn record cho cha hiện tại.
            { // Mở nhánh kết thúc không cycle.
                return false; // Leo tới nút cha không còn bản ghi -> chuỗi kết thúc, không cycle.
            } // Hết nhánh kết thúc không cycle.

            parentId = nextParent; // Tiếp tục leo lên cha kế tiếp.
        } // Hết while leo cha.

        return false; // Leo hết lên null mà không vòng -> hợp lệ.
    } // Kết thúc HasCycleCte.

    // Flatten preorder cho tree flat (không có Level trong response).
    private static void FlattenTreeFlat(CommentTreeDto node, ICollection<CommentFlatNoLevelDto> sink) // node là gốc subtree cần flatten.
    { // Mở khối FlattenTreeFlat.
        // Bước preorder: ghi node hiện tại trước.
        sink.Add(new CommentFlatNoLevelDto // Thêm node hiện tại vào output phẳng.
        { // Mở initializer DTO flat.
            Id = node.Id, // Id nút.
            Content = node.Content, // Nội dung nút.
            CreatedAt = node.CreatedAt, // Thời gian tạo nút.
            PostId = node.PostId, // Post của nút.
            UserId = node.UserId, // User của nút.
            ParentId = node.ParentId // Liên kết cha của nút.
        }); // Kết thúc initializer DTO flat.

        // Sau node hiện tại, duyệt con theo thứ tự ổn định và đệ quy preorder.
        foreach (var child in node.Children.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)) // Duyệt con theo thứ tự ổn định.
        { // Mở foreach flatten con.
            FlattenTreeFlat(child, sink); // Ghi toàn bộ nhánh con.
        } // Hết foreach flatten con.
    } // Kết thúc FlattenTreeFlat.

    // Flatten preorder cho tree CTE (giữ Level).
    private static void FlattenTreeCte(CommentTreeDto node, ICollection<CommentFlatDto> sink) // node là gốc subtree CTE cần flatten.
    { // Mở khối FlattenTreeCte.
        // Bước preorder: ghi node hiện tại trước, giữ nguyên Level của cây CTE.
        sink.Add(new CommentFlatDto // Thêm node hiện tại vào output phẳng có Level.
        { // Mở initializer DTO cte-flat.
            Id = node.Id, // Id nút.
            Content = node.Content, // Nội dung nút.
            CreatedAt = node.CreatedAt, // Thời gian tạo nút.
            PostId = node.PostId, // Post của nút.
            UserId = node.UserId, // User của nút.
            ParentId = node.ParentId, // Liên kết cha.
            Level = node.Level // Level được bảo toàn từ tree CTE.
        }); // Kết thúc initializer DTO cte-flat.

        // Duyệt con theo thứ tự ổn định để output phẳng nhất quán giữa các lần gọi.
        foreach (var child in node.Children.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)) // Duyệt con theo thứ tự ổn định.
        { // Mở foreach flatten con CTE.
            FlattenTreeCte(child, sink); // Flatten toàn bộ subtree con.
        } // Hết foreach flatten con CTE.
    } // Kết thúc FlattenTreeCte.

    // Map cây nội bộ sang DTO tree-flat không có Level trong response.
    private static CommentTreeFlatDto MapTreeFlat(CommentTreeDto node) // Map một node tree chung sang node tree-flat.
    { // Mở khối MapTreeFlat.
        // Map node hiện tại và đệ quy map toàn bộ children sang DTO không Level.
        return new CommentTreeFlatDto // Trả DTO tree-flat mới.
        { // Mở initializer tree-flat.
            Id = node.Id, // Id nút.
            Content = node.Content, // Nội dung.
            CreatedAt = node.CreatedAt, // Thời điểm tạo.
            PostId = node.PostId, // Post chứa nút.
            UserId = node.UserId, // Tác giả nút.
            ParentId = node.ParentId, // Id cha (nullable).
            Children = node.Children.Select(MapTreeFlat).ToList() // Đệ quy map toàn bộ con.
        }; // Kết thúc initializer tree-flat.
    } // Kết thúc MapTreeFlat.

    // Tập định danh cây con (gồm rootId) bằng BFS theo quan hệ ParentId trên danh sách phẳng một post.
    private static HashSet<Guid> BuildSubtreeIdSet(IReadOnlyList<Comment> inPost, Guid rootId) // Danh sách trong post và Id gốc.
    { // Mở khối BuildSubtreeIdSet.
        var s = new HashSet<Guid> { rootId }; // Tập đã thăm/kết quả; khởi tạo với gốc.
        var q = new Queue<Guid>(); // Hàng đợi BFS.
        q.Enqueue(rootId); // Đưa gốc vào hàng đợi.
        while (q.Count > 0) // Còn nút xử lý.
        { // Mở khối.
            var u = q.Dequeue(); // Lấy Id cha hiện tại.
            foreach (var n in inPost) // Quét toàn list phẳng (O(n) mỗi tầng).
            { // Mở khối.
                if (n.ParentId == u && s.Add(n.Id)) // Con trực tiếp và Id con mới (Add trả true nếu chưa có).
                { // Mở khối.
                    q.Enqueue(n.Id); // Đưa con vào hàng đợi.
                } // Hết nhánh con hợp lệ.
            } // Hết foreach.
        } // Hết while BFS.

        return s; // Trả tập Id cây con.
    } // Kết thúc BuildSubtreeIdSet.

    #endregion
} // Kết thúc lớp CommentService và không gian tệp.
