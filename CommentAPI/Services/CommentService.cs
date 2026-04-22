using AutoMapper; 
using CommentAPI; 
using CommentAPI.DTOs; 
using CommentAPI.Entities; 
using CommentAPI.Interfaces; 
using Microsoft.AspNetCore.Http; 

namespace CommentAPI.Services; 

public class CommentService : ICommentService // Lớp dịch vụ triển khai ICommentService.
{ // Mở khối thân lớp CommentService.
    private const int MaxCommentsToComputeLevels = 25_000; // Ngưỡng số comment tối đa để còn tính Level đệ quy an toàn.

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

    // Lấy danh sách comment phân trang toàn hệ thống, có cache theo trang.
    public async Task<PagedResult<CommentDto>> GetAllPagedAsync( // Khai báo phương thức phân trang toàn cục (CommentDto).
        int page, // Số trang (1-based).
        int pageSize, // Số bản ghi mỗi trang.
        CancellationToken cancellationToken = default) // Hủy bất đồng bộ.
    { // Mở khối GetAllPagedAsync.
        var cacheKey = EntityCacheKeys.CommentsAll(page, pageSize); // Chuỗi khóa cache theo số trang và cỡ trang.
        var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken); // Đọc JSON từ cache; không SQL.
        if (cached is not null) // Có bản trong cache.
        { // Mở khối.
            return cached; // Trả ngay, bỏ qua repository.
        } // Kết thúc nhánh cache hit.

