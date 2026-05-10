using AutoMapper; // Ánh xạ giữa entity và DTO trong tầng dịch vụ.
using CommentAPI; // ApiException, mã lỗi, thông điệp dùng chung API.
using CommentAPI.DTOs; // Các kiểu dữ liệu truyền qua controller/service.
using CommentAPI.Entities; // Entity Comment và các thực thể liên quan.
using CommentAPI.Interfaces; // ICommentService, ICommentRepository, cache interface.
using CommentAPI.Repositories; // CommentRepository.CommentListSortDefault, SortCommentTreeCteRootsForPaging.
using Microsoft.AspNetCore.Http; // StatusCodes cho mã HTTP trong ApiException.

namespace CommentAPI.Services;

// CommentService: toàn bộ nghiệp vụ CommentsController ([01]–[17]) + helper dựng cây/CTE/flatten/cache.
// Quy ước chú thích: "BƯỚC n — …" đặt ngay trước dòng mã bắt đầu bước đó; "TRƯỜNG HỢP" trước nhánh điều kiện; các dòng còn lại có // ngắn sau lệnh.
// Epoch list (thế hệ cache danh sách): BƯỚC 0 trong các GET list gọi GetCommentsListEpochAsync; CRUD gọi InvalidateCommentsListsAsync.
// Giải thích thuật ngữ “epoch” cho người đọc code: xem đầu file CacheListEpochStore.cs (mốc thế hệ snapshot list, không phải Unix time).
public class CommentService : ServiceBase, ICommentService
{
    private readonly ICommentRepository _repository; // Tầng dữ liệu: EF + SqlQueryRaw CTE + demo loading.
    private readonly IMapper _mapper; // DTO ↔ entity cho CRUD và projection thủ công một số chỗ.
    // ICacheListEpochStore: đọc/bump số epoch nhúng trong prefix khóa cache JSON danh sách (cmt:{epoch}:…) — xem CacheListEpochStore.cs.
    private readonly ICacheListEpochStore _listEpoch;

    public CommentService(
        ICommentRepository repository,
        IMapper mapper,
        IEntityResponseCache cache,
        ICacheListEpochStore listEpoch)
        : base(cache)
    {
        // BƯỚC 1 — Gán repository: mọi đọc/ghi comment đi qua ICommentRepository.
        _repository = repository; // Bắt buộc để mọi đọc/ghi comment.
        // BƯỚC 2 — Gán mapper: DTO ↔ entity (AutoMapper profile đăng ký ở Program).
        _mapper = mapper; // AutoMapper profile đăng ký ở Program.
        // BƯỚC 3 — Gán store epoch: mọi route GET list dùng GetCommentsListEpochAsync; mọi CRUD ảnh hưởng aggregate gọi InvalidateCommentsListsAsync.
        _listEpoch = listEpoch; // Singleton-scoped theo DI Program: cùng IDistributedCache với EntityResponseCache.
    } // Kết thúc constructor CommentService.

    // Sort cho chuỗi khóa cache list (null → cùng default như repository list phân trang).
    private static SortByColumn CommentSortCacheKey(SortByColumn? sort) =>
        sort ?? CommentRepository.CommentListSortDefault;

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
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        Guid? userId = null, // Lọc tác giả (tuỳ chọn).
        SortByColumn? sort = null) // Sort dropdown (cache key + SQL).
    { // Mở khối GetCommentListAsync.
        // BƯỚC 0 — Đọc epoch danh sách comment từ cache phân tán (một lần / request; mọi khóa cmt:{epoch}:… trong hàm này dùng cùng biến này).
        var cmtEpoch = await _listEpoch.GetCommentsListEpochAsync(cancellationToken); // long: số thế hệ hiện tại; sau InvalidateCommentsListsAsync sẽ lớn hơn ⇒ khóa JSON list đổi ⇒ miss.

        // BƯỚC 1 — Xác định hasContent: nếu true thì ưu tiên nhánh tìm theo nội dung (unpaged trong bài / toàn hệ hoặc GetFlatRoutePagedAsync).
        var hasContent = !string.IsNullOrWhiteSpace(contentContains); // Có chữ thật trong content → nhánh tìm theo nội dung.
        if (hasContent) // Tìm theo nội dung (null/blank đã loại ở trên).
        { // Mở nhánh có từ khóa tìm trong nội dung.
            var term = contentContains!.Trim(); // Chuẩn hóa chuỗi tìm (bỏ khoảng đầu/cuối); null-forgiving vì đã qua hasContent.

            if (unpaged) // Trả toàn bộ khớp Contains.
            { // Mở nhánh không phân trang khi có content.
                if (postId is { } spid) // Nếu client giới hạn một bài viết.
                    await EnsurePostExistsAsync(spid); // Ném 404 khi post không tồn tại trước khi truy vấn.
                var entities = postId is { } spid2 // Chọn tập entity theo phạm vi post.
                    ? await _repository.LoadFlatUnpagedAsync(spid2, createdAtFrom, createdAtTo, userId, term, sort, cancellationToken) // Nạp mọi comment khớp trong một bài theo sort.
                    : await _repository.LoadFlatUnpagedAsync(null, createdAtFrom, createdAtTo, userId, term, sort, cancellationToken); // Toàn hệ cùng sort.
                return ToUnpagedCommentDtoResult(entities.Select(_mapper.Map<CommentDto>).ToList()); // Ánh xạ sang DTO rồi gói PagedResult giả một trang đủ dài.
            } // Kết thúc unpaged có content.

            // Cùng pipeline cache + LoadFlatAsync với [08] GET /api/comments/flat (contentContains bật SuppressCommentRouteCache); map sang CommentDto vì route [01] không trả Level.
            var flatPaged = await GetFlatRoutePagedAsync(postId, page, pageSize, cancellationToken, createdAtFrom, createdAtTo, userId, term, sort); // Phân trang có Skip/Take; cache tắt khi có lọc nội dung.
            return MapPagedCommentFlatToCommentDto(flatPaged); // Bỏ trường Level khỏi payload list chung.
        } // Kết thúc nhánh hasContent.

        // BƯỚC 2 — Không search theo content: nếu có postId thì xử lý unpaged một bài hoặc cache-aside + LoadFlatAsync theo post (cache key nhúng cmtEpoch ở nhánh phân trang có cache).
        if (postId is { } onlyPost) // Chỉ lọc post, không search.
        { // Mở khối theo post.
            if (unpaged) // Thay route GET .../post/{id} không phân trang.
            { // Mở khối.
                await EnsurePostExistsAsync(onlyPost);
                var fullPostEntities = await _repository.LoadFlatUnpagedAsync(onlyPost, createdAtFrom, createdAtTo, userId, null, sort, cancellationToken);
                return ToUnpagedCommentDtoResult(fullPostEntities.Select(_mapper.Map<CommentDto>).ToList());
            } // Kết thúc unpaged theo post.

            if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, null)) // Chỉ đọc cache khi không lọc ngày/user/content.
            { // Mở khối thử cache theo post.
                // Truyền cmtEpoch vào factory — chuỗi khóa chứa "cmt:{epoch}:" nên sau CRUD (epoch tăng) không còn trùng key với JSON cũ.
                var keyList = EntityCacheKeys.CommentsFlatByPost(cmtEpoch, onlyPost, page, pageSize, CommentSortCacheKey(sort)); // Khóa danh sách phẳng theo post + trang + sort + epoch list.
                var cachedList = await Cache.GetJsonAsync<PagedResult<CommentDto>>(keyList, cancellationToken); // Deserialize JSON cache hoặc null nếu miss.
                if (cachedList is not null) // Hit cache.
                    return cachedList; // Trả ngay, bỏ qua DB.
            } // Kết thúc nhánh đọc cache.
            await EnsurePostExistsAsync(onlyPost); // Xác nhận post tồn tại trước khi SELECT.
            var (listEntities, listTotal) = await _repository.LoadFlatAsync(onlyPost, page, pageSize, cancellationToken, createdAtFrom, createdAtTo, userId, null, sort); // COUNT + một trang entity phẳng trong bài.
            var listResult = CommentPagedResult.ForFlatCommentList(listEntities.Select(_mapper.Map<CommentDto>).ToList(), page, pageSize, listTotal); // Map sang DTO và gói metadata phân trang.
            if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, null)) // Chỉ ghi cache khi điều kiện cho phép.
                // SetJsonAsync cùng epoch đã đọc ở BƯỚC 0 — đảm bảo đọc/ghi cùng một “thế hệ”; sau bump chỉ các request sau mới nhìn thấy key mới.
                await Cache.SetJsonAsync(EntityCacheKeys.CommentsFlatByPost(cmtEpoch, onlyPost, page, pageSize, CommentSortCacheKey(sort)), listResult, cancellationToken); // Lưu kết quả với TTL mặc định EntityResponseCache.
            return listResult; // Trả về cho controller.
        } // Kết thúc chỉ postId.

        // BƯỚC 3 — Không cố định post: nếu unpaged thì nạp toàn hệ một lần rồi gói ToUnpagedCommentDtoResult.
        if (unpaged) // Toàn bộ comment mọi bài — không cache (khối lượng lớn).
        { // Mở khối global unpaged.
            var entities = await _repository.LoadFlatUnpagedAsync(null, createdAtFrom, createdAtTo, userId, null, sort, cancellationToken); // SELECT có lọc ở SQL.
            var list = entities.Select(_mapper.Map<CommentDto>).ToList(); // Ánh xạ DTO.
            return ToUnpagedCommentDtoResult(list); // Trả PagedResult “giả” một trang đủ dài.
        } // Kết thúc global unpaged.

        // BƯỚC 4 — Mặc định còn lại: phân trang toàn hệ qua PagedGlobalListAsync (cmtEpoch truyền xuống để nhúng vào CommentsFlatAll).
        return await PagedGlobalListAsync(cmtEpoch, page, pageSize, cancellationToken, createdAtFrom, createdAtTo, userId, null, sort); // Đồng bộ cache với GET /api/comments/flat khi không postId và không suppress.
    } // Kết thúc GetCommentListAsync.

    // Đọc một comment theo Id với cache; 404 nếu không có — cùng thứ tự GET /api/comments/{id} trong CommentsController.
    // [02] Route: GET /api/comments/{id}
    public async Task<CommentDto> GetByIdAsync(Guid id) // Khóa chính comment.
    { // Mở khối GetByIdAsync.
        // BƯỚC 1 — Tính khóa cache theo Id comment (EntityCacheKeys.Comment).
        var cacheKey = EntityCacheKeys.Comment(id); // Khóa theo Guid comment.
        // BƯỚC 2 — Đọc cache; hit thì return ngay (không gọi DB).
        var cached = await Cache.GetJsonAsync<CommentDto>(cacheKey, CancellationToken.None); // Đọc cache; CancellationToken.None cố định như code cũ.
        if (cached is not null) // Đã có trong cache.
        { // Mở khối.
            return cached; // Trả ngay không truy vấn DB.
        } // Hết nhánh cache.

        // BƯỚC 3 — Miss cache: projection SQL một dòng theo Id; null → ném 404 bên dưới.
        var dto = await _repository.GetCommentByIdRouteReadAsync(id, default); // SELECT projection một comment.
        if (dto is null) // Không tồn tại.
        { // Mở khối.
            throw new ApiException( // Ném ngoại lệ thống nhất.
                StatusCodes.Status404NotFound, // HTTP 404.
                ApiErrorCodes.CommentNotFound, // Mã lỗi.
                ApiMessages.CommentNotFound); // Thông điệp.
        } // Hết nhánh null.

        // BƯỚC 4 — Ghi cache rồi trả DTO cho caller.
        await Cache.SetJsonAsync(cacheKey, dto, default); // Lưu DTO vào cache.
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
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        string? contentContains = null, // Tìm trong nội dung (tuỳ chọn).
        SortByColumn? sort = null) // Sort dropdown.
    { // Mở khối GetCommentsByUserIdPagedAsync.
        // BƯỚC 0 — Đọc epoch danh sách comment: mọi khóa CommentsByUser(cmtEpoch, …) trong hàm này cùng một “thế hệ”; sau CRUD gọi InvalidateCommentsListsAsync thì số tăng ⇒ miss cache cũ.
        var cmtEpoch = await _listEpoch.GetCommentsListEpochAsync(cancellationToken); // long — giá trị hiện tại trong IDistributedCache (__epoch:list:comments); không đổi giữa các lệnh Get/Set trong cùng request.

        // BƯỚC 1 — Đảm bảo user tồn tại; không có → EnsureUserExistsAsync ném 404.
        await EnsureUserExistsAsync(userId); // User phải có trong DB.
        // BƯỚC 2 — Cache-aside đọc: chỉ khi không suppress (không lọc ngày + không lọc content).
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, null, contentContains)) // Cache khi không lọc ngày và không lọc content.
        { // Mở khối cache.
            // Nhúng cmtEpoch vào chuỗi khóa — prefix "cmt:{epoch}:" khiến JSON list cũ (epoch thấp hơn) không bao giờ được hit sau khi đã bump.
            var cacheKey = EntityCacheKeys.CommentsByUser(cmtEpoch, userId, page, pageSize, CommentSortCacheKey(sort)); // Khóa theo epoch + user + trang + sort.
            var cached = await Cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken); // Đọc cache.
            if (cached is not null) // Hit.
            { // Mở khối.
                return cached; // Trả ngay.
            } // Kết thúc hit.
        } // Kết thúc nhánh cache.

        // BƯỚC 4 — Đếm số comment gốc (ParentId null) của user trong phạm vi lọc — dùng metadata TotalNodes.
        var totalRoots = await _repository.CountCommentRootsMatchingRouteAsync( // Số comment gốc của user trong phạm vi lọc (TotalNodes).
            null, // Mọi post.
            contentContains, // Nội dung.
            cancellationToken, // Hủy.
            createdAtFrom, // Ngày.
            createdAtTo, // Ngày.
            userId); // Cùng user với route.
        // BƯỚC 5 — Nạp một trang comment phẳng toàn hệ đã lọc theo user (+ ngày/content nếu có): COUNT + Skip/Take.
        // Cùng pipeline LoadFlatAsync(postId: null, userId: …) — tránh trùng GetCommentsByUserRoutePagedAsync.
        var (items, total) = await _repository.LoadFlatAsync(null, page, pageSize, cancellationToken, createdAtFrom, createdAtTo, userId, contentContains, sort);
        // BƯỚC 6 — Gói PagedResult: map DTO + TotalPages theo total comment + metadata gốc (totalRoots).
        var result = CommentPagedResult.ForFlatCommentPageWithRootTotals( // Danh sách phẳng; TotalPages theo tổng comment user.
            items.Select(_mapper.Map<CommentDto>).ToList(), // Map DTO.
            page, // Trang.
            pageSize, // Cỡ.
            total, // TotalComments + mẫu số TotalPages.
            totalRoots); // Gốc (ParentId null) của user khớp lọc.
        // BƯỚC 7 — Ghi cache khi được phép (cùng điều kiện suppress như bước đọc cache); dùng đúng cmtEpoch từ BƯỚC 0 để đọc/ghi cùng thế hệ.
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, null, contentContains)) // Chỉ set cache khi không lọc ngày/content.
        { // Mở khối.
            // SetJsonAsync với CommentsByUser(cmtEpoch, …) — trùng logic khóa với BƯỚC 2; sau InvalidateCommentsListsAsync epoch mới không trùng key với bản ghi này.
            await Cache.SetJsonAsync(EntityCacheKeys.CommentsByUser(cmtEpoch, userId, page, pageSize, CommentSortCacheKey(sort)), result, cancellationToken); // Serialize PagedResult → JSON trong cache phân tán.
        } // Kết thúc set.

        return result; // Trả controller.
    } // Kết thúc GetCommentsByUserIdPagedAsync.

    // Tạo comment mới sau khi kiểm tra post, user và parent hợp lệ.
    // [04] Route: POST /api/comments
    public async Task<CommentDto> CreateAsync(CreateCommentDto dto) // Payload tạo mới.
    { // Mở khối CreateAsync.
        // BƯỚC 1 — Kiểm tra post tồn tại; không → 404 PostNotFound.
        if (!await _repository.PostExistsAsync(dto.PostId)) // Any trên Posts.
        { // Mở khối.
            throw new ApiException( // Post không tồn tại.
                StatusCodes.Status404NotFound, // 404.
                ApiErrorCodes.PostNotFound, // Mã post.
                ApiMessages.PostNotFound); // Thông báo.
        } // Hết kiểm tra post.

        // BƯỚC 2 — Kiểm tra user tồn tại; không → 404 UserNotFound.
        if (!await _repository.UserExistsAsync(dto.UserId)) // Any trên Users.
        { // Mở khối.
            throw new ApiException( // User không tồn tại.
                StatusCodes.Status404NotFound, // 404.
                ApiErrorCodes.UserNotFound, // Mã user.
                ApiMessages.UserNotFound); // Thông báo.
        } // Hết kiểm tra user.

        // BƯỚC 3 — Nếu có ParentId: cha phải tồn tại và cùng post; sai → 400 CommentParentInvalid.
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

        // BƯỚC 4 — Map DTO → entity, sinh Id + CreatedAt UTC, Add + SaveChanges (INSERT một dòng).
        var entity = _mapper.Map<Comment>(dto); // DTO → entity trong RAM.
        entity.Id = Guid.NewGuid(); // Sinh Id mới.
        entity.CreatedAt = DateTime.UtcNow; // Ghi mốc thời gian UTC.

        await _repository.AddAsync(entity); // Đánh dấu Added trong context.
        await _repository.SaveChangesAsync(); // INSERT xuống SQL.

        // BƯỚC 5 — Vô hiệu cache resource GET /api/posts/{postId}/comments/tree|flat (payload gắn post, không dùng prefix cmt:{epoch}).
        await InvalidatePostsResourceCommentsCachesAsync(dto.PostId, default); // Khóa riêng l:posts:…:comments:* — xóa tường minh.

        // BƯỚC 6 — Tăng epoch danh sách comment: ghi long mới vào khóa __epoch:list:comments trong IDistributedCache (atomic increment trong CacheListEpochStore).
        await _listEpoch.InvalidateCommentsListsAsync(default); // “Invalidate” ở đây = bump epoch — không xóa từng key JSON cmt:*; chỉ khiến mọi key có prefix cmt:{epoch_cũ} trở thành rác (TTL) và client sau dùng epoch mới.

        // BƯỚC 7 — Map entity → DTO trả về (không SELECT lại DB).
        return _mapper.Map<CommentDto>(entity); // Trả DTO sau khi đã có Id trong RAM (không SELECT lại).
    } // Kết thúc CreateAsync.

    // Người dùng: chỉ tác giả (UserId trùng JWT) sửa nội dung; không đổi cây hay post.
    // [05] Route: PUT /api/comments/{id}
    public async Task UpdateAsAuthorAsync(Guid id, UpdateCommentDto dto, Guid currentUserId) // id comment, payload, user hiện tại.
    { // Mở khối UpdateAsAuthorAsync.
        // BƯỚC 1 — Nạp entity tracked theo Id; null → 404 CommentNotFound.
        var entity = await _repository.GetByIdAsync(id); // Nạp entity tracked.
        if (entity is null) // Không có bản ghi.
        { // Mở khối.
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // Mã HTTP.
                ApiErrorCodes.CommentNotFound, // Mã nghiệp vụ.
                ApiMessages.CommentNotFound); // Thông điệp.
        } // Hết null.

        // BƯỚC 2 — Chỉ tác giả được sửa; UserId khác currentUserId → 403 NotResourceAuthor.
        if (entity.UserId != currentUserId) // Không phải tác giả.
        { // Mở khối.
            throw new ApiException( // 403.
                StatusCodes.Status403Forbidden, // Cấm.
                ApiErrorCodes.NotResourceAuthor, // Mã quyền.
                ApiMessages.NotResourceAuthor); // Thông điệp.
        } // Hết kiểm tra tác giả.

        // BƯỚC 3 — Kiểm tra post của comment vẫn tồn tại (nhất quán với các route khác); mất → 404 PostNotFound.
        if (!await _repository.PostExistsAsync(entity.PostId)) // Post đã mất (nhất quán logic).
        { // Mở khối.
            throw new ApiException( // 404 post.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.PostNotFound, // Mã.
                ApiMessages.PostNotFound); // Thông báo.
        } // Hết kiểm tra post.

        // BƯỚC 4 — Gán nội dung mới, đánh dấu Modified, SaveChanges.
        entity.Content = dto.Content; // Cập nhật nội dung.
        _repository.Update(entity); // Đánh dấu Modified.
        await _repository.SaveChangesAsync(); // Ghi DB.

        // BƯỚC 5 — Xóa cache chi tiết theo Id và cache resource cây/phẳng gắn post (khác cơ chế epoch list).
        await Cache.RemoveAsync(EntityCacheKeys.Comment(id), default); // Miss GET /api/comments/{id} lần sau.
        await InvalidatePostsResourceCommentsCachesAsync(entity.PostId, default); // Xóa l:posts:{postId}:comments:* .

        // BƯỚC 6 — Bump epoch danh sách: mọi GET list/flat/tree có khóa cmt:{n}:… sẽ dùng n mới sau lệnh này; JSON list cũ không còn được truy cập qua khóa mới.
        await _listEpoch.InvalidateCommentsListsAsync(default); // Một Round-trip vào cache lưu số epoch — không iterate toàn bộ key JSON.
    } // Kết thúc UpdateAsAuthorAsync.

    // Quản trị: cập nhật đủ trường; chuyển post cập nhật PostId cả cây con; chặn parent tạo chu trình hoặc sai post.
    // [06] Route: PUT /api/admin/comments/{id}
    public async Task UpdateAsAdminAsync(Guid id, AdminUpdateCommentDto dto) // id gốc và payload admin.
    { // Mở khối UpdateAsAdminAsync.
        // BƯỚC 1 — User đích phải tồn tại; không → 404 UserNotFound.
        if (!await _repository.UserExistsAsync(dto.UserId)) // User đích phải tồn tại.
        { // Mở khối.
            throw new ApiException( // 404 user.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.UserNotFound, // Mã.
                ApiMessages.UserNotFound); // Thông báo.
        } // Hết kiểm tra user.

        // BƯỚC 2 — Post đích phải tồn tại; không → 404 PostNotFound.
        if (!await _repository.PostExistsAsync(dto.PostId)) // Post đích phải tồn tại.
        { // Mở khối.
            throw new ApiException( // 404 post.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.PostNotFound, // Mã.
                ApiMessages.PostNotFound); // Thông báo.
        } // Hết kiểm tra post.

        // BƯỚC 3 — Nạp entity comment cần sửa (tracked); không có → 404 CommentNotFound.
        var root = await _repository.GetByIdAsync(id); // Nạp nút gốc cần sửa.
        if (root is null) // Không có comment.
        { // Mở khối.
            throw new ApiException( // 404 comment.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.CommentNotFound, // Mã.
                ApiMessages.CommentNotFound); // Thông báo.
        } // Hết null root.

        // BƯỚC 4 — Nạp toàn comment phẳng của bài cũ rồi BuildSubtreeIdSet để biết mọi Id thuộc cây con (gốc + hậu duệ).
        // Lấy toàn bộ comment phẳng của bài cũ (AsNoTracking) để suy ra tập Id cây con qua BFS.
        var inOldFlat = await _repository.LoadFlatUnpagedAsync(root.PostId); // List comment một post.
        var subtree = BuildSubtreeIdSet(inOldFlat, root.Id); // Tập Id gốc + hậu duệ.

        // BƯỚC 5 — Nếu đổi ParentId: chặn self-parent, cha nằm trong subtree, cha null, cha sai post (400 tương ứng).
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

        // BƯỚC 6 — Nếu đổi PostId: nạp tracked toàn post cũ và gán PostId mới cho mọi entity có Id ∈ subtree.
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

        // BƯỚC 7 — Gán đầy đủ trường trên entity gốc (Content, UserId, PostId, ParentId), Update + SaveChanges.
        root.Content = dto.Content; // Nội dung mới.
        root.UserId = dto.UserId; // Chủ sở hữu mới.
        root.PostId = dto.PostId; // Post (đã đồng bộ subtree nếu chuyển).
        root.ParentId = dto.ParentId; // Cha (nullable).
        _repository.Update(root); // Đánh dấu sửa gốc.
        await _repository.SaveChangesAsync(); // Flush thay đổi.

        // BƯỚC 8 — Gom khóa cache (post cũ, post mới nếu đổi, từng comment trong subtree) rồi RemoveManyAsync.
        var cacheKeys = new List<string> { EntityCacheKeys.Post(oldPostId) }; // Luôn xóa cache post cũ.
        if (oldPostId != dto.PostId) // Nếu đổi post.
        { // Mở khối.
            cacheKeys.Add(EntityCacheKeys.Post(dto.PostId)); // Thêm cache post mới.
        } // Hết nhánh đổi post.

        cacheKeys.AddRange(subtree.Select(EntityCacheKeys.Comment)); // Thêm khóa từng comment trong cây.
        cacheKeys.AddRange(EntityCacheKeys.PostsResourceCommentsCteAllKeys(oldPostId)); // Cache GET …/posts/{id}/comments/tree|flat (post cũ).
        if (oldPostId != dto.PostId) // Đổi bài: vô hiệu cache resource post mới (có thể đã cache từ trước).
            cacheKeys.AddRange(EntityCacheKeys.PostsResourceCommentsCteAllKeys(dto.PostId)); // Mọi biến thể includeReplies/sort.
        await Cache.RemoveManyAsync(cacheKeys, default); // Xóa hàng loạt khóa cụ thể (Id, post, resource).

        // BƯỚC 9 — Bump epoch toàn bộ danh sách comment (prefix cmt:) vì cây/post/user hiển thị trên list có thể đã đổi mà không đủ gom từng khóa trang.
        await _listEpoch.InvalidateCommentsListsAsync(default); // Tăng __epoch:list:comments — vô hiệu hóa ngầm mọi snapshot list/tree phân trang đã cache.
    } // Kết thúc UpdateAsAdminAsync.

    // Xóa một comment và toàn bộ hậu duệ trong cùng post; vô hiệu cache liên quan.
    // [07] Route: DELETE /api/comments/{id}
    public async Task DeleteAsync(Guid id) // Id comment gốc cần xóa.
    { // Mở khối DeleteAsync.
        // BƯỚC 1 — Nạp comment gốc cần xóa; null → 404 CommentNotFound.
        var entity = await _repository.GetByIdAsync(id); // Nạp entity gốc cần xóa.
        if (entity is null) // Không tồn tại.
        { // Mở khối.
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.CommentNotFound, // Mã.
                ApiMessages.CommentNotFound); // Thông báo.
        } // Hết null.

        // BƯỚC 2 — Post chứa comment phải còn tồn tại; không → 404 PostNotFound (nhất quán route khác).
        if (!await _repository.PostExistsAsync(entity.PostId)) // Kiểm tra post (logic nhất quán với code gốc).
        { // Mở khối.
            throw new ApiException( // 404 post.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.PostNotFound, // Mã.
                ApiMessages.PostNotFound); // Thông báo.
        } // Hết kiểm tra post.

        // BƯỚC 3 — Nạp toàn comment của post vào RAM để duyệt cây con bằng BFS trên tập phẳng.
        var allCommentsInPost = await _repository.LoadFlatUnpagedAsync(entity.PostId); // SELECT toàn comment của post vào RAM.
        var toDelete = new HashSet<Guid> { entity.Id }; // Tập Id sẽ xóa, khởi tạo với gốc.
        var queue = new Queue<Guid>(); // Hàng đợi BFS các Id con.
        queue.Enqueue(entity.Id); // Bắt đầu từ comment gốc.

        // BƯỚC 4 — BFS: gom toDelete = gốc + mọi Id con cháu (queue + duyệt ParentId == currentId).
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

        // BƯỚC 5 — Lọc entity thuộc toDelete để Remove từng phần tử.
        var entitiesToRemove = allCommentsInPost // Lọc entity cần Remove.
            .Where(x => toDelete.Contains(x.Id)) // Chỉ những Id đã gom.
            .ToList(); // List để foreach Remove.

        // BƯỚC 6 — Đánh dấu Remove cho từng entity trong tập cần xóa.
        foreach (var comment in entitiesToRemove) // Duyệt entity cần xóa.
        { // Mở khối.
            _repository.Remove(comment); // Đánh dấu Deleted từng entity.
        } // Hết foreach Remove.

        // BƯỚC 7 — Flush DELETE xuống SQL.
        await _repository.SaveChangesAsync(); // Gửi DELETE (hoặc batch) xuống SQL.

        // BƯỚC 8 — Xóa cache theo mọi Id đã xóa + cache resource GET …/posts/{postId}/comments/tree|flat của bài đó.
        var keys = toDelete.Select(EntityCacheKeys.Comment).ToList(); // Sinh danh sách khóa cache cho mọi Id đã xóa.
        keys.AddRange(EntityCacheKeys.PostsResourceCommentsCteAllKeys(entity.PostId)); // Khóa l:posts:…:comments:* (không dùng l:comments:p:…).
        await Cache.RemoveManyAsync(keys, default); // Xóa cache theo loạt.

        // BƯỚC 9 — Bump epoch list comment: sau khi xóa nhiều dòng, mọi trang list/flat/tree cached với epoch cũ không còn hợp lệ.
        await _listEpoch.InvalidateCommentsListsAsync(default); // Đồng bộ quan điểm “thế hệ danh sách” với DB mà không cần biết hết tổ hợp page/sort.
    } // Kết thúc DeleteAsync.

    #endregion

    #region Public — GET dạng phẳng / CTE / cây / posts/{id}/comments ([08]–[13] cache+DB+map; [2a][2b] cache riêng l:posts:…:comments:*)

    // [08] Route: GET /api/comments/flat
    public async Task<PagedResult<CommentFlatDto>> GetFlatRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetFlatRoutePagedAsync — danh sách phẳng CommentFlatDto (EF, Level=0) có cache khi không lọc “nặng”.
        // BƯỚC 0 — Đọc epoch hiện tại cho mọi khóa cmt:{epoch}:… trong GET /flat (post hoặc toàn cục); đồng bộ invalidation với Create/Update/Delete.
        var cmtEpoch = await _listEpoch.GetCommentsListEpochAsync(cancellationToken); // Số trong cache Redis/SQL chứa trong __epoch:list:comments — dùng chung một biến cho Get + Set của request này.

        // BƯỚC 1 — Cache-aside đọc: nếu không suppress thì thử GetJsonAsync theo khóa post hoặc toàn hệ (chuỗi khóa phụ thuộc cmtEpoch).
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Cho phép dùng cache phân trang mặc định.
        { // Mở khối đọc cache.
            // Chọn nhánh khóa: theo post (CommentsFlatByPost) hay toàn hệ (CommentsFlatAll); cả hai nhúng cmtEpoch vào đầu chuỗi khóa.
            var cacheKey = postId is { } p ? EntityCacheKeys.CommentsFlatByPost(cmtEpoch, p, page, pageSize, CommentSortCacheKey(sort)) : EntityCacheKeys.CommentsFlatAll(cmtEpoch, page, pageSize, CommentSortCacheKey(sort)); // Nếu epoch vừa bump, key mới ≠ key đã cache ⇒ miss.
            var cached = await Cache.GetJsonAsync<PagedResult<CommentFlatDto>>(cacheKey, cancellationToken); // Deserialize JSON trong distributed cache.
            if (cached is not null) // Hit (cùng epoch và cùng tham số trang/sort).
                return cached; // Không hit DB trong nhánh này.
        } // Kết thúc đọc cache.
        // BƯỚC 2 — Nếu client truyền postId: đảm bảo post tồn tại (404 khi không có).
        if (postId is { } pid) // Có lọc theo một bài.
            await EnsurePostExistsAsync(pid); // 404 nếu post không có.
        // BƯỚC 3 — Repository: COUNT + SELECT một trang comment phẳng đã lọc.
        var (entities, total) = await _repository.LoadFlatAsync(postId, page, pageSize, cancellationToken, createdAtFrom, createdAtTo, userId, contentContains, sort); // COUNT + SELECT một trang phẳng.
        // BƯỚC 4 — Map entity → CommentFlatDto (Level=0 từ profile) và gói PagedResult.ForFlatCommentList.
        var result = CommentPagedResult.ForFlatCommentList(entities.Select(_mapper.Map<CommentFlatDto>).ToList(), page, pageSize, total); // Map entity → DTO route flat.
        // BƯỚC 5 — Ghi cache JSON khi không suppress: khóa trùng BƯỚC 1 (cùng cmtEpoch) để lần sau GET cùng tham số hit.
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Được phép ghi cache.
        { // Mở khối set cache.
            var cacheKey = postId is { } p2 ? EntityCacheKeys.CommentsFlatByPost(cmtEpoch, p2, page, pageSize, CommentSortCacheKey(sort)) : EntityCacheKeys.CommentsFlatAll(cmtEpoch, page, pageSize, CommentSortCacheKey(sort)); // Phải trùng đúng nhánh và epoch với bước đọc.
            await Cache.SetJsonAsync(cacheKey, result, cancellationToken); // Lưu PagedResult<CommentFlatDto> dưới key có prefix cmt:{epoch}: .
        } // Kết thúc set cache.
        return result; // Trả dữ liệu cho HTTP 200.
    } // Kết thúc GetFlatRoutePagedAsync.

    // [09] Route: GET /api/comments/cte — chỉ danh sách phẳng từ SqlQueryRaw CTE (CommentCteDto + Level); không BuildTreeCte, không flatten cây (khác [11] tree/cte và [13] tree/cte/flatten).
    public async Task<PagedResult<CommentCteDto>> GetCteFlatRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetCteFlatRoutePagedAsync — một pipeline: LoadRawCteAsync → Skip/Take trên dòng phẳng (cùng tinh thần GET …/flat).
        // BƯỚC 0 — Epoch list cho nhánh CommentsCteFlat* / CommentsAllCteFlat* (cùng ý với GET /flat; bump khi có thay đổi aggregate comment).
        var cmtEpoch = await _listEpoch.GetCommentsListEpochAsync(cancellationToken); // Một đọc / request — truyền vào mọi factory khóa CTE-flat bên dưới.

        // BƯỚC 1 — Thử đọc cache CTE-flat khi không suppress.
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Cache khi không có lọc làm phình không gian khóa.
        { // Mở đọc cache.
            var cacheKey = postId is { } p ? EntityCacheKeys.CommentsCteFlatByPost(cmtEpoch, p, page, pageSize, CommentSortCacheKey(sort)) : EntityCacheKeys.CommentsAllCteFlat(cmtEpoch, page, pageSize, CommentSortCacheKey(sort)); // Namespace khóa tách biệt route /cte khỏi /flat nhưng cùng epoch.
            var cached = await Cache.GetJsonAsync<PagedResult<CommentCteDto>>(cacheKey, cancellationToken); // Lấy bản đã cache.
            if (cached is not null) // Hit.
                return cached; // Trả luôn.
        } // Hết đọc cache.
        // BƯỚC 2 — Có postId: đảm bảo bài tồn tại (404), đồng bộ với route flat theo bài.
        if (postId is { } pid) // Client giới hạn một bài.
            await EnsurePostExistsAsync(pid); // Không SELECT CTE khi post không có.
        // BƯỚC 3 — Một lần SqlQueryRaw CTE: toàn bộ dòng phẳng đã lọc + ORDER BY (whitelist) trong SQL — không dựng CommentTreeCteDto.
        var flatRows = await _repository.LoadRawCteAsync(postId, cancellationToken, createdAtFrom, createdAtTo, userId, contentContains, sort); // Danh sách thô CommentCteDto.
        // BƯỚC 4 — Phân trang theo dòng phẳng: mỗi phần tử = một comment; TotalCount = số dòng CTE (mẫu số TotalPages giống route /flat).
        var totalRows = (long)flatRows.Count; // Tổng dòng kết quả CTE sau lọc (một nguồn sự thật cho Skip/Take).
        var pageItems = flatRows // Cắt trang trong RAM (CTE đã materialize một lần).
            .Skip((page - 1) * pageSize) // OFFSET theo trang.
            .Take(pageSize) // FETCH cỡ trang.
            .ToList(); // Trang hiện tại — vẫn là CommentCteDto thô, không qua cây.
        // BƯỚC 5 — Gói PagedResult kiểu danh sách phẳng (TotalComments/TotalNodes = null; không dùng ForCtePagedByRootNodes).
        var result = CommentPagedResult.ForFlatCommentList(pageItems, page, pageSize, totalRows); // Metadata giống GET …/flat: chỉ TotalCount.
        // BƯỚC 6 — Ghi cache khi không suppress (cmtEpoch nhất quán với BƯỚC 1).
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Được cache.
        { // Mở set cache.
            var cacheKey = postId is { } p2 ? EntityCacheKeys.CommentsCteFlatByPost(cmtEpoch, p2, page, pageSize, CommentSortCacheKey(sort)) : EntityCacheKeys.CommentsAllCteFlat(cmtEpoch, page, pageSize, CommentSortCacheKey(sort)); // Cùng quy tắc nhúng epoch như CommentsFlat*.
            await Cache.SetJsonAsync(cacheKey, result, cancellationToken); // Ghi cache phân trang CTE-flat.
        } // Hết set cache.
        return result; // Trả response.
    } // Kết thúc GetCteFlatRoutePagedAsync.

    // [10] Route: GET /api/comments/tree/flat
    public async Task<PagedResult<CommentTreeFlatDto>> GetTreeFlatRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetTreeFlatRoutePagedAsync — cây lồng có Level (BFS sau BuildTreeFlat, đồng bộ với pipeline CTE).
        // BƯỚC 0 — Đọc epoch cho họ khóa CommentsTreeFlat* / CommentsAllTreeFlat* (invalidate chung khi CRUD comment).
        var cmtEpoch = await _listEpoch.GetCommentsListEpochAsync(cancellationToken); // long — nhúng vào chuỗi khóa JSON phân trang tree-flat.

        // BƯỚC 1 — Thử đọc cache tree-flat khi không suppress (hit chỉ khi epoch trùng bản đã Set).
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Cho phép cache mặc định.
        { // Mở đọc cache.
            var cacheKey = postId is { } p ? EntityCacheKeys.CommentsTreeFlatByPost(cmtEpoch, p, page, pageSize, CommentSortCacheKey(sort)) : EntityCacheKeys.CommentsAllTreeFlat(cmtEpoch, page, pageSize, CommentSortCacheKey(sort)); // Hai namespace post/global, cùng prefix cmt:{epoch}: .
            var cached = await Cache.GetJsonAsync<PagedResult<CommentTreeFlatDto>>(cacheKey, cancellationToken); // Đọc bản cache.
            if (cached is not null) // Hit.
                return cached; // Trả sớm.
        } // Hết đọc cache.
        // BƯỚC 2 — BuildFlatTreesPagedCoreAsync: gốc trang + BFS subtree + BuildTreeFlat trong RAM.
        var (trees, totalComments, totalRoots) = await BuildFlatTreesPagedCoreAsync(postId, page, pageSize, cancellationToken, createdAtFrom, createdAtTo, userId, contentContains, sort); // Gốc trang + nạp subtree + dựng cây.
        // BƯỚC 3 — MapTreeFlat từng gốc rồi ForCtePagedByRootNodes (metadata giống route CTE).
        var result = CommentPagedResult.ForCtePagedByRootNodes(trees.Select(MapTreeFlat).ToList(), page, pageSize, totalComments, totalRoots); // Map sang DTO tree-flat và gói phân trang.
        // BƯỚC 4 — Ghi cache tree-flat khi không suppress; Set dùng cùng cmtEpoch như BƯỚC 1.
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Được cache.
        { // Mở set.
            var cacheKey = postId is { } p2 ? EntityCacheKeys.CommentsTreeFlatByPost(cmtEpoch, p2, page, pageSize, CommentSortCacheKey(sort)) : EntityCacheKeys.CommentsAllTreeFlat(cmtEpoch, page, pageSize, CommentSortCacheKey(sort)); // Trùng quy tắc nhánh đọc.
            await Cache.SetJsonAsync(cacheKey, result, cancellationToken); // Lưu JSON.
        } // Hết set.
        return result; // HTTP payload.
    } // Kết thúc GetTreeFlatRoutePagedAsync.

    // [11] Route: GET /api/comments/tree/cte
    public async Task<PagedResult<CommentTreeCteDto>> GetTreeCteRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetTreeCteRoutePagedAsync — CommentTreeCteDto có Level từ CTE, phân trang theo gốc.
        // BƯỚC 0 — Epoch list dùng chung invalidate với các route khác trong prefix cmt:{n}:….
        var cmtEpoch = await _listEpoch.GetCommentsListEpochAsync(cancellationToken); // Truyền vào CommentsTreeCte* / CommentsAllTreeCte*.

        // BƯỚC 1 — Thử đọc cache tree-CTE khi không suppress.
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Cache khi hợp lệ.
        { // Mở đọc.
            var cacheKey = postId is { } p ? EntityCacheKeys.CommentsTreeCteByPost(cmtEpoch, p, page, pageSize, CommentSortCacheKey(sort)) : EntityCacheKeys.CommentsAllTreeCte(cmtEpoch, page, pageSize, CommentSortCacheKey(sort)); // Khóa tree-CTE có epoch.
            var cached = await Cache.GetJsonAsync<PagedResult<CommentTreeCteDto>>(cacheKey, cancellationToken); // Deserialize.
            if (cached is not null) // Hit.
                return cached; // Thoát sớm.
        } // Hết đọc.
        // BƯỚC 2 — BuildCteTreesPagedCoreAsync: danh sách gốc trang + metadata tổng.
        var (items, totalNodes, totalComments) = await BuildCteTreesPagedCoreAsync(postId, page, pageSize, cancellationToken, createdAtFrom, createdAtTo, userId, contentContains, sort); // Cây gốc trang + tổng gốc CTE + tổng comment bảng.
        // BƯỚC 3 — Gói PagedResult trực tiếp từ items (CommentTreeCteDto).
        var result = CommentPagedResult.ForCtePagedByRootNodes(items, page, pageSize, totalComments, totalNodes); // Gói kết quả phân trang.
        // BƯỚC 4 — Ghi cache khi không suppress (cmtEpoch như BƯỚC 0–1).
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Ghi cache.
        { // Mở set.
            var cacheKey = postId is { } p2 ? EntityCacheKeys.CommentsTreeCteByPost(cmtEpoch, p2, page, pageSize, CommentSortCacheKey(sort)) : EntityCacheKeys.CommentsAllTreeCte(cmtEpoch, page, pageSize, CommentSortCacheKey(sort)); // Khóa nhất quán.
            await Cache.SetJsonAsync(cacheKey, result, cancellationToken); // Lưu.
        } // Hết set.
        return result; // Trả về.
    } // Kết thúc GetTreeCteRoutePagedAsync.

    // [12] Route: GET /api/comments/tree/flat/flatten
    public async Task<PagedResult<CommentFlattenFlatDto>> GetTreeFlatFlattenRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetTreeFlatFlattenRoutePagedAsync — CommentFlattenFlatDto từ pipeline tree/flat RAM.
        // BƯỚC 0 — Epoch cho họ khóa CommentsTreeFlatFlatten* (cùng cơ chế bump InvalidateCommentsListsAsync).
        var cmtEpoch = await _listEpoch.GetCommentsListEpochAsync(cancellationToken); // Đồng bộ với mọi list JSON có prefix thế hệ.

        // BƯỚC 1 — Thử đọc cache flatten khi không suppress.
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Điều kiện cache.
        { // Mở đọc cache.
            var cacheKey = postId is { } p ? EntityCacheKeys.CommentsTreeFlatFlattenByPost(cmtEpoch, p, page, pageSize, CommentSortCacheKey(sort)) : EntityCacheKeys.CommentsAllTreeFlatFlatten(cmtEpoch, page, pageSize, CommentSortCacheKey(sort)); // Nhánh post vs global có cùng vị trí nhúng epoch.
            var cached = await Cache.GetJsonAsync<PagedResult<CommentFlattenFlatDto>>(cacheKey, cancellationToken); // Đọc.
            if (cached is not null) // Hit.
                return cached; // Trả cache.
        } // Hết đọc.
        // BƯỚC 2 — BuildFlatTreesPagedCoreAsync: gốc trang + subtree + metadata.
        var (flatQueryRootsOnPage, totalCommentsMatchingFilter, totalRootNodesInTable) = await BuildFlatTreesPagedCoreAsync(postId, page, pageSize, cancellationToken, createdAtFrom, createdAtTo, userId, contentContains, sort); // Gốc trang + metadata.
        // BƯỚC 3 — Preorder từng gốc CommentTreeDto → CommentFlattenFlatDto (Level đã BFS trong BuildTreeFlat).
        var preorderFlatRows = new List<CommentFlattenFlatDto>(); // DTO riêng route tree/flat/flatten.
        foreach (var root in flatQueryRootsOnPage) // Duyệt từng gốc trang.
            FlattenTreeFlatToFlattenFlatDto(root, preorderFlatRows); // Preorder append.
        // BƯỚC 4 — Gói PagedResult (TotalPages theo tổng gốc).
        var result = CommentPagedResult.ForCtePagedByRootNodes(preorderFlatRows, page, pageSize, totalCommentsMatchingFilter, totalRootNodesInTable); // Gói PagedResult (TotalPages theo gốc).
        // BƯỚC 5 — Ghi cache flatten khi không suppress (khóa gắn cmtEpoch từ BƯỚC 0).
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Set cache.
        { // Mở set.
            var cacheKey = postId is { } p2 ? EntityCacheKeys.CommentsTreeFlatFlattenByPost(cmtEpoch, p2, page, pageSize, CommentSortCacheKey(sort)) : EntityCacheKeys.CommentsAllTreeFlatFlatten(cmtEpoch, page, pageSize, CommentSortCacheKey(sort)); // Khóa đồng bộ đọc.
            await Cache.SetJsonAsync(cacheKey, result, cancellationToken); // Ghi.
        } // Hết set.
        return result; // Response.
    } // Kết thúc GetTreeFlatFlattenRoutePagedAsync.

    // [13] Route: GET /api/comments/tree/cte/flatten
    public async Task<PagedResult<CommentFlattenCteDto>> GetTreeCteFlattenRoutePagedAsync(
        Guid? postId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetTreeCteFlattenRoutePagedAsync — CommentFlattenCteDto sau cây CommentTreeCteDto.
        // BƯỚC 0 — Epoch list chung — sau mỗi CRUD InvalidateCommentsListsAsync làm các key CommentsFlattenedCteTree* “lệch thế hệ”.
        var cmtEpoch = await _listEpoch.GetCommentsListEpochAsync(cancellationToken); // Một snapshot số đếm trong cache phân tán.

        // BƯỚC 1 — Thử đọc cache flatten-CTE khi không suppress.
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Cache hợp lệ.
        { // Mở đọc.
            var cacheKey = postId is { } p ? EntityCacheKeys.CommentsFlattenedCteTree(cmtEpoch, p, page, pageSize, CommentSortCacheKey(sort)) : EntityCacheKeys.CommentsAllFlattenCteTree(cmtEpoch, page, pageSize, CommentSortCacheKey(sort)); // Khóa flatten-CTE có epoch.
            var cached = await Cache.GetJsonAsync<PagedResult<CommentFlattenCteDto>>(cacheKey, cancellationToken); // Đọc cache.
            if (cached is not null) // Hit.
                return cached; // Trả sớm.
        } // Hết đọc.
        // BƯỚC 2 — BuildCteTreesPagedCoreAsync (pipeline CTE chung).
        var (cteRootsOnPage, totalCteRootNodes, totalCommentsMatchingFilter) = await BuildCteTreesPagedCoreAsync(postId, page, pageSize, cancellationToken, createdAtFrom, createdAtTo, userId, contentContains, sort); // Pipeline CTE chung.
        // BƯỚC 3 — Preorder CommentTreeCteDto → CommentFlattenCteDto.
        var preorderRows = new List<CommentFlattenCteDto>(); // DTO riêng route tree/cte/flatten.
        foreach (var root in cteRootsOnPage) // Mỗi gốc trang.
            FlattenTreeCteToFlattenCteDto(root, preorderRows); // Preorder giữ Level.
        // BƯỚC 4 — Gói PagedResult.
        var result = CommentPagedResult.ForCtePagedByRootNodes(preorderRows, page, pageSize, totalCommentsMatchingFilter, totalCteRootNodes); // Gói phân trang.
        // BƯỚC 5 — Ghi cache khi không suppress; epoch trùng luồng đọc ở BƯỚC 1.
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Ghi cache.
        { // Mở set.
            var cacheKey = postId is { } p2 ? EntityCacheKeys.CommentsFlattenedCteTree(cmtEpoch, p2, page, pageSize, CommentSortCacheKey(sort)) : EntityCacheKeys.CommentsAllFlattenCteTree(cmtEpoch, page, pageSize, CommentSortCacheKey(sort)); // Khóa đồng bộ đọc.
            await Cache.SetJsonAsync(cacheKey, result, cancellationToken); // Lưu.
        } // Hết set.
        return result; // Trả về client.
    } // Kết thúc GetTreeCteFlattenRoutePagedAsync.

    // [2a] Resource: GET /api/posts/{postId}/comments/tree — cache JSON riêng (EntityCacheKeys.PostsResourceCommentsTreeCte), không dùng khóa /api/comments/*.
    public async Task<IReadOnlyList<CommentTreeCteDto>> GetCommentsTreeForPostAsync(
        Guid postId,
        bool includeReplies = true,
        SortByColumn? sort = null,
        CancellationToken cancellationToken = default)
    { // Mở khối GetCommentsTreeForPostAsync.
        // BƯỚC 1 — Xác nhận bài tồn tại (404 PostNotFound nếu không có trong bảng Posts).
        await EnsurePostExistsAsync(postId); // Luôn kiểm tra trước cache để không trả dữ liệu khi post đã xóa.
        // BƯỚC 2 — Cache-aside: khóa chỉ dành cho resource post /comments/tree.
        var cacheKey = EntityCacheKeys.PostsResourceCommentsTreeCte(postId, includeReplies, CommentSortCacheKey(sort)); // l:posts:{id}:comments:tree:cte:…
        var cached = await Cache.GetJsonAsync<List<CommentTreeCteDto>>(cacheKey, cancellationToken); // Deserialize hoặc miss.
        if (cached is not null) // Hit cache.
            return cached; // Trả rừng đã lưu, không gọi DB.
        // BƯỚC 3 — GetAllCommentsForPost: SqlQueryRaw CTE độc lập; mọi dòng có Level từ SQL.
        var rows = await _repository.GetAllCommentsForPost(postId, includeReplies, cancellationToken, sort); // Một round-trip CTE.
        // BƯỚC 4 — BuildTreeCte rồi ghi cache (List để JSON ổn định).
        var forest = BuildTreeCte(rows); // Rừng CommentTreeCteDto trong RAM.
        await Cache.SetJsonAsync(cacheKey, forest, cancellationToken); // TTL mặc định EntityResponseCache.
        return forest; // Rỗng nếu bài không có comment (hoặc includeReplies=false và không có gốc).
    } // Kết thúc GetCommentsTreeForPostAsync.

    // [2b] Resource: GET /api/posts/{postId}/comments/flat — cache JSON riêng (EntityCacheKeys.PostsResourceCommentsFlatCte), không BuildTreeCte.
    public async Task<IReadOnlyList<CommentCteDto>> GetCommentsFlatForPostAsync(
        Guid postId,
        bool includeReplies = true,
        SortByColumn? sort = null,
        CancellationToken cancellationToken = default)
    { // Mở khối GetCommentsFlatForPostAsync.
        // BƯỚC 1 — Xác nhận bài tồn tại.
        await EnsurePostExistsAsync(postId); // 404 khi post không có.
        // BƯỚC 2 — Cache-aside: khóa chỉ dành cho resource post /comments/flat (CTE phẳng).
        var cacheKey = EntityCacheKeys.PostsResourceCommentsFlatCte(postId, includeReplies, CommentSortCacheKey(sort)); // l:posts:{id}:comments:flat:cte:…
        var cached = await Cache.GetJsonAsync<List<CommentCteDto>>(cacheKey, cancellationToken); // Miss → null.
        if (cached is not null) // Hit.
            return cached; // Trả danh sách phẳng đã cache.
        // BƯỚC 3 — Một lần gọi CTE repository.
        var flat = await _repository.GetAllCommentsForPost(postId, includeReplies, cancellationToken, sort); // List CommentCteDto.
        await Cache.SetJsonAsync(cacheKey, flat, cancellationToken); // Ghi cache riêng resource post.
        return flat; // Danh sách phẳng có Level.
    } // Kết thúc GetCommentsFlatForPostAsync.

    #endregion

    #region CommentsController — demo kiểu nạp quan hệ (lazy / eager / explicit / projection)

    // Một điểm gọi repository demo: paged=true thì Normalize page/pageSize; paged=false thì (1,1) và bỏ qua tham số trang của caller.
    private async Task<(List<CommentLoadingDemoDto> Items, long Total, int Page, int PageSize)> DemoQueryAsync(
        CommentDemoLoadKind kind,
        bool paged,
        int page,
        int pageSize,
        CancellationToken cancellationToken,
        Guid? postId,
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        Guid? userId,
        string? contentContains,
        SortByColumn? sort)
    { // Mở khối DemoQueryAsync — điểm chung gọi repository demo theo loại nạp.
        // BƯỚC 1 — Nếu có postId: EnsurePostExistsAsync (404 khi post không tồn tại).
        if (postId is { } pid) // Có lọc theo bài.
            await EnsurePostExistsAsync(pid); // Đảm bảo post hợp lệ trước khi demo query.
        // BƯỚC 2 — Chuẩn hóa (page, pageSize) khi paged; unpaged dùng (1,1) vì repository bỏ Skip/Take.
        var (p, s) = paged ? PaginationQuery.Normalize(page, pageSize) : (1, 1); // Chuẩn hóa trang khi paged; unpaged dùng (1,1) làm giả vì repository bỏ qua Skip/Take.
        // BƯỚC 3 — switch kind: gọi đúng hàm demo repository (lazy / eager / explicit / projection).
        var (items, total) = kind switch // Phân nhánh theo chiến lược nạp dữ liệu demo.
        { // Mở biểu thức switch trả tuple.
            CommentDemoLoadKind.Lazy => await _repository.GetCommentsLazyLoadingDemoRouteAsync(paged, p, s, cancellationToken, postId, createdAtFrom, createdAtTo, userId, contentContains, sort), // Lazy: navigation có thể phát sinh thêm SQL sau.
            CommentDemoLoadKind.Eager => await _repository.GetCommentsEagerLoadingDemoRouteAsync(paged, p, s, cancellationToken, postId, createdAtFrom, createdAtTo, userId, contentContains, sort), // Eager: Include + split query gom quan hệ.
            CommentDemoLoadKind.Explicit => await _repository.GetCommentsExplicitLoadingDemoRouteAsync(paged, p, s, cancellationToken, postId, createdAtFrom, createdAtTo, userId, contentContains, sort), // Explicit: LoadAsync từng quan hệ.
            CommentDemoLoadKind.Projection => await _repository.GetCommentsProjectionDemoRouteAsync(paged, p, s, cancellationToken, postId, createdAtFrom, createdAtTo, userId, contentContains, sort), // Projection: Select DTO trên server.
            _ => throw new ArgumentOutOfRangeException(nameof(kind)), // Giá trị enum lạ → lỗi lập trình.
        }; // Kết thúc switch.
        // BƯỚC 4 — Trả tuple (items, total, p, s) để caller bọc PagedResult hoặc list thuần.
        return (items, total, p, s); // Trả dữ liệu + tổng + trang đã chuẩn hóa + cỡ trang.
    } // Kết thúc DemoQueryAsync.

    // [14] Route: GET /api/comments/demo/lazy-loading (paged)
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsLazyLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetCommentsLazyLoadingDemoPagedAsync.
        // BƯỚC 1 — Gọi DemoQueryAsync(Lazy, paged: true, …).
        var (items, total, p, s) = await DemoQueryAsync(CommentDemoLoadKind.Lazy, true, page, pageSize, cancellationToken, postId, createdAtFrom, createdAtTo, userId, contentContains, sort); // Gọi demo lazy với phân trang bật.
        // BƯỚC 2 — Gói ForFlatCommentList → PagedResult thống nhất API.
        return CommentPagedResult.ForFlatCommentList(items, p, s, total); // Gói danh sách DTO demo thành PagedResult thống nhất API.
    } // Kết thúc GetCommentsLazyLoadingDemoPagedAsync.

    // [14] Route: GET /api/comments/demo/lazy-loading (unpaged)
    public async Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsLazyLoadingDemoAsync(
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetAllCommentsLazyLoadingDemoAsync.
        // BƯỚC 1 — Gọi DemoQueryAsync(Lazy, paged: false, …).
        var (items, _, _, _) = await DemoQueryAsync(CommentDemoLoadKind.Lazy, false, 0, 0, cancellationToken, postId, createdAtFrom, createdAtTo, userId, contentContains, sort); // unpaged: bỏ qua page/size thực tế trong repository.
        // BƯỚC 2 — Trả danh sách DTO read-only (không bọc PagedResult).
        return items; // Trả toàn bộ dòng demo lazy đã map.
    } // Kết thúc GetAllCommentsLazyLoadingDemoAsync.

    // [15] Route: GET /api/comments/demo/eager-loading (paged)
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsEagerLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetCommentsEagerLoadingDemoPagedAsync.
        // BƯỚC 1 — Gọi DemoQueryAsync(Eager, paged: true, …).
        var (items, total, p, s) = await DemoQueryAsync(CommentDemoLoadKind.Eager, true, page, pageSize, cancellationToken, postId, createdAtFrom, createdAtTo, userId, contentContains, sort); // Demo eager có phân trang.
        // BƯỚC 2 — Gói PagedResult.
        return CommentPagedResult.ForFlatCommentList(items, p, s, total); // Gói PagedResult.
    } // Kết thúc GetCommentsEagerLoadingDemoPagedAsync.

    // [15] Route: GET /api/comments/demo/eager-loading (unpaged)
    public async Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsEagerLoadingDemoAsync(
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetAllCommentsEagerLoadingDemoAsync.
        // BƯỚC 1 — Gọi DemoQueryAsync(Eager, paged: false, …).
        var (items, _, _, _) = await DemoQueryAsync(CommentDemoLoadKind.Eager, false, 0, 0, cancellationToken, postId, createdAtFrom, createdAtTo, userId, contentContains, sort); // Lấy toàn bộ khớp lọc với eager.
        // BƯỚC 2 — Trả danh sách đầy đủ.
        return items; // Danh sách đầy đủ.
    } // Kết thúc GetAllCommentsEagerLoadingDemoAsync.

    // [16] Route: GET /api/comments/demo/explicit-loading (paged)
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsExplicitLoadingDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetCommentsExplicitLoadingDemoPagedAsync.
        // BƯỚC 1 — Gọi DemoQueryAsync(Explicit, paged: true, …).
        var (items, total, p, s) = await DemoQueryAsync(CommentDemoLoadKind.Explicit, true, page, pageSize, cancellationToken, postId, createdAtFrom, createdAtTo, userId, contentContains, sort); // Explicit có phân trang.
        // BƯỚC 2 — Gói PagedResult.
        return CommentPagedResult.ForFlatCommentList(items, p, s, total); // Gói kết quả.
    } // Kết thúc GetCommentsExplicitLoadingDemoPagedAsync.

    // [16] Route: GET /api/comments/demo/explicit-loading (unpaged)
    public async Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsExplicitLoadingDemoAsync(
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetAllCommentsExplicitLoadingDemoAsync.
        // BƯỚC 1 — Gọi DemoQueryAsync(Explicit, paged: false, …).
        var (items, _, _, _) = await DemoQueryAsync(CommentDemoLoadKind.Explicit, false, 0, 0, cancellationToken, postId, createdAtFrom, createdAtTo, userId, contentContains, sort); // Toàn bộ + LoadAsync từng phần.
        // BƯỚC 2 — Trả list.
        return items; // Trả list.
    } // Kết thúc GetAllCommentsExplicitLoadingDemoAsync.

    // [17] Route: GET /api/comments/demo/projection (paged)
    public async Task<PagedResult<CommentLoadingDemoDto>> GetCommentsProjectionDemoPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetCommentsProjectionDemoPagedAsync.
        // BƯỚC 1 — Gọi DemoQueryAsync(Projection, paged: true, …).
        var (items, total, p, s) = await DemoQueryAsync(CommentDemoLoadKind.Projection, true, page, pageSize, cancellationToken, postId, createdAtFrom, createdAtTo, userId, contentContains, sort); // Projection phân trang.
        // BƯỚC 2 — Gói PagedResult.
        return CommentPagedResult.ForFlatCommentList(items, p, s, total); // Gói PagedResult.
    } // Kết thúc GetCommentsProjectionDemoPagedAsync.

    // [17] Route: GET /api/comments/demo/projection (unpaged)
    public async Task<IReadOnlyList<CommentLoadingDemoDto>> GetAllCommentsProjectionDemoAsync(
        CancellationToken cancellationToken = default,
        Guid? postId = null,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        Guid? userId = null,
        string? contentContains = null,
        SortByColumn? sort = null)
    { // Mở khối GetAllCommentsProjectionDemoAsync.
        // BƯỚC 1 — Gọi DemoQueryAsync(Projection, paged: false, …).
        var (items, _, _, _) = await DemoQueryAsync(CommentDemoLoadKind.Projection, false, 0, 0, cancellationToken, postId, createdAtFrom, createdAtTo, userId, contentContains, sort); // Toàn bộ qua Select SQL.
        // BƯỚC 2 — Trả danh sách DTO projection.
        return items; // Trả danh sách DTO projection.
    } // Kết thúc GetAllCommentsProjectionDemoAsync.

    #endregion

    #region Private — danh sách & tìm kiếm (GET /api/comments, hỗ trợ nội bộ)

    // Phân trang toàn hệ — dùng nội bộ GetCommentListAsync và unit test.
    // commentsListEpoch: số epoch danh sách comment do caller đọc một lần (ví dụ GetCommentListAsync BƯỚC 0) — nhúng vào CommentsFlatAll để đồng bộ TTL/miss sau CRUD.
    private async Task<PagedResult<CommentDto>> PagedGlobalListAsync( // Phân trang toàn cục CommentDto.
        long commentsListEpoch,
        int page, // Số trang (1-based).
        int pageSize, // Số bản ghi mỗi trang.
        CancellationToken cancellationToken = default, // Hủy bất đồng bộ.
        DateTime? createdAtFrom = null, // Lọc CreatedAt inclusive.
        DateTime? createdAtTo = null, // Lọc CreatedAt inclusive.
        Guid? userId = null, // Lọc tác giả.
        string? contentContains = null, // Lọc Contains nội dung.
        SortByColumn? sort = null) // Sort dropdown.
    { // Mở khối PagedGlobalListAsync.
        // BƯỚC 1 — Cache-aside đọc: CommentsFlatAll(commentsListEpoch, …); tham số epoch không đọc lại trong hàm này để tránh lệch với epoch đã dùng ở caller cùng request.
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Không lọc → được cache.
        { // Mở khối cache.
            // commentsListEpoch = giá trị GetCommentsListEpochAsync tại thời điểm bắt đầu xử lý GET — nếu trùng với lúc Set thì hit; bump sau CRUD ⇒ caller truyền epoch mới ⇒ miss JSON cũ.
            var cacheKey = EntityCacheKeys.CommentsFlatAll(commentsListEpoch, page, pageSize, CommentSortCacheKey(sort)); // Chuỗi khóa cmt:{commentsListEpoch}:…:flat:all:…
            var cached = await Cache.GetJsonAsync<PagedResult<CommentDto>>(cacheKey, cancellationToken); // Đọc JSON từ cache; không SQL.
            if (cached is not null) // Có bản trong cache.
            { // Mở khối.
                return cached; // Trả ngay, bỏ qua repository.
            } // Kết thúc nhánh cache hit.
        } // Kết thúc nhánh có thể dùng cache.

        // BƯỚC 2 — LoadFlatAsync(postId: null): COUNT + SELECT một trang phẳng toàn hệ đã lọc.
        var (items, total) = await _repository.LoadFlatAsync(null, page, pageSize, cancellationToken, createdAtFrom, createdAtTo, userId, contentContains, sort); // [1][8][10][12] COUNT + SELECT phẳng.
        // BƯỚC 3 — Map Comment → DTO và ForFlatCommentList (metadata phân trang).
        var result = CommentPagedResult.ForFlatCommentList( // Danh sách phẳng toàn hệ.
            items.Select(_mapper.Map<CommentDto>).ToList(), // Map Comment → DTO.
            page, // Trang.
            pageSize, // Cỡ trang.
            total); // Tổng comment khớp lọc.
        // BƯỚC 4 — Ghi cache CommentsFlatAll khi không suppress; dùng đúng commentsListEpoch của caller để không ghi vào nhánh khóa mà GET /flat không bao giờ đọc.
        if (!SuppressCommentRouteCache(createdAtFrom, createdAtTo, userId, contentContains)) // Chỉ cache khi không lọc.
        { // Mở khối.
            await Cache.SetJsonAsync(EntityCacheKeys.CommentsFlatAll(commentsListEpoch, page, pageSize, CommentSortCacheKey(sort)), result, cancellationToken); // Lưu dưới cùng khóa với GET /comments/flat (post null) và với nhánh reuse PagedGlobalListAsync.
        } // Kết thúc set cache.

        return result; // Trả về cho caller.
    } // Kết thúc PagedGlobalListAsync.

    #endregion

    #region Private helpers

    // Mục đích: quyết định có được phép cache JSON cho các route danh sách comment hay không.
    // TRẢ VỀ true = tắt cache (suppress); false = được phép đọc/ghi cache theo khóa route (flat/tree…).
    private static bool SuppressCommentRouteCache(DateTime? createdAtFrom, DateTime? createdAtTo, Guid? userId, string? contentContains)
    {
        // TRƯỜNG HỢP 1: Có lọc CreatedAt (một hoặc hai biên) — tổ hợp khóa cache quá lớn, dữ liệu dễ stale.
        if (HasCreatedAtFilter(createdAtFrom, createdAtTo))
            return true;

        // TRƯỜNG HỢP 2: Lọc theo tác giả — không dùng chung khóa với list “mặc định”.
        if (userId is not null)
            return true;

        // TRƯỜNG HỢP 3: Có chuỗi tìm trong nội dung — kết quả phụ thuộc term, không cache trong pipeline flat chung.
        if (!string.IsNullOrWhiteSpace(contentContains))
            return true;

        // TRƯỜNG HỢP 4: Không có các lọc trên — được cache (caller vẫn phải chọn đúng EntityCacheKeys).
        return false;
    }

    // Mục đích: gói danh sách đã đủ bản ghi (unpaged) thành PagedResult để API luôn trả cùng shape JSON.
    private static PagedResult<CommentDto> ToUnpagedCommentDtoResult(List<CommentDto> items)
    {
        // BƯỚC 1 — Đếm số phần tử thực tế sau khi map.
        var n = items.Count;

        // BƯỚC 2 — Chọn PageSize: nếu rỗng dùng DefaultPageSize để TotalPages không bị chia cho 0.
        var ps = n == 0 ? PaginationQuery.DefaultPageSize : n;

        // BƯỚC 3 — Gói ForFlatCommentList(Page=1, PageSize=ps, TotalCount=n) — một “trang giả” chứa hết items.
        return CommentPagedResult.ForFlatCommentList(items, 1, ps, n);
    }

    // Pipeline [10][12]: phân trang theo gốc (route tree/flat) → nạp đủ subtree → dựng rừng (mỗi item = một thread đầy đủ).
    private async Task<(List<CommentTreeDto> Roots, long TotalComments, long TotalRootNodesInTable)> BuildFlatTreesPagedCoreAsync( // Tuple: rừng trang + tổng comment + tổng gốc.
        Guid? postId, // null = toàn hệ; có giá trị = một bài.
        int page, // Trang trên danh sách gốc (PostId → CreatedAt → Id của comment ParentId null).
        int pageSize, // Số gốc mỗi trang.
        CancellationToken cancellationToken = default, // Hủy truy vấn.
        DateTime? createdAtFrom = null, // Lọc CreatedAt (áp trên hàng gốc khi phân trang).
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        Guid? userId = null, // Lọc tác giả.
        string? contentContains = null, // Lọc nội dung.
        SortByColumn? sort = null) // Thứ tự gốc + subtree (dropdown).
    { // Mở khối BuildFlatTreesPagedCoreAsync.
        // BƯỚC 1 — Nếu có postId: EnsurePostExistsAsync (404 khi không tồn tại).
        if (postId is { } pid) // Nếu client truyền postId.
        { // Mở khối kiểm tra post.
            await EnsurePostExistsAsync(pid); // Đồng bộ validation cho mọi route có lọc theo post.
        } // Kết thúc kiểm tra post.

        // BƯỚC 2 — COUNT mọi comment khớp lọc (TotalComments / metadata giống route CTE).
        var totalCommentsInTable = await _repository.CountCommentsMatchingRouteAsync( // TotalComments: COUNT mọi comment khớp lọc (metadata giống route CTE).
            postId, // Post.
            contentContains, // Nội dung.
            cancellationToken, // Hủy.
            createdAtFrom, // Ngày.
            createdAtTo, // Ngày.
            userId); // User.

        // BƯỚC 3 — COUNT comment gốc (ParentId null) khớp lọc — mẫu số totalPages theo gốc.
        var totalRootsInTable = await _repository.CountCommentRootsMatchingRouteAsync( // Tổng gốc khớp lọc — mẫu số totalPages.
            postId, // Post.
            contentContains, // Nội dung.
            cancellationToken, // Hủy.
            createdAtFrom, // Ngày.
            createdAtTo, // Ngày.
            userId); // User.

        // BƯỚC 4 — Lấy một trang entity gốc (chưa có con) theo Skip/Take trên tập gốc đã lọc.
        var (pageRoots, _) = await _repository.GetCommentRootsRoutePagedAsync( // Một trang gốc (chưa có con).
            postId, // Lọc bài hoặc null.
            contentContains, // Contains tùy chọn.
            page, // Trang.
            pageSize, // Số gốc.
            cancellationToken, // Hủy.
            createdAtFrom, // Ngày.
            createdAtTo, // Ngày.
            userId, // User.
            sort); // Sort gốc theo query.

        // BƯỚC 5 — Trang gốc rỗng: trả rừng rỗng kèm metadata tổng (early return).
        if (pageRoots.Count == 0) // Trang gốc rỗng.
        { // Mở khối early return.
            return (new List<CommentTreeDto>(), totalCommentsInTable, totalRootsInTable); // Không cây; metadata đầy đủ.
        } // Kết thúc early return.

        // BƯỚC 6 — Gom Id gốc trang rồi LoadCommentsForSubtreesAsync (BFS repository) nạp gốc + hậu duệ.
        var rootIds = pageRoots.Select(r => r.Id).ToList(); // Id gốc trang hiện tại.
        var subtreeRows = await _repository.LoadCommentsForSubtreesAsync(rootIds, cancellationToken, sort); // Gốc + mọi hậu duệ; sắp kết quả theo sort.
        // BƯỚC 7 — BuildTreeFlat: dựng rừng CommentTreeDto từ tập phẳng subtree.
        var forest = BuildTreeFlat(subtreeRows); // Dựng cây lồng từ tập đóng subtree.
        // BƯỚC 8 — Dictionary Id → nút gốc để ghép lại đúng thứ tự pageRoots.
        var byId = forest.ToDictionary(x => x.Id); // Id → gốc cây (chỉ tầng 1 cần thứ tự trang).
        var orderedRoots = new List<CommentTreeDto>(); // Giữ đúng thứ tự GetCommentRootsRoutePagedAsync.
        // BƯỚC 9 — foreach pageRoots: TryGetValue và Add theo thứ tự trang SQL.
        foreach (var r in pageRoots) // Thứ tự gốc như OFFSET trên SQL.
        { // Mở khối reorder.
            if (byId.TryGetValue(r.Id, out var treeRoot)) // Tìm cây đã build.
            { // Mở khối.
                orderedRoots.Add(treeRoot); // Ghép theo trang gốc.
            } // Kết thúc nhánh tìm thấy.
        } // Kết thúc foreach thứ tự.

        // BƯỚC 10 — Trả rừng trang + metadata tổng comment + tổng gốc bảng.
        return (orderedRoots, totalCommentsInTable, totalRootsInTable); // Rừng trang + metadata.
    } // Kết thúc BuildFlatTreesPagedCoreAsync.

    // Pipeline CTE [09][11][13]: COUNT bảng + CTE đầy đủ; TotalPages theo số gốc CTE; TotalComments = tổng bản ghi bảng khớp lọc.
    private async Task<(List<CommentTreeCteDto> PagedRoots, long TotalRootNodesInCte, long TotalCommentsInTable)> BuildCteTreesPagedCoreAsync( // Tuple: gốc trang CommentTreeCteDto + gốc CTE + comment bảng.
        Guid? postId, // Một bài hoặc null (mọi bài).
        int page, // Trang trên danh sách gốc đã sắp.
        int pageSize, // Số gốc mỗi trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Tham số lọc chuyển xuống SQL CTE.
        DateTime? createdAtTo = null, // Tham số lọc chuyển xuống SQL CTE.
        Guid? userId = null, // Tham số lọc chuyển xuống SQL CTE.
        string? contentContains = null, // Tham số LIKE chuyển xuống SQL CTE.
        SortByColumn? sort = null) // ORDER BY CTE + sắp gốc RAM theo dropdown.
    { // Mở khối BuildCteTreesPagedCoreAsync.
        // BƯỚC 1 — Nếu có postId: EnsurePostExistsAsync.
        if (postId is { } pid) // Có postId từ client.
        { // Mở khối validate post.
            await EnsurePostExistsAsync(pid); // Đồng bộ validation giữa tất cả route cte theo post.
        } // Kết thúc validate post.

        // BƯỚC 2 — COUNT bảng Comments khớp lọc (TotalComments; có thể > số dòng CTE nếu có mồ côi).
        var totalCommentsTable = await _repository.CountCommentsMatchingRouteAsync( // TotalComments: COUNT bảng cùng lọc (có thể > số dòng CTE nếu dữ liệu mồ côi).
            postId, // Post.
            contentContains, // Nội dung.
            cancellationToken, // Hủy.
            createdAtFrom, // Ngày.
            createdAtTo, // Ngày.
            userId); // User.

        // BƯỚC 3 — SqlQueryRaw một lần: toàn bộ hàng phẳng có Level (đã lọc trong SQL).
        var rows = await _repository.LoadRawCteAsync(postId, cancellationToken, createdAtFrom, createdAtTo, userId, contentContains, sort); // Lấy toàn bộ hàng phẳng có Level (SQL đã ORDER BY whitelist).
        // BƯỚC 4 — BuildTreeCte: nhóm theo PostId, dựng rừng gốc CommentTreeCteDto.
        var roots = BuildTreeCte(rows);

        // BƯỚC 5 — Sắp danh sách gốc theo sort (đồng bộ với GetCommentRootsRoutePagedAsync / dropdown).
        var orderedRoots = CommentRepository.SortCommentTreeCteRootsForPaging(roots, sort ?? CommentRepository.CommentListSortDefault); // Thứ tự gốc trước Skip/Take trong RAM.

        // BƯỚC 6 — totalRootNodesInCte = số gốc trong kết quả CTE (mẫu số TotalPages).
        var totalRootNodesInCte = (long)orderedRoots.Count; // TotalNodes: số gốc trong kết quả CTE (TotalPages theo giá trị này).
        // BƯỚC 7 — Skip/Take trên list gốc đã sắp → pagedRoots (phân trang gốc trong RAM).
        var pagedRoots = orderedRoots // Áp phân trang cổ điển trên list gốc.
            .Skip((page - 1) * pageSize) // Bỏ các gốc của trang trước.
            .Take(pageSize) // Giữ đúng số gốc một trang.
            .ToList(); // List gốc của trang hiện tại.

        // BƯỚC 8 — Trả tuple (gốc trang, tổng gốc CTE, tổng comment bảng).
        return (pagedRoots, totalRootNodesInCte, totalCommentsTable); // Trả cây đã phân trang gốc + metadata.
    } // Kết thúc BuildCteTreesPagedCoreAsync.

    // Xóa cache resource GET /api/posts/{postId}/comments/tree|flat (mọi includeReplies × sort) — prefix l:posts:…, không đụng l:comments:*.
    private Task InvalidatePostsResourceCommentsCachesAsync(Guid postId, CancellationToken cancellationToken = default) =>
        Cache.RemoveManyAsync(EntityCacheKeys.PostsResourceCommentsCteAllKeys(postId), cancellationToken); // Gọi sau CRUD comment / khi cần đồng bộ.

    // Ném 404 nếu post không tồn tại; dùng chung cho endpoint theo postId.
    private async Task EnsurePostExistsAsync(Guid postId) // Id bài viết cần kiểm tra.
    { // Mở khối EnsurePostExistsAsync.
        // BƯỚC 1 — PostExistsAsync; TRƯỜNG HỢP false: ném ApiException 404 PostNotFound.
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
        // BƯỚC 1 — UserExistsAsync; TRƯỜNG HỢP false: ném ApiException 404 UserNotFound.
        if (!await _repository.UserExistsAsync(userId)) // Any trong bảng Users.
        { // Mở khối.
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // HTTP.
                ApiErrorCodes.UserNotFound, // Mã.
                ApiMessages.UserNotFound); // Thông báo.
        } // Hết nhánh không tồn tại.
    } // Kết thúc EnsureUserExistsAsync.

    // Ánh xạ phân trang route [08] (CommentFlatDto, có Level) sang payload list [01] (CommentDto, không có cột Level).
    private static PagedResult<CommentDto> MapPagedCommentFlatToCommentDto(PagedResult<CommentFlatDto> flatPaged)
    { // Mở khối MapPagedCommentFlatToCommentDto — giữ nguyên metadata trang, chỉ đổi kiểu phần tử.
        return new PagedResult<CommentDto> // Gói lại cho GetCommentListAsync khi nhánh có content dùng chung SQL với GET …/flat.
        {
            Items = flatPaged.Items.Select(f => new CommentDto // Mỗi dòng: bỏ Level so với CommentFlatDto.
            {
                Id = f.Id,
                Content = f.Content,
                CreatedAt = f.CreatedAt,
                PostId = f.PostId,
                UserId = f.UserId,
                ParentId = f.ParentId,
            }).ToList(),
            Page = flatPaged.Page,
            PageSize = flatPaged.PageSize,
            TotalCount = flatPaged.TotalCount,
            TotalComments = flatPaged.TotalComments,
            TotalNodes = flatPaged.TotalNodes,
        };
    } // Kết thúc MapPagedCommentFlatToCommentDto.

    // Leo chuỗi ParentId từ commentId để phát hiện chu kỳ hoặc đường đi lặp trong tập hữu hạn.
    private static bool HasCycleFlat(Guid commentId, List<Comment> comments)
    {
        // BƯỚC 1 — Dictionary Id → Comment: tra cứu O(1) khi leo chuỗi ParentId.
        var map = comments.ToDictionary(x => x.Id, x => x);

        // BƯỚC 2 — HashSet visited: phát hiện Id cha lặp lại trên đường leo (chu trình gián tiếp).
        // Nếu một ID xuất hiện lần thứ 2 trong này, nghĩa là ta đang đi vòng tròn.
        var visited = new HashSet<Guid>();

        // BƯỚC 3 — Khởi tạo biên leo ngược: ParentId của nút đang kiểm tra.
        Guid? currentParentId = map[commentId].ParentId;

        // BƯỚC 4 — Vòng lặp leo ngược cho đến khi ParentId null hoặc phát hiện chu trình / chuỗi đứt.
        while (currentParentId is not null)
        {
            // TRƯỜNG HỢP 1: Cha "thất lạc" (Broken Chain)
            // Nếu ID cha được nhắc tên nhưng không tồn tại trong danh sách dữ liệu đang có.
            if (!map.TryGetValue(currentParentId.Value, out var current))
            {
                // Dòng chảy bị đứt đoạn, không thể tạo thành vòng khép kín được nữa.
                // Kết luận: Không có chu kỳ.
                return false;
            }

            // TRƯỜNG HỢP 2: Quay lại điểm xuất phát (Direct Cycle)
            // Ví dụ: A là cha của B, và giờ thấy B lại là cha của A.
            if (current.Id == commentId)
            {
                // Phát hiện ID cha trùng khớp với ID của chính nút bắt đầu.
                return true;
            }

            // TRƯỜNG HỢP 3: Đi vào vòng quẩn quanh ở giữa (Indirect Cycle)
            // Ví dụ: A -> B -> C -> D -> B ... (vòng lặp từ B đến D, không chứa A).
            // Hàm .Add() trả về 'false' nếu ID đó đã tồn tại trong HashSet 'visited'.
            if (!visited.Add(current.Id))
            {
                // Thấy một ông cha đã từng leo qua trước đó => Đang đi vòng tròn.
                return true;
            }

            // LUỒNG CHẠY BÌNH THƯỜNG: Tiếp tục leo lên tầng cao hơn.
            // Lấy ParentId của ông cha hiện tại để chuẩn bị cho vòng lặp kế tiếp.
            currentParentId = current.ParentId;
        }

        // KẾT THÚC AN TOÀN: Đã leo đến tận cùng (gặp null) mà không dính các trường hợp trên.
        // Kết luận: Cây thẳng, an toàn.
        return false;
    }

    // Kiểm tra cycle khi dựng tree CTE từ raw rows.
    private static bool HasCycleCte(Guid commentId, List<CommentCteDto> rows)
    {
        // BƯỚC 1 — Dictionary Id → ParentId (nhẹ hơn lưu nguyên DTO) phục vụ leo ngược.
        var parentById = rows.ToDictionary(x => x.Id, x => x.ParentId);

        // TRƯỜNG HỢP — commentId không có trong rows: không đủ dữ liệu suy chu trình → coi như không có cycle.
        if (!parentById.ContainsKey(commentId))
        {
            // Không tìm thấy dữ liệu về nút này -> Không thể xác định chu kỳ -> Coi như an toàn.
            return false;
        }

        // BƯỚC 2 — HashSet visited ghi nhớ các Id cha đã đi qua trên đường leo.
        var visited = new HashSet<Guid>();

        // BƯỚC 3 — Bắt đầu từ ParentId của nút đang kiểm tra.
        Guid? parentId = parentById[commentId];

        // BƯỚC 4 — Vòng lặp leo ngược theo parentId cho đến null hoặc phát hiện cycle / chuỗi đứt.
        // Tiếp tục leo cho đến khi gặp bình luận gốc (ParentId là null).
        while (parentId is not null)
        {
            // TRƯỜNG HỢP A: Đóng vòng trực tiếp (Direct Cycle).
            // Nếu ID cha quay lại trùng khít với ID nút xuất phát.
            // Ví dụ: Bạn là cha của chính bạn.
            if (parentId == commentId)
            {
                return true; // Phát hiện vòng lặp!
            }

            // TRƯỜNG HỢP B: Phát hiện vòng lặp "đi luẩn quẩn" (Gián tiếp).
            // Hãy tưởng tượng bạn đang đi tìm tổ tiên bằng cách leo ngược lên từng đời cha.
            // Mỗi lần gặp một người cha mới, bạn ghi tên họ vào một cuốn sổ tay (visited).

            // Lệnh .Add giống như việc bạn mở sổ ra ghi tên:
            // - Nếu tên chưa có trong sổ: Ghi thành công (trả về true).
            // - Nếu tên ĐÃ CÓ trong sổ từ trước: Không ghi được nữa (trả về false).
            if (!visited.Add(parentId.Value))
            {
                // GIẢI THÍCH: 
                // Nếu bạn đang leo lên mà lại gặp lại một người cha ĐÃ CÓ trong sổ,
                // điều đó có nghĩa là bạn đang đi bộ theo hình tròn (vòng lặp vô tận).
                // Ví dụ: A là con B, B là con C, C lại là con A -> Cứ thế đi mãi không bao giờ tới đích.

                return true; // BÁO ĐỘNG: Đã phát hiện một cái bẫy vòng lặp!
            }

            // TRƯỜNG HỢP C: Chuỗi bị đứt đoạn (Broken Chain).
            // Thử lấy ID cha của người cha hiện tại.
            // Nếu ID cha hiện tại không có dữ liệu trong 'parentById' (không tìm thấy record).
            if (!parentById.TryGetValue(parentId.Value, out var nextParent))
            {
                // Không thể leo tiếp vì thiếu dữ liệu tầng trên.
                // Một chuỗi không dẫn tới đâu và không quay lại điểm cũ thì không phải là chu kỳ.
                return false;
            }

            // LUỒNG BÌNH THƯỜNG:
            // Gán cha của cha vào biến tạm để tiếp tục leo lên tầng cao hơn nữa.
            parentId = nextParent;
        }

        // KẾT THÚC: Nếu leo được tới tận cùng (parentId == null) mà không dính các lỗi trên.
        // Chúc mừng, đây là một "cây thẳng" (hợp lệ).
        return false;
    }

    // Map cây nội bộ sang DTO tree-flat: giữ Level đồng bộ với CommentTreeDto sau AssignLevelsFlatForest.
    private static CommentTreeFlatDto MapTreeFlat(CommentTreeDto node) // Map một node tree chung sang node tree-flat.
    { // Mở khối MapTreeFlat.
        // BƯỚC 1 — Khởi tạo CommentTreeFlatDto: sao chép scalar + Level (0 = gốc, tăng theo tầng).
        return new CommentTreeFlatDto // Trả DTO tree-flat mới.
        { // Mở initializer tree-flat.
            Id = node.Id, // Id nút.
            Content = node.Content, // Nội dung.
            CreatedAt = node.CreatedAt, // Thời điểm tạo.
            PostId = node.PostId, // Post chứa nút.
            UserId = node.UserId, // Tác giả nút.
            ParentId = node.ParentId, // Id cha (nullable).
            Level = node.Level, // Độ sâu giống pipeline CTE / flatten CTE.
            // BƯỚC 2 — Children.Select(MapTreeFlat).ToList(): đệ quy map cùng quy tắc.
            Children = node.Children.Select(MapTreeFlat).ToList() // Đệ quy map toàn bộ con.
        }; // Kết thúc initializer tree-flat.
    } // Kết thúc MapTreeFlat.

    // Tập định danh cây con (gồm rootId) bằng BFS theo quan hệ ParentId trên danh sách phẳng một post.
    private static HashSet<Guid> BuildSubtreeIdSet(IReadOnlyList<Comment> inPost, Guid rootId) // Danh sách trong post và Id gốc.
    { // Mở khối BuildSubtreeIdSet.
        // BƯỚC 1 — Khởi tạo HashSet kết quả + Queue BFS, enqueue rootId.
        var s = new HashSet<Guid> { rootId }; // Tập đã thăm/kết quả; khởi tạo với gốc.
        var q = new Queue<Guid>(); // Hàng đợi BFS.
        q.Enqueue(rootId); // Đưa gốc vào hàng đợi.
        // BƯỚC 2 — while queue: dequeue cha u, quét inPost tìm ParentId == u, Add Id con mới và enqueue.
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

        // BƯỚC 3 — Trả HashSet Id cây con (gồm root) cho admin reparent / xóa cache.
        return s; // Trả tập Id cây con.
    } // Kết thúc BuildSubtreeIdSet.

    private static List<CommentTreeDto> BuildTreeFlat(List<Comment> comments)
    {
        // BƯỚC 1 — Dictionary Id → CommentTreeDto (sao chép scalar từ entity) để gắn Cha–Con và tra cứu O(1).
        var lookup = comments.ToDictionary(
            x => x.Id,
            x => new CommentTreeDto
            {
                Id = x.Id,
                Content = x.Content,
                CreatedAt = x.CreatedAt,
                PostId = x.PostId,
                UserId = x.UserId,
                ParentId = x.ParentId,
                Level = 0 // Trong luồng tree/flat, Level sẽ được tính toán lại sau nếu cần.
            });

        // Danh sách chứa các bình luận "gốc" (những bình luận nằm ở tầng cao nhất của cây).
        var roots = new List<CommentTreeDto>();

        // BƯỚC 2 — foreach comment đã sắp CreatedAt/Id: gốc / orphan / cycle → roots; hợp lệ → parent.Children.Add.
        foreach (var comment in comments.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id))
        {
            // Lấy đối tượng DTO tương ứng từ bản đồ tra cứu.
            var node = lookup[comment.Id];

            // --- TRƯỜNG HỢP 1: BÌNH LUẬN GỐC CHUẨN ---
            // Nếu ParentId là null, đây hiển nhiên là một bình luận gốc.
            if (comment.ParentId is null)
            {
                roots.Add(node);
                continue; // Đã là gốc thì không cần tìm cha nữa.
            }

            // --- TRƯỜNG HỢP 2: DỮ LIỆU LỆCH (ORPHAN NODE) ---
            // Nếu có ParentId nhưng ID người cha đó lại không nằm trong danh sách được nạp lên.
            // Điều này xảy ra khi: DB bị mất dữ liệu cha, hoặc truy vấn chỉ lấy một phần cây.
            if (!lookup.TryGetValue(comment.ParentId.Value, out var parent))
            {
                // GIẢI PHÁP: Nâng nút này lên làm gốc để người dùng vẫn nhìn thấy nội dung, 
                // thay vì bị ẩn mất do không tìm thấy cha để gắn vào.
                roots.Add(node);
                continue;
            }

            // --- TRƯỜNG HỢP 3: DỮ LIỆU CÓ CHU KỲ (LOOP) ---
            // Sử dụng hàm HasCycleFlat để kiểm tra xem việc gắn nút này vào cha 
            // có tạo thành vòng lặp vô hạn hay không (ví dụ: A -> B -> A).
            if (HasCycleFlat(comment.Id, comments))
            {
                // GIẢI PHÁP: "Chặt đứt" vòng lặp bằng cách nâng nút này lên làm gốc độc lập.
                // Điều này cực kỳ quan trọng để bảo vệ các hàm Render/Flatten ở phía sau không bị đệ quy vô hạn.
                roots.Add(node);
                continue;
            }

            // --- TRƯỜNG HỢP 4: DỮ LIỆU HỢP LỆ ---
            // Nếu vượt qua tất cả các kiểm tra trên, tiến hành gắn nút hiện tại vào danh sách con của cha.
            parent.Children.Add(node);
        }

        // BƯỚC 3 — Gán Level theo độ sâu thực (BFS từ mỗi gốc): đồng bộ với semantics Level của CTE (0 = gốc, orphan/cycle root = 0).
        AssignLevelsFlatForest(roots);

        // Trả về danh sách các gốc. Mỗi gốc giờ đây đã chứa đầy đủ các "Children" lồng nhau.
        return roots;
    }

    // Gán Level cho toàn bộ nút trong rừng flat: mỗi gốc BFS, con = cha.Level + 1 (thứ tự con theo CreatedAt/Id khi enqueue).
    private static void AssignLevelsFlatForest(IEnumerable<CommentTreeDto> roots)
    {
        foreach (var root in roots)
        {
            var q = new Queue<(CommentTreeDto Node, int Level)>();
            q.Enqueue((root, 0));
            while (q.Count > 0)
            {
                var (n, level) = q.Dequeue();
                n.Level = level;
                foreach (var child in n.Children.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id))
                    q.Enqueue((child, level + 1));
            }
        }
    }

    // Dựng rừng gốc CommentTreeCteDto từ toàn bộ hàng CTE phẳng: GroupBy(PostId); mỗi nhóm lookup + gắng cha–con + orphan/cycle (HasCycleCte trên tập nhóm); gộp gốc vào forest — toàn bộ trong một hàm, không hàm con.
    private static List<CommentTreeCteDto> BuildTreeCte(List<CommentCteDto> rows)
    { // Mở khối BuildTreeCte.
        // BƯỚC 1 — Guard: rows null hoặc rỗng → trả rừng rỗng.
        if (rows is null || rows.Count == 0) // Không có dòng CTE.
            return new List<CommentTreeCteDto>(); // Rừng rỗng.

        // BƯỚC 2 — Khởi tạo rừng toàn cục; duyệt từng nhóm PostId (thứ tự PostId ổn định).
        var forest = new List<CommentTreeCteDto>(); // Gom mọi gốc của mọi bài.
        foreach (var group in rows.GroupBy(r => r.PostId).OrderBy(g => g.Key)) // Mỗi PostId = một nhóm độc lập.
        { // Mở foreach nhóm bài — logic một nhóm nằm trọn trong khối này.
            // BƯỚC 2a — Materialize nhóm một lần (lookup + HasCycleCte dùng cùng list).
            var groupRows = group.ToList(); // Danh sách dòng CTE thuộc một PostId.

            // BƯỚC 2b — Dictionary Id → nút cây (sao chép scalar + Level từ CTE).
            var lookup = groupRows.ToDictionary(
                x => x.Id,
                x => new CommentTreeCteDto
                {
                    Id = x.Id,
                    Content = x.Content,
                    CreatedAt = x.CreatedAt,
                    PostId = x.PostId,
                    UserId = x.UserId,
                    ParentId = x.ParentId,
                    Level = x.Level,
                });

            // BƯỚC 2c — Gốc của nhóm: ParentId null, orphan (cha không trong nhóm), hoặc có chu trình trên tập nhóm.
            var postRoots = new List<CommentTreeCteDto>(); // Gốc sau xử lý nhóm hiện tại.

            // BƯỚC 2d — Duyệt dòng đã sắp Level → CreatedAt → Id để cha luôn xử lý trước con khi gắng Children.
            foreach (var row in groupRows.OrderBy(x => x.Level).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id)) // Thứ tự an toàn khi nối cây.
            {
                var node = lookup[row.Id]; // Nút ứng với Id dòng hiện tại.

                if (row.ParentId is null) // Gốc logic (neo trong bài).
                {
                    postRoots.Add(node); // Đưa vào danh sách gốc nhóm.
                    continue; // Không cần tìm cha.
                }

                if (!lookup.TryGetValue(row.ParentId.Value, out var parent)) // Cha không có trong nhóm (chuỗi đứt / thiếu dòng).
                {
                    postRoots.Add(node); // Nâng thành gốc để không mất nút trên API.
                    continue;
                }

                if (HasCycleCte(row.Id, groupRows)) // Phát hiện vòng lặp ParentId trong cùng nhóm.
                {
                    postRoots.Add(node); // Tách khỏi nhánh có vòng, tránh đệ quy vô hạn
                    continue;
                }

                parent.Children.Add(node); // Gắn con hợp lệ dưới cha đã có trong lookup.
            }

            // BƯỚC 2e — Ghép gốc nhóm vào forest theo CreatedAt rồi Id (ổn định trong một bài).
            foreach (var root in postRoots.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id)) // Sắp gốc trong nhóm.
                forest.Add(root); // Một gốc một lần thêm vào rừng toàn cục.
        } // Kết thúc foreach nhóm PostId.

        // BƯỚC 3 — Trả rừng toàn cục (PostId tăng dần, trong mỗi bài gốc theo CreatedAt/Id).
        return forest; // Kết quả cho GetCommentsTreeForPostAsync / BuildCteTreesPagedCoreAsync.
    } // Kết thúc BuildTreeCte.

    // Preorder cây CTE → danh sách CommentCteDto (route GET …/comments/cte).
    private static void FlattenTreeCteToCteDto(CommentTreeCteDto node, ICollection<CommentCteDto> sink)
    { // Mở khối FlattenTreeCteToCteDto.
        sink.Add(new CommentCteDto
        {
            Id = node.Id,
            Content = node.Content,
            CreatedAt = node.CreatedAt,
            PostId = node.PostId,
            UserId = node.UserId,
            ParentId = node.ParentId,
            Level = node.Level,
        });
        foreach (var child in node.Children.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id))
            FlattenTreeCteToCteDto(child, sink);
    } // Kết thúc FlattenTreeCteToCteDto.

    // Preorder cây CTE → CommentFlattenCteDto (route GET …/tree/cte/flatten).
    private static void FlattenTreeCteToFlattenCteDto(CommentTreeCteDto node, ICollection<CommentFlattenCteDto> sink)
    { // Mở khối FlattenTreeCteToFlattenCteDto.
        sink.Add(new CommentFlattenCteDto
        {
            Id = node.Id,
            Content = node.Content,
            CreatedAt = node.CreatedAt,
            PostId = node.PostId,
            UserId = node.UserId,
            ParentId = node.ParentId,
            Level = node.Level,
        });
        foreach (var child in node.Children.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id))
            FlattenTreeCteToFlattenCteDto(child, sink);
    } // Kết thúc FlattenTreeCteToFlattenCteDto.

    // Preorder cây RAM tree/flat → CommentFlattenFlatDto (route GET …/tree/flat/flatten).
    private static void FlattenTreeFlatToFlattenFlatDto(CommentTreeDto node, ICollection<CommentFlattenFlatDto> sink)
    { // Mở khối FlattenTreeFlatToFlattenFlatDto.
        sink.Add(new CommentFlattenFlatDto
        {
            Id = node.Id,
            Content = node.Content,
            CreatedAt = node.CreatedAt,
            PostId = node.PostId,
            UserId = node.UserId,
            ParentId = node.ParentId,
            Level = node.Level,
        });
        foreach (var child in node.Children.OrderBy(c => c.CreatedAt).ThenBy(c => c.Id))
            FlattenTreeFlatToFlattenFlatDto(child, sink);
    } // Kết thúc FlattenTreeFlatToFlattenFlatDto.

    #endregion
} // Kết thúc lớp CommentService và không gian tệp.