        var (items, total) = await _repository.GetPagedAsync(page, pageSize, cancellationToken); // Gọi COUNT + SELECT trang trong repository.
        var result = new PagedResult<CommentDto> // Tạo object kết quả phân trang API.
        { // Mở khối.
            Items = items.Select(_mapper.Map<CommentDto>).ToList(), // Biến mỗi Comment thành CommentDto trong RAM (LINQ to Objects, không SQL).
            Page = page, // Ghi số trang hiện tại.
            PageSize = pageSize, // Ghi cỡ trang.
            TotalCount = total // Tổng bản ghi từ COUNT repository.
        }; // Kết thúc khởi tạo PagedResult.
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken); // Ghi JSON vào cache cho lần sau.
        return result; // Trả về cho controller.
    } // Kết thúc GetAllPagedAsync.

    // Lấy hết comment phẳng của một post một lần — không cache (tránh lệch với thay đổi tần suất cao); không cắt MaxPageSize như phân trang.
    public async Task<IReadOnlyList<CommentDto>> GetAllByPostIdAsync( // Toàn bộ DTO theo PostId.
        Guid postId, // Bài viết.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetAllByPostIdAsync.
        await EnsurePostExistsAsync(postId); // 404 nếu post không tồn tại.
        var entities = await _repository.GetByPostIdAsync(postId); // Một SELECT toàn comment thuộc post (AsNoTracking trong repo).
        return entities.Select(_mapper.Map<CommentDto>).ToList(); // Map sang DTO, trả IReadOnlyList qua List.
    } // Kết thúc GetAllByPostIdAsync.

    // Tìm comment theo nội dung (toàn hệ thống), phân trang và cache.
    public async Task<PagedResult<CommentDto>> SearchByContentPagedAsync( // Khai báo tìm kiếm nội dung toàn hệ thống.
        string? content, // Chuỗi tìm kiếm có thể null.
        int page, // Số trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Token hủy.
    { // Mở khối SearchByContentPagedAsync.
        var term = RequireSearchTerm(content); // Cắt khoảng trắng; ném lỗi nếu rỗng (không SQL).
        var cacheKey = EntityCacheKeys.CommentsSearchContent(EntityCacheHash.SearchTerm(term), page, pageSize); // Khóa gồm băm term để khóa ngắn.
        var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken); // Thử đọc cache.
        if (cached is not null) // Trúng cache.
        { // Mở khối.
            return cached; // Trả kết quả đã lưu.
        } // Hết nhánh cache.

        var (items, total) = await _repository.SearchByContentPagedAsync(term, page, pageSize, cancellationToken); // COUNT + SELECT có WHERE Contains.
        var result = new PagedResult<CommentDto> // Gói trang kết quả.
        { // Mở khối.
            Items = items.Select(_mapper.Map<CommentDto>).ToList(), // Map từng phần tử trong bộ nhớ.
            Page = page, // Chỉ số trang.
            PageSize = pageSize, // Kích thước trang.
            TotalCount = total // Tổng khớp tìm kiếm.
        }; // Kết thúc object initializer.
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken); // Ghi cache.
        return result; // Trả về cho caller.
    } // Kết thúc SearchByContentPagedAsync.

    // Đọc một comment theo Id trong phạm vi một bài viết; 404 nếu không tồn tại.
    public async Task<CommentDto> GetByIdInPostAsync( // Khai báo đọc comment trong một post.
        Guid postId, // Định danh bài viết chứa comment.
        Guid commentId, // Định danh comment cần đọc.
        CancellationToken cancellationToken = default) // Token hủy thao tác bất đồng bộ.
    { // Mở khối GetByIdInPostAsync.
        await EnsurePostExistsAsync(postId); // Gọi Any Post — một truy vấn SQL trong repository.
        var dto = await _repository.GetByIdForReadInPostAsync(postId, commentId, cancellationToken); // SELECT projection một dòng hoặc null.
        if (dto is null) // Không có comment đó trong post.
        { // Mở khối.
            throw new ApiException( // Ném lỗi HTTP 404 thống nhất API.
                StatusCodes.Status404NotFound, // Mã 404.
                ApiErrorCodes.CommentNotFound, // Mã lỗi nghiệp vụ.
                ApiMessages.CommentNotFound); // Thông điệp hiển thị.
        } // Kết thúc nhánh null.

        return dto; // Trả DTO đã đọc.
    } // Kết thúc GetByIdInPostAsync.

    // Tìm comment theo nội dung trong một bài viết, phân trang và cache.
    public async Task<PagedResult<CommentDto>> SearchByContentInPostPagedAsync( // Khai báo tìm theo nội dung trong post.
        Guid postId, // Bài viết giới hạn phạm vi tìm kiếm.
        string? content, // Chuỗi tìm kiếm.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Token hủy.
    { // Mở khối SearchByContentInPostPagedAsync.
        var term = RequireSearchTerm(content); // Chuẩn hóa term.
        var cacheKey = EntityCacheKeys.CommentsSearchContentInPost(postId, EntityCacheHash.SearchTerm(term), page, pageSize); // Khóa có postId.
        var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken); // Đọc cache nếu có.
        if (cached is not null) // Cache hit.
        { // Mở khối.
            return cached; // Trả ngay.
        } // Hết nhánh cache.

        await EnsurePostExistsAsync(postId); // Kiểm tra post tồn tại (SQL Any).
        var (items, total) = await _repository.SearchByContentInPostPagedAsync(postId, term, page, pageSize, cancellationToken); // COUNT + SELECT trong post.
        var result = new PagedResult<CommentDto> // Đối tượng phân trang.
        { // Mở khối.
            Items = items.Select(_mapper.Map<CommentDto>).ToList(), // Ánh xạ sang DTO.
            Page = page, // Trang hiện tại.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng bản ghi khớp.
        }; // Kết thúc initializer.
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken); // Lưu cache.
        return result; // Trả kết quả.
    } // Kết thúc SearchByContentInPostPagedAsync.

    // Đọc một comment theo Id với cache; 404 nếu không có.
    public async Task<CommentDto> GetByIdAsync(Guid id) // Khóa chính comment.
    { // Mở khối GetByIdAsync.
        var cacheKey = EntityCacheKeys.Comment(id); // Khóa theo Guid comment.
        var cached = await _cache.GetJsonAsync<CommentDto>(cacheKey, CancellationToken.None); // Đọc cache; CancellationToken.None cố định như code cũ.
        if (cached is not null) // Đã có trong cache.
        { // Mở khối.
            return cached; // Trả ngay không truy vấn DB.
        } // Hết nhánh cache.

        var dto = await _repository.GetByIdForReadAsync(id, default); // SELECT projection một comment.
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

    // Tạo comment mới sau khi kiểm tra post, user và parent hợp lệ.
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
        var inOldFlat = await _repository.GetByPostIdAsync(root.PostId); // List comment một post.
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
            var tracked = await _repository.GetByPostIdTrackedAsync(oldPostId, default); // Nạp tracked toàn post cũ.
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

    // Xóa một comment và toàn bộ hậu duệ trong cùng post; vô hiệu cache liên quan.
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

        var allCommentsInPost = await _repository.GetByPostIdAsync(entity.PostId); // SELECT toàn comment của post vào RAM.
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

    // Alias phân trang phẳng toàn cục: cùng implementation với GetAllPagedAsync.
    public Task<PagedResult<CommentDto>> GetAllFlatPagedAsync( // Alias phân trang phẳng (Task đồng bộ hóa với GetAllPagedAsync).
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetAllFlatPagedAsync.
        return GetAllPagedAsync(page, pageSize, cancellationToken); // Ủy quyền hoàn toàn — cùng SQL và cache.
    } // Kết thúc GetAllFlatPagedAsync.

    // Phân trang comment dạng cây (gốc toàn hệ thống), dựng bằng EF trong bộ nhớ.
    public async Task<PagedResult<CommentTreeDto>> GetAllTreePagedAsync( // Phân trang cây toàn hệ thống (EF).
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetAllTreePagedAsync.
        var cacheKey = EntityCacheKeys.CommentsAllTreeFlat(page, pageSize); // Khóa cây EF theo trang.
        var cached = await _cache.GetJsonAsync<PagedResult<CommentTreeDto>>(cacheKey, cancellationToken); // Thử cache.
        if (cached is not null) // Hit.
        { // Mở khối.
            return cached; // Trả ngay.
        } // Hết cache.

        var (roots, total) = await _repository.GetRootCommentsPagedAsync(page, pageSize, cancellationToken); // COUNT + SELECT trang gốc.
        var trees = await BuildSubtreesForRootsAsync(roots, cancellationToken); // Thêm query nạp comment theo post + dựng cây RAM.
        var result = new PagedResult<CommentTreeDto> // Gói phân trang.
        { // Mở khối.
            Items = trees, // Danh sách cây đã dựng.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng số gốc (theo phân trang gốc).
        }; // Kết thúc initializer.
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken); // Lưu cache.
        return result; // Trả kết quả.
    } // Kết thúc GetAllTreePagedAsync.

    // Phân trang danh sách phẳng có Level; tên CTE chỉ phản ánh endpoint, dữ liệu từ EF phân trang.
    public async Task<PagedResult<CommentFlatDto>> GetAllCteFlatPagedAsync( // Phân trang phẳng có Level (tên CTE, dữ liệu EF).
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetAllCteFlatPagedAsync.
        var cacheKey = EntityCacheKeys.CommentsAllCteFlat(page, pageSize); // Khóa cache.
        var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken); // Đọc cache.
        if (cached is not null) // Hit.
        { // Mở khối.
            return cached; // Trả ngay.
        } // Hết cache.

        var (items, total) = await _repository.GetPagedAsync(page, pageSize, cancellationToken); // Hai query EF phân trang.
        var flats = await ToCommentFlatDtosAsync(items, cancellationToken); // Có thể thêm query nạp đủ post để tính Level.
        var result = new PagedResult<CommentFlatDto> // Gói phân trang.
        { // Mở khối.
            Items = flats, // Dòng phẳng có Level.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng bản ghi.
        }; // Kết thúc initializer.
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken); // Ghi cache.
        return result; // Trả về.
    } // Kết thúc GetAllCteFlatPagedAsync.

    // Alias cây “CTE”: thực tế gọi GetAllTreePagedAsync (cây EF).
    public Task<PagedResult<CommentTreeDto>> GetAllCteTreePagedAsync( // Alias cây CTE → ủy quyền GetAllTreePagedAsync.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetAllCteTreePagedAsync.
        return GetAllTreePagedAsync(page, pageSize, cancellationToken); // Không gọi CTE SQL — dùng cây EF.
    } // Kết thúc GetAllCteTreePagedAsync.

    // Làm phẳng rừng cây EF (preorder) theo trang gốc toàn hệ thống.
    public async Task<PagedResult<CommentFlatDto>> GetFlattenedForestPagedAsync( // Rừng EF làm phẳng preorder, phân trang theo gốc.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetFlattenedForestPagedAsync.
        var cacheKey = EntityCacheKeys.CommentsAllFlattenEfTree(page, pageSize); // Khóa cache.
        var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken); // Đọc cache.
        if (cached is not null) // Hit.
        { // Mở khối.
            return cached; // Trả ngay.
        } // Hết cache.

        var (roots, total) = await _repository.GetRootCommentsPagedAsync(page, pageSize, cancellationToken); // Trang gốc.
        var trees = await BuildSubtreesForRootsAsync(roots, cancellationToken); // Dựng subtree đầy đủ cho mỗi gốc.
        var flat = FlattenForestPreorder(trees); // Duyệt DFS gán Level — chỉ CPU.
        var result = new PagedResult<CommentFlatDto> // Gói phân trang.
        { // Mở khối.
            Items = flat, // Danh sách phẳng có Level.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng gốc (theo phân trang gốc).
        }; // Kết thúc initializer.
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken); // Ghi cache.
        return result; // Trả về.
    } // Kết thúc GetFlattenedForestPagedAsync.

    // Làm phẳng toàn bộ cây bằng CTE SQL, rồi phân trang trong bộ nhớ (Skip/Take).
    public async Task<PagedResult<CommentFlatDto>> GetFlattenedFromCtePagedAsync( // CTE toàn cục rồi cắt trang trong RAM.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetFlattenedFromCtePagedAsync.
        var cacheKey = EntityCacheKeys.CommentsAllFlattenCteTree(page, pageSize); // Khóa cache.
        var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken); // Đọc cache.
        if (cached is not null) // Hit.
        { // Mở khối.
            return cached; // Trả ngay.
        } // Hết cache.

        var allRows = await _repository.GetTreeRowsByCteAllAsync(); // Một (hoặc vài) lệnh ADO CTE toàn cục.
        var flatList = BuildGlobalFlatFromCteAllRows(allRows); // Nhóm post + cây + flatten trong RAM.
        var total = flatList.Count; // Tổng số dòng phẳng (int) từ Count collection.
        var slice = flatList // Nguồn cho biểu thức Skip/Take (phân trang trong RAM).
            .Skip((page - 1) * pageSize) // Bỏ các dòng của trang trước (LINQ to Objects).
            .Take(pageSize) // Lấy đúng pageSize phần tử.
            .ToList(); // List vật lý cho response.
        var result = new PagedResult<CommentFlatDto> // Gói phân trang.
        { // Mở khối.
            Items = slice, // Trang con của danh sách phẳng.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng dòng phẳng toàn cục.
        }; // Kết thúc initializer.
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken); // Ghi cache.
        return result; // Trả về.
    } // Kết thúc GetFlattenedFromCtePagedAsync.

    // Phân trang comment phẳng (DTO cơ bản) theo một post.
    public async Task<PagedResult<CommentDto>> GetFlatByPostIdPagedAsync( // Phân trang DTO phẳng theo post.
        Guid postId, // Bài viết.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetFlatByPostIdPagedAsync.
        var cacheKey = EntityCacheKeys.CommentsFlatByPost(postId, page, pageSize); // Khóa cache.
        var cached = await _cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken); // Đọc cache.
        if (cached is not null) // Hit.
        { // Mở khối.
            return cached; // Trả ngay.
        } // Hết cache.

        await EnsurePostExistsAsync(postId); // Any post.
        var (items, total) = await _repository.GetByPostIdPagedAsync(postId, page, pageSize, cancellationToken); // COUNT + SELECT trong post.
        var result = new PagedResult<CommentDto> // Gói phân trang.
        { // Mở khối.
            Items = items.Select(_mapper.Map<CommentDto>).ToList(), // Map sang DTO.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng comment post.
        }; // Kết thúc initializer.
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken); // Ghi cache.
        return result; // Trả về.
    } // Kết thúc GetFlatByPostIdPagedAsync.

    // Phân trang phẳng có Level trong một post (tính Level qua ToCommentFlatDtosAsync).
    public async Task<PagedResult<CommentFlatDto>> GetCteFlatByPostIdPagedAsync( // Phân trang CommentFlatDto theo post.
        Guid postId, // Bài viết.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetCteFlatByPostIdPagedAsync.
        var cacheKey = EntityCacheKeys.CommentsCteFlatByPost(postId, page, pageSize); // Khóa cache.
        var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken); // Đọc cache.
        if (cached is not null) // Hit.
        { // Mở khối.
            return cached; // Trả ngay.
        } // Hết cache.

        await EnsurePostExistsAsync(postId); // Kiểm tra post tồn tại.
        var (items, total) = await _repository.GetByPostIdPagedAsync(postId, page, pageSize, cancellationToken); // Phân trang EF trong post.
        var flats = await ToCommentFlatDtosAsync(items, cancellationToken); // Bổ sung query + tính Level.
        var result = new PagedResult<CommentFlatDto> // Gói phân trang.
        { // Mở khối.
            Items = flats, // Dòng phẳng có Level.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng bản ghi post.
        }; // Kết thúc initializer.
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken); // Ghi cache.
        return result; // Trả về.
    } // Kết thúc GetCteFlatByPostIdPagedAsync.

    // Phân trang cây theo gốc trong một post (dựng cây EF; lặp BuildTree theo từng gốc trang).
    public async Task<PagedResult<CommentTreeDto>> GetTreeByPostIdPagedAsync( // Cây theo post, phân trang gốc.
        Guid postId, // Bài viết.
        int page, // Trang gốc.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetTreeByPostIdPagedAsync.
        var cacheKey = EntityCacheKeys.CommentsTreeByPost(postId, page, pageSize); // Khóa cache.
        var cached = await _cache.GetJsonAsync<PagedResult<CommentTreeDto>>(cacheKey, cancellationToken); // Đọc cache.
        if (cached is not null) // Hit.
        { // Mở khối.
            return cached; // Trả ngay.
        } // Hết cache.

        await EnsurePostExistsAsync(postId); // Kiểm tra post.
        var (roots, total) = await _repository.GetRootsByPostIdPagedAsync(postId, page, pageSize, cancellationToken); // Trang gốc trong post.
        var allInPost = await _repository.GetByPostIdAsync(postId); // Toàn bộ comment của post — một SELECT lớn.
        var trees = new List<CommentTreeDto>(); // Chứa cây con ứng mỗi gốc trang.
        foreach (var root in roots) // Duyệt từng gốc trong trang hiện tại.
        { // Mở khối.
            var forest = BuildTreeFromComments(allInPost); // Dựng toàn rừng từ list phẳng (lặp lại mỗi gốc — chi phí CPU).
            var node = forest.FirstOrDefault(t => t.Id == root.Id); // Tìm nút gốc tương ứng trong rừng vừa build.
            if (node is not null) // Tìm thấy nút.
            { // Mở khối.
                trees.Add(node); // Thêm subtree đầy đủ vào kết quả.
            } // Hết nhánh tìm thấy.
        } // Hết foreach roots.

        var result = new PagedResult<CommentTreeDto> // Gói phân trang.
        { // Mở khối.
            Items = trees, // Danh sách cây.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng gốc trong post.
        }; // Kết thúc initializer.
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken); // Ghi cache.
        return result; // Trả về.
    } // Kết thúc GetTreeByPostIdPagedAsync.

    // Alias “CTE tree”: ủy quyền sang GetTreeByPostIdPagedAsync.
    public Task<PagedResult<CommentTreeDto>> GetCteTreeByPostIdPagedAsync( // Alias → GetTreeByPostIdPagedAsync.
        Guid postId, // Bài viết.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetCteTreeByPostIdPagedAsync.
        return GetTreeByPostIdPagedAsync(postId, page, pageSize, cancellationToken); // Cùng implementation EF.
    } // Kết thúc GetCteTreeByPostIdPagedAsync.

    // Làm phẳng cây EF trong một post theo trang gốc (một lần dựng rừng, trích subtree theo gốc trang).
    public async Task<PagedResult<CommentFlatDto>> GetFlattenedTreeByPostIdPagedAsync( // Làm phẳng cây EF trong post.
        Guid postId, // Bài viết.
        int page, // Trang gốc.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetFlattenedTreeByPostIdPagedAsync.
        var cacheKey = EntityCacheKeys.CommentsFlattenedEfTreeByPost(postId, page, pageSize); // Khóa cache.
        var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken); // Đọc cache.
        if (cached is not null) // Hit.
        { // Mở khối.
            return cached; // Trả ngay.
        } // Hết cache.

        await EnsurePostExistsAsync(postId); // Kiểm tra post.
        var (roots, total) = await _repository.GetRootsByPostIdPagedAsync(postId, page, pageSize, cancellationToken); // Trang gốc.
        var allInPost = await _repository.GetByPostIdAsync(postId); // Nạp toàn comment post.
        var forest = BuildTreeFromComments(allInPost); // Một lần dựng rừng cho cả post.
        var trees = new List<CommentTreeDto>(); // Subtree theo từng gốc trang.
        foreach (var root in roots) // Duyệt gốc trang.
        { // Mở khối.
            var node = forest.FirstOrDefault(t => t.Id == root.Id); // Tìm subtree theo gốc trang.
            if (node is not null) // Có nút.
            { // Mở khối.
                trees.Add(node); // Thu thập subtree.
            } // Hết nhánh.
        } // Hết foreach.

        var flat = FlattenForestPreorder(trees); // Preorder → danh sách CommentFlatDto.
        var result = new PagedResult<CommentFlatDto> // Gói phân trang.
        { // Mở khối.
            Items = flat, // Dòng phẳng.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng gốc post.
        }; // Kết thúc initializer.
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken); // Ghi cache.
        return result; // Trả về.
    } // Kết thúc GetFlattenedTreeByPostIdPagedAsync.

    // CTE theo post: dựng cây từ hàng SQL, làm phẳng, phân trang trong RAM.
    public async Task<PagedResult<CommentFlatDto>> GetFlattenedCteTreeByPostIdPagedAsync( // CTE một post, phẳng, cắt trang.
        Guid postId, // Bài viết.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetFlattenedCteTreeByPostIdPagedAsync.
        var cacheKey = EntityCacheKeys.CommentsFlattenedCteTree(postId, page, pageSize); // Khóa cache.
        var cached = await _cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken); // Đọc cache.
        if (cached is not null) // Hit.
        { // Mở khối.
            return cached; // Trả ngay.
        } // Hết cache.

        await EnsurePostExistsAsync(postId); // Kiểm tra post.

        var cteRows = await _repository.GetTreeRowsByCteAsync(postId); // ADO CTE một post.
        var roots = BuildTreeFromFlatDtosForOnePost(cteRows); // Dựng cây từ hàng có Level.
        var flatList = FlattenForestPreorder(roots); // Làm phẳng preorder.
        var total = flatList.Count; // Tổng dòng phẳng.
        var slice = flatList // Nguồn cho Skip/Take sau khi làm phẳng CTE.
            .Skip((page - 1) * pageSize) // Bỏ trang trước.
            .Take(pageSize) // Lấy cỡ trang.
            .ToList(); // Materialize.
        var result = new PagedResult<CommentFlatDto> // Gói phân trang.
        { // Mở khối.
            Items = slice, // Trang con.
            Page = page, // Trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng dòng.
        }; // Kết thúc initializer.
        await _cache.SetJsonAsync(cacheKey, result, cancellationToken); // Ghi cache.
        return result; // Trả về.
    } // Kết thúc GetFlattenedCteTreeByPostIdPagedAsync.

    // Demo lazy loading: ủy quyền repository; có thể phát sinh truy vấn bổ sung khi đọc navigation.
    public async Task<CommentLoadingDemoDto> GetCommentLazyLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default) // Id comment.
    { // Mở khối GetCommentLazyLoadingDemoAsync.
        var dto = await _repository.GetCommentLazyLoadingDemoAsync(id, cancellationToken); // Ủy quyền repository (SQL + lazy tiềm ẩn).
        if (dto is null) // Không tìm thấy.
        { // Mở khối.
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.CommentNotFound, // Mã.
                ApiMessages.CommentNotFound); // Thông báo.
        } // Hết null.

        return dto; // Trả DTO demo.
    } // Kết thúc GetCommentLazyLoadingDemoAsync.

    // Demo eager loading: ủy quyền repository (Include + split query).
    public async Task<CommentLoadingDemoDto> GetCommentEagerLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default) // Id comment.
    { // Mở khối GetCommentEagerLoadingDemoAsync.
        var dto = await _repository.GetCommentEagerLoadingDemoAsync(id, cancellationToken); // Nạp quan hệ trong ít round-trip có kiểm soát.
        if (dto is null) // Không tìm thấy.
        { // Mở khối.
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.CommentNotFound, // Mã.
                ApiMessages.CommentNotFound); // Thông báo.
        } // Hết null.

        return dto; // Trả DTO demo.
    } // Kết thúc GetCommentEagerLoadingDemoAsync.

    // Demo explicit loading: ủy quyền repository (LoadAsync từng reference/collection).
    public async Task<CommentLoadingDemoDto> GetCommentExplicitLoadingDemoAsync(Guid id, CancellationToken cancellationToken = default) // Id comment.
    { // Mở khối GetCommentExplicitLoadingDemoAsync.
        var dto = await _repository.GetCommentExplicitLoadingDemoAsync(id, cancellationToken); // Nạp có điều khiển sau truy vấn đầu.
        if (dto is null) // Không tìm thấy.
        { // Mở khối.
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.CommentNotFound, // Mã.
                ApiMessages.CommentNotFound); // Thông báo.
        } // Hết null.

        return dto; // Trả DTO demo.
    } // Kết thúc GetCommentExplicitLoadingDemoAsync.

    // Demo projection: ủy quyền repository (một truy vấn chiếu DTO).
    public async Task<CommentLoadingDemoDto> GetCommentProjectionDemoAsync(Guid id, CancellationToken cancellationToken = default) // Id comment.
    { // Mở khối GetCommentProjectionDemoAsync.
        var dto = await _repository.GetCommentProjectionDemoAsync(id, cancellationToken); // SELECT chiếu thẳng sang DTO.
        if (dto is null) // Không tìm thấy.
        { // Mở khối.
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.CommentNotFound, // Mã.
                ApiMessages.CommentNotFound); // Thông báo.
        } // Hết null.

        return dto; // Trả DTO demo.
    } // Kết thúc GetCommentProjectionDemoAsync.

    // Demo phân trang lazy: normalize rồi gọi repository.
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsLazyLoadingDemoPagedAsync( // Demo phân trang lazy.
        int page, // Trang yêu cầu.
        int pageSize, // Cỡ trang yêu cầu.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetCommentsLazyLoadingDemoPagedAsync.
        var (p, s) = PaginationQuery.Normalize(page, pageSize); // Chuẩn hóa số trang/cỡ trang (không SQL).
        var (items, total) = await _repository.GetCommentsLazyLoadingDemoPagedAsync(p, s, cancellationToken); // COUNT + SELECT + lazy tiềm ẩn trong repo.
        return new PagedResult<CommentLoadingDemoDto> // Gói kết quả.
        { // Mở khối.
            Items = items, // Danh sách đã map từ repository.
            Page = p, // Trang đã chuẩn hóa.
            PageSize = s, // Cỡ trang đã chuẩn hóa.
            TotalCount = total // Tổng bản ghi.
        }; // Kết thúc object initializer.
    } // Kết thúc GetCommentsLazyLoadingDemoPagedAsync.

    // Demo phân trang eager: normalize rồi gọi repository.
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsEagerLoadingDemoPagedAsync( // Demo phân trang eager.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetCommentsEagerLoadingDemoPagedAsync.
        var (p, s) = PaginationQuery.Normalize(page, pageSize); // Chuẩn hóa tham số phân trang.
        var (items, total) = await _repository.GetCommentsEagerLoadingDemoPagedAsync(p, s, cancellationToken); // Include + phân trang.
        return new PagedResult<CommentLoadingDemoDto> // Gói kết quả.
        { // Mở khối.
            Items = items, // Dòng demo.
            Page = p, // Trang.
            PageSize = s, // Cỡ trang.
            TotalCount = total // Tổng.
        }; // Kết thúc initializer.
    } // Kết thúc GetCommentsEagerLoadingDemoPagedAsync.

    // Demo phân trang explicit loading.
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsExplicitLoadingDemoPagedAsync( // Demo phân trang explicit.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetCommentsExplicitLoadingDemoPagedAsync.
        var (p, s) = PaginationQuery.Normalize(page, pageSize); // Chuẩn hóa.
        var (items, total) = await _repository.GetCommentsExplicitLoadingDemoPagedAsync(p, s, cancellationToken); // LoadAsync sau phân trang.
        return new PagedResult<CommentLoadingDemoDto> // Gói kết quả.
        { // Mở khối.
            Items = items, // Dòng demo.
            Page = p, // Trang.
            PageSize = s, // Cỡ trang.
            TotalCount = total // Tổng.
        }; // Kết thúc initializer.
    } // Kết thúc GetCommentsExplicitLoadingDemoPagedAsync.

    // Demo phân trang projection (DTO ngay trong SQL).
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsProjectionDemoPagedAsync( // Demo phân trang projection.
        int page, // Trang.
        int pageSize, // Cỡ trang.
        CancellationToken cancellationToken = default) // Hủy.
    { // Mở khối GetCommentsProjectionDemoPagedAsync.
        var (p, s) = PaginationQuery.Normalize(page, pageSize); // Chuẩn hóa.
        var (items, total) = await _repository.GetCommentsProjectionDemoPagedAsync(p, s, cancellationToken); // Select DTO phân trang.
        return new PagedResult<CommentLoadingDemoDto> // Gói kết quả.
        { // Mở khối.
            Items = items, // Dòng demo.
            Page = p, // Trang.
            PageSize = s, // Cỡ trang.
            TotalCount = total // Tổng.
        }; // Kết thúc initializer.
    } // Kết thúc GetCommentsProjectionDemoPagedAsync.

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

    // Với mỗi gốc trong trang: nạp comment các post liên quan và trích subtree tương ứng.
    private async Task<List<CommentTreeDto>> BuildSubtreesForRootsAsync( // Helper: dựng subtree cho danh sách gốc.
        List<Comment> roots, // Các comment gốc (ParentId null) của trang hiện tại.
        CancellationToken cancellationToken) // Hủy.
    { // Mở khối BuildSubtreesForRootsAsync.
        if (roots.Count == 0) // Không có gốc.
        { // Mở khối.
            return new List<CommentTreeDto>(); // Trả list rỗng, không gọi SQL.
        } // Hết nhánh rỗng.

        var postIds = roots.Select(r => r.PostId) // Lấy PostId từ mỗi gốc.
            .Distinct() // Loại trùng post.
            .ToList(); // Materialize danh sách Guid.
        var allInPosts = await _repository.GetCommentsForPostsAsync(postIds, cancellationToken); // Một SELECT IN PostId.
        var trees = new List<CommentTreeDto>(); // Kết quả cây theo thứ tự gốc.
        foreach (var root in roots) // Xử lý từng gốc trang.
        { // Mở khối.
            var inPost = allInPosts.Where(c => c.PostId == root.PostId).ToList(); // Lọc comment cùng post với gốc (RAM).
            var forest = BuildTreeFromComments(inPost); // Dựng rừng trong post.
            var node = forest.FirstOrDefault(t => t.Id == root.Id); // Tìm nút gốc trong rừng.
            if (node is not null) // Tìm thấy subtree.
            { // Mở khối.
                trees.Add(node); // Thu thập.
            } // Hết nhánh.
        } // Hết foreach.

        return trees; // Danh sách cây theo thứ tự gốc đầu vào.
    } // Kết thúc BuildSubtreesForRootsAsync.

    // Chuyển một trang entity Comment sang CommentFlatDto; tính Level nếu đủ nhỏ, ngược lại gán 0.
    private async Task<List<CommentFlatDto>> ToCommentFlatDtosAsync( // Helper: trang entity → CommentFlatDto có Level.
        List<Comment> pageItems, // Các bản ghi của trang hiện tại.
        CancellationToken cancellationToken) // Hủy.
    { // Mở khối ToCommentFlatDtosAsync.
        if (pageItems.Count == 0) // Trang rỗng.
        { // Mở khối.
            return new List<CommentFlatDto>(); // Không gọi DB thêm.
        } // Hết nhánh rỗng.

        var postIds = pageItems.Select(c => c.PostId).Distinct().ToList(); // Các post xuất hiện trong trang hiện tại.
        var allForPosts = await _repository.GetCommentsForPostsAsync(postIds, cancellationToken); // Nạp đủ comment các post để biết cây.

        if (allForPosts.Count > MaxCommentsToComputeLevels) // Quá nhiều bản ghi.
        { // Mở khối.
            return pageItems.Select(c => new CommentFlatDto // Chiếu từng phần tử trang sang DTO.
            { // Mở khối.
                Id = c.Id, // Khóa comment.
                Content = c.Content, // Nội dung.
                CreatedAt = c.CreatedAt, // Thời điểm tạo.
                PostId = c.PostId, // Bài viết.
                UserId = c.UserId, // Người viết.
                ParentId = c.ParentId, // Cha.
                Level = 0 // Không tính độ sâu — gán 0 an toàn.
            }).ToList(); // List kết quả.
        } // Hết nhánh ngưỡng.

        var byPost = allForPosts.GroupBy(x => x.PostId).ToDictionary(g => g.Key, g => g.ToList()); // Dictionary PostId → list comment trong post.
        var depthByPost = byPost.ToDictionary(kv => kv.Key, kv => BuildDepthById(kv.Value)); // Mỗi post → map Id → độ sâu.

        var list = new List<CommentFlatDto>(pageItems.Count); // Dự trữ đúng cỡ trang.
        foreach (var c in pageItems) // Duyệt từng phần tử trang.
        { // Mở khối.
            depthByPost[c.PostId].TryGetValue(c.Id, out var lv); // Lấy Level đã memo trong post tương ứng.
            list.Add(new CommentFlatDto // Thêm DTO phẳng.
            { // Mở khối.
                Id = c.Id, // Khóa.
                Content = c.Content, // Nội dung.
                CreatedAt = c.CreatedAt, // Thời gian.
                PostId = c.PostId, // Post.
                UserId = c.UserId, // User.
                ParentId = c.ParentId, // Cha.
                Level = lv // Độ sâu tính được hoặc 0 mặc định struct.
            }); // Kết thúc Add.
        } // Hết foreach.

        return list; // Danh sách đầy đủ Level (hoặc 0 khi vượt ngưỡng đã xử lý ở nhánh trên).
    } // Kết thúc ToCommentFlatDtosAsync.

    // Tính độ sâu từng Id trong một post bằng đệ quy có ghi nhớ (memoization).
    private static Dictionary<Guid, int> BuildDepthById(List<Comment> inPost) // Toàn comment một post.
    { // Mở khối BuildDepthById.
        var parentById = inPost.ToDictionary(c => c.Id, c => c.ParentId); // Map Id → ParentId nullable.
        var memo = new Dictionary<Guid, int>(); // Nhớ độ sâu đã tính.

        int Depth(Guid id) // Hàm local đệ quy có memo.
        { // Mở khối.
            if (memo.TryGetValue(id, out var d)) // Đã tính id này.
            { // Mở khối.
                return d; // Trả sâu đã lưu.
            } // Hết cache hit.

            if (!parentById.TryGetValue(id, out var p) || !p.HasValue) // Không có cha → gốc.
            { // Mở khối.
                memo[id] = 0; // Ghi nhớ độ sâu 0.
                return 0; // Gốc.
            } // Hết nhánh gốc.

            var v = 1 + Depth(p.Value); // Độ sâu = 1 + độ sâu cha.
            memo[id] = v; // Lưu memo.
            return v; // Trả độ sâu.
        } // Kết thúc local function Depth.

        foreach (var c in inPost) // Đảm bảo mọi Id trong list đều được tính.
        { // Mở khối.
            _ = Depth(c.Id); // Gọi Depth vì tác dụng phụ điền memo.
        } // Hết foreach.

        return memo; // Map Id → độ sâu.
    } // Kết thúc BuildDepthById.

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

    // Dựng rừng CommentTreeDto từ danh sách phẳng entity; xử lý dữ liệu lệch và chu kỳ bằng cách nâng nút lên gốc.
    private static List<CommentTreeDto> BuildTreeFromComments(List<Comment> comments) // Danh sách comment một hoặc nhiều cây.
    { // Mở khối BuildTreeFromComments.
        var lookup = comments.ToDictionary( // Từ Id → nút DTO trống chưa gắn con.
            x => x.Id, // Khóa dictionary.
            x => new CommentTreeDto // Khởi tạo nút lá/chưa có con.
            { // Mở khối.
                Id = x.Id, // Định danh.
                Content = x.Content, // Nội dung.
                CreatedAt = x.CreatedAt, // Thời gian.
                PostId = x.PostId, // Bài viết.
                UserId = x.UserId, // Người viết.
                ParentId = x.ParentId // Tham chiếu cha.
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

            if (CreatesCycleFromComments(comment.Id, comments)) // Phát hiện chu kỳ parent.
            { // Mở khối.
                roots.Add(node); // Tách nút ra gốc để tránh vòng lặp vô hạn.
                continue; // Không gắn vào cây có chu kỳ.
            } // Hết nhánh chu kỳ.

            parent.Children.Add(node); // Gắn nút hiện tại vào Children của cha.
        } // Hết foreach.

        return roots; // Rừng các cây độc lập.
    } // Kết thúc BuildTreeFromComments.

    // Leo chuỗi ParentId từ commentId để phát hiện chu kỳ hoặc đường đi lặp trong tập hữu hạn.
    private static bool CreatesCycleFromComments(Guid commentId, List<Comment> comments) // Id xuất phát và tập entity.
    { // Mở khối CreatesCycleFromComments.
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
    } // Kết thúc CreatesCycleFromComments.

    // Gom hàng CTE toàn cục theo PostId, dựng cây từng post rồi nối preorder thành một danh sách phẳng.
    private static List<CommentFlatDto> BuildGlobalFlatFromCteAllRows(List<CommentFlatDto> allRows) // Mọi hàng CTE đã map DTO.
    { // Mở khối BuildGlobalFlatFromCteAllRows.
        var result = new List<CommentFlatDto>(); // Phẳng toàn cục sẽ nối từng post.
        foreach (var group in allRows.GroupBy(r => r.PostId).OrderBy(g => g.Key)) // Nhóm theo PostId, sort key PostId.
        { // Mở khối.
            var roots = BuildTreeFromFlatDtosForOnePost(group.ToList()); // Dựng cây một post từ list hàng.
            result.AddRange(FlattenForestPreorder(roots)); // Nối preorder vào cuối result.
        } // Hết foreach nhóm.

        return result; // Danh sách phẳng toàn hệ thống.
    } // Kết thúc BuildGlobalFlatFromCteAllRows.

    // Dựng cây từ các hàng phẳng có Level (từ SQL); thứ tự duyệt ổn định theo Level rồi thời gian.
    private static List<CommentTreeDto> BuildTreeFromFlatDtosForOnePost(List<CommentFlatDto> rows) // Hàng một post.
    { // Mở khối BuildTreeFromFlatDtosForOnePost.
        var lookup = rows.ToDictionary( // Id → nút cây.
            x => x.Id, // Khóa.
            x => new CommentTreeDto // Nút.
            { // Mở khối.
                Id = x.Id, // Id.
                Content = x.Content, // Nội dung.
                CreatedAt = x.CreatedAt, // Thời gian.
                PostId = x.PostId, // Post.
                UserId = x.UserId, // User.
                ParentId = x.ParentId // Cha.
            }); // Kết thúc ToDictionary.

        var roots = new List<CommentTreeDto>(); // Gốc logic của post.

        foreach (var row in rows.OrderBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id)) // Thứ tự an toàn theo Level SQL.
        { // Mở khối.
            var node = lookup[row.Id]; // Nút hiện tại.

            if (row.ParentId is null) // Gốc.
            { // Mở khối.
                roots.Add(node); // Thêm gốc.
                continue; // Không gắn cha.
            } // Hết gốc.

            if (!lookup.TryGetValue(row.ParentId.Value, out var parent)) // Cha không có trong lookup.
            { // Mở khối.
                roots.Add(node); // Nâng lên gốc để không mất nút.
                continue; // Bỏ gắn.
            } // Hết cha thiếu.

            if (CreatesCycleFromFlatRows(row.Id, rows)) // Chu kỳ trên hàng phẳng.
            { // Mở khối.
                roots.Add(node); // Tách khỏi cây lỗi.
                continue; // Không gắn.
            } // Hết chu kỳ.

            parent.Children.Add(node); // Gắn con vào cha.
        } // Hết foreach.

        return roots; // Rừng cây (thường một cây hoặc nhiều gốc).
    } // Kết thúc BuildTreeFromFlatDtosForOnePost.

    // Duyệt từng cây gốc theo thứ tự ổn định; gọi DFS preorder để làm phẳng.
    private static List<CommentFlatDto> FlattenForestPreorder(List<CommentTreeDto> roots) // Danh sách gốc các cây.
    { // Mở khối FlattenForestPreorder.
        var result = new List<CommentFlatDto>(); // Danh sách phẳng đầu ra.
        foreach (var root in roots.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)) // Thứ tự gốc ổn định.
        { // Mở khối.
            VisitPreorder(root, 0, result); // DFS từ depth 0.
        } // Hết foreach.

        return result; // Rừng đã phẳng thành một list.
    } // Kết thúc FlattenForestPreorder.

    // DFS preorder: thêm nút với Level hiện tại, rồi đệ quy con theo thứ tự thời gian/Id.
    private static void VisitPreorder(CommentTreeDto node, int level, List<CommentFlatDto> sink) // Nút, độ sâu, đích ghi.
    { // Mở khối VisitPreorder.
        sink.Add(new CommentFlatDto // Thêm nút hiện tại vào danh sách phẳng.
        { // Mở khối.
            Id = node.Id, // Id.
            Content = node.Content, // Nội dung.
            CreatedAt = node.CreatedAt, // Thời gian.
            PostId = node.PostId, // Post.
            UserId = node.UserId, // User.
            ParentId = node.ParentId, // Cha.
            Level = level // Độ sâu DFS hiện tại.
        }); // Kết thúc Add.

        foreach (var child in node.Children.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id)) // Con sắp ổn định.
        { // Mở khối.
            VisitPreorder(child, level + 1, sink); // Đệ quy với level tăng 1.
        } // Hết foreach con.
    } // Kết thúc VisitPreorder.

    // Kiểm tra chu kỳ trên biểu đồ cha được mô tả bởi danh sách hàng phẳng (ParentId).
    private static bool CreatesCycleFromFlatRows(Guid commentId, List<CommentFlatDto> rows) // Id xuất phát và hàng.
    { // Mở khối CreatesCycleFromFlatRows.
        var parentById = rows.ToDictionary(x => x.Id, x => x.ParentId); // Id → ParentId.
        if (!parentById.ContainsKey(commentId)) // Không có nút trong map.
        { // Mở khối.
            return false; // Không leo được → không chu kỳ từ Id này.
        } // Hết thiếu khóa.

        var visited = new HashSet<Guid>(); // Đường đi các Id cha đã qua.
        Guid? parentId = parentById[commentId]; // Bắt đầu từ cha.

        while (parentId is not null) // Còn bậc leo.
        { // Mở khối.
            if (parentId == commentId) // Cha trỏ về chính nút xuất phát.
            { // Mở khối.
                return true; // Chu kỳ trực tiếp.
            } // Hết nhánh.

            if (!visited.Add(parentId.Value)) // Trùng đường đi.
            { // Mở khối.
                return true; // Chu kỳ gián tiếp.
            } // Hết nhánh.

            if (!parentById.TryGetValue(parentId.Value, out var nextParent)) // Hết chuỗi.
            { // Mở khối.
                return false; // Không còn cha trong tập.
            } // Hết chuỗi.

            parentId = nextParent; // Leo tiếp.
        } // Hết while.

        return false; // Đến gốc null an toàn.
    } // Kết thúc CreatesCycleFromFlatRows.
} // Kết thúc lớp CommentService và không gian tệp.
