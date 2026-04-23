using AutoMapper; 
using CommentAPI;
using CommentAPI.DTOs;
using CommentAPI.Entities; 
using CommentAPI.Interfaces; 
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity; 

namespace CommentAPI.Services; 

public class UserService : IUserService // Triển khai use-case user + cache.
{
    #region Trường & hàm tạo — UsersController

    private readonly IUserRepository _repository; // Truy cập dữ liệu user/role batch.
    private readonly UserManager<User> _userManager; // Tạo/xóa user Identity, roles.
    private readonly RoleManager<IdentityRole<Guid>> _roleManager; // Kiểm tra role tồn tại trước khi gán.
    private readonly IMapper _mapper; // Ánh xạ User → UserDto cơ bản.
    private readonly IEntityResponseCache _cache; // Cache-aside JSON.

    public UserService( // Constructor DI.
        IUserRepository repository, // Repo.
        UserManager<User> userManager, // Identity user.
        RoleManager<IdentityRole<Guid>> roleManager, // Identity role.
        IMapper mapper, // AutoMapper.
        IEntityResponseCache cache) // Distributed cache wrapper.
    {
        _repository = repository; // Assign.
        _userManager = userManager; // Assign.
        _roleManager = roleManager; // Assign.
        _mapper = mapper; // Assign.
        _cache = cache; // Assign.
    }

    #endregion

    #region GET — UsersController (GetAll, GetById)

    public async Task<PagedResult<UserDto>> GetPagedAsync( // Trang user + tổng.
        int page, // Số trang 1-based.
        int pageSize, // Kích thước trang.
        CancellationToken cancellationToken = default, // Hủy.
        DateTime? createdAtFrom = null, // Lọc CreatedAt.
        DateTime? createdAtTo = null, // Lọc CreatedAt.
        string? nameContains = null, // Filter Name.
        string? userNameContains = null, // Filter UserName.
        string? emailContains = null) // Filter Email.
    {
        if (!HasUserListFilter(createdAtFrom, createdAtTo, nameContains, userNameContains, emailContains)) // Chỉ cache danh sách thuần.
        {
            var cacheKey = EntityCacheKeys.UsersPaged(page, pageSize); // Khóa list cố định theo trang.
            var cached = await _cache.GetJsonAsync<PagedResult<UserDto>>(cacheKey, cancellationToken); // Thử đọc cache.
            if (cached is not null) // Hit.
                return cached; // Trả ngay DTO phân trang.
        }

        // Lấy một trang user và map kèm vai trò (giữ nguyên cách MapToDtoAsync).
        var (items, total) = await _repository.GetPagedAsync(
            page,
            pageSize,
            cancellationToken,
            createdAtFrom,
            createdAtTo,
            nameContains,
            userNameContains,
            emailContains); // Projection + count.
        var ids = items.ConvertAll(x => x.Id); // Danh sách id để batch roles.
        var rolesByUser = await _repository.GetRoleNamesByUserIdsAsync(ids, cancellationToken); // Một query join roles.
        var list = new List<UserDto>(items.Count); // Dự đoán dung lượng.
        foreach (var row in items) // Ghép từng dòng.
        {
            list.Add(ToUserDto(row, rolesByUser)); // Static helper gán Roles.
        }

        var result = new PagedResult<UserDto> // Gói phản hồi phân trang.
        {
            Items = list, // Dòng trang hiện tại.
            Page = page, // Chỉ số trang.
            PageSize = pageSize, // Cỡ trang.
            TotalCount = total // Tổng bản ghi khớp filter (ở đây toàn bộ users).
        };
        if (!HasUserListFilter(createdAtFrom, createdAtTo, nameContains, userNameContains, emailContains))
            await _cache.SetJsonAsync(EntityCacheKeys.UsersPaged(page, pageSize), result, cancellationToken); // Ghi cache TTL.
        return result; // Trả cho controller.
    }

    public async Task<UserDto> GetByIdAsync(Guid id) // Chi tiết một user.
    {
        // Cache-aside: đọc DTO từ Redis/memory trước; miss thì truy DB rồi ghi lại cache.
        var cacheKey = EntityCacheKeys.User(id); // Key theo id.
        var cached = await _cache.GetJsonAsync<UserDto>(cacheKey, CancellationToken.None); // Đọc (không truyền CT từ caller ở đây).
        if (cached is not null) // Hit.
        {
            return cached; // Trả DTO đầy đủ roles (đã snapshot lúc set).
        }

        var entity = await _repository.GetByIdAsync(id); // Tracked/ không tracking tùy repo — một user.
        if (entity is null) // Không tồn tại.
        {
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // Not found.
                ApiErrorCodes.UserNotFound, // Code.
                ApiMessages.UserNotFound); // Message.
        }

        var dto = await MapToDtoAsync(entity); // UserManager roles live.
        await _cache.SetJsonAsync(cacheKey, dto, default); // Populate cache.
        return dto; // Return.
    }

    #endregion

    #region POST — UsersController (Create)

    public async Task<UserDto> CreateAsync(CreateUserDto dto) // Tạo user + role User mặc định.
    {
        if (await _userManager.FindByNameAsync(dto.UserName) != null) // Trùng username.
        {
            throw new ApiException( // 409 conflict.
                StatusCodes.Status409Conflict, // Conflict.
                ApiErrorCodes.UserNameConflict, // Code.
                ApiMessages.UserNameTaken); // Message.
        }

        var email = string.IsNullOrWhiteSpace(dto.Email) // Email rỗng → placeholder nội bộ.
            ? $"{dto.UserName}@users.local" // Synthetic email để thỏa Identity.
            : dto.Email!.Trim(); // Email thật đã trim.

        var entity = new User // Thực thể trước khi hash password.
        {
            Id = Guid.NewGuid(), // PK client-generated.
            UserName = dto.UserName, // Login.
            Name = dto.Name, // Display.
            Email = email, // Email lưu DB.
            EmailConfirmed = true, // Bỏ qua flow xác nhận trong demo/training.
            CreatedAt = DateTime.UtcNow // Timestamp UTC.
        };

        var result = await _userManager.CreateAsync(entity, dto.Password); // Hash + lưu user.
        if (!result.Succeeded) // Identity validation errors.
        {
            var detail = string.Join(" ", result.Errors.Select(e => e.Description)); // Gộp mô tả.
            throw new ApiException( // 400 với chi tiết.
                StatusCodes.Status400BadRequest, // Bad request.
                ApiErrorCodes.UserCreateFailed, // Code.
                string.IsNullOrWhiteSpace(detail) ? ApiMessages.UserCreateFailed : detail); // Fallback message.
        }

        await _userManager.AddToRoleAsync(entity, "User"); // Gán role mặc định.
        return await MapToDtoAsync(entity); // DTO kèm roles.
    }

    #endregion

    #region PUT — UsersController (Update, UpdateAsAdmin)

    // User thường: chỉ đổi Name; id phải trùng JWT (chặn sửa hộ user khác).
    public async Task UpdateAsSelfAsync(Guid id, UpdateUserDto dto, Guid currentUserId)
    {
        if (id != currentUserId) // Không cho chỉnh profile người khác qua endpoint này.
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.NotResourceAuthor,
                ApiMessages.NotResourceAuthor);
        }

        var entity = await _repository.GetByIdAsync(id); // Load user (tracked).
        if (entity is null) // Missing.
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.UserNotFound,
                ApiMessages.UserNotFound);
        }

        entity.Name = dto.Name; // Chỉ cập nhật tên hiển thị.
        _repository.Update(entity);
        await _repository.SaveChangesAsync();

        await _cache.RemoveAsync(EntityCacheKeys.User(id), default);
    }

    // Admin: Name, UserName, Email, roles thay thế hoàn toàn, mật khẩu tùy chọn — chống trùng login/email và gỡ Admin khỏi admin cuối.
    public async Task UpdateAsAdminAsync(Guid id, AdminUpdateUserDto dto)
    {
        var user = await _repository.GetByIdAsync(id);
        if (user is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.UserNotFound,
                ApiMessages.UserNotFound);
        }

        var normalizedRoles = NormalizeAdminRolesOrThrow(dto.Roles);
        foreach (var role in normalizedRoles)
        {
            if (!await _roleManager.RoleExistsAsync(role))
            {
                throw new ApiException(
                    StatusCodes.Status400BadRequest,
                    ApiErrorCodes.UserInvalidRoles,
                    ApiMessages.UserInvalidRoles);
            }
        }

        var trimmedUserName = dto.UserName.Trim();
        var otherByName = await _userManager.FindByNameAsync(trimmedUserName);
        if (otherByName is not null && otherByName.Id != id)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.UserNameConflict,
                ApiMessages.UserNameTaken);
        }

        var emailToStore = string.IsNullOrWhiteSpace(dto.Email)
            ? $"{trimmedUserName}@users.local"
            : dto.Email!.Trim();

        var otherByEmail = await _userManager.FindByEmailAsync(emailToStore);
        if (otherByEmail is not null && otherByEmail.Id != id)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.UserEmailConflict,
                ApiMessages.UserEmailTaken);
        }

        var currentRoles = (await _userManager.GetRolesAsync(user)).ToList();
        var hadAdmin = currentRoles.Any(r => r.Equals("Admin", StringComparison.OrdinalIgnoreCase));
        var keepsAdmin = normalizedRoles.Contains("Admin");
        if (hadAdmin && !keepsAdmin)
        {
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            if (admins.Count == 1 && admins[0].Id == id)
            {
                throw new ApiException(
                    StatusCodes.Status400BadRequest,
                    ApiErrorCodes.UserLastAdminProtected,
                    ApiMessages.UserLastAdminProtected);
            }
        }

        user.Name = dto.Name.Trim();

        ThrowUnlessSucceeded(
            await _userManager.SetUserNameAsync(user, trimmedUserName));

        user.EmailConfirmed = true;
        ThrowUnlessSucceeded(
            await _userManager.SetEmailAsync(user, emailToStore));

        ThrowUnlessSucceeded(
            await _userManager.RemoveFromRolesAsync(user, currentRoles));
        ThrowUnlessSucceeded(
            await _userManager.AddToRolesAsync(user, normalizedRoles));

        if (!string.IsNullOrWhiteSpace(dto.NewPassword))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            ThrowUnlessSucceeded(
                await _userManager.ResetPasswordAsync(user, token, dto.NewPassword!));
        }

        await _cache.RemoveAsync(EntityCacheKeys.User(id), default);
    }

    #endregion

    #region DELETE — UsersController (Delete)

    public async Task DeleteAsync(Guid id) // Xóa user Identity.
    {
        var entity = await _repository.GetByIdAsync(id); // Find.
        if (entity is null) // Not found.
        {
            throw new ApiException( // 404.
                StatusCodes.Status404NotFound, // 404.
                ApiErrorCodes.UserNotFound, // Code.
                ApiMessages.UserNotFound); // Msg.
        }

        await _cache.RemoveAsync(EntityCacheKeys.User(id), default); // Xóa cache trước khi xóa user (tránh stale read).

        var result = await _userManager.DeleteAsync(entity); // Cascade theo cấu hình Identity/EF.
        if (!result.Succeeded) // Identity error.
        {
            var detail = string.Join(" ", result.Errors.Select(e => e.Description)); // Details.
            throw new ApiException( // 400.
                StatusCodes.Status400BadRequest, // 400.
                ApiErrorCodes.UserDeleteFailed, // Code.
                string.IsNullOrWhiteSpace(detail) ? ApiMessages.UserDeleteFailed : detail); // Msg.
        }
    }

    #endregion

    #region Private helpers

    // Có filter list → không cache.
    private static bool HasUserListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        string? nameContains,
        string? userNameContains,
        string? emailContains) =>
        createdAtFrom.HasValue
        || createdAtTo.HasValue
        || !string.IsNullOrWhiteSpace(nameContains)
        || !string.IsNullOrWhiteSpace(userNameContains)
        || !string.IsNullOrWhiteSpace(emailContains);

    // Chuẩn hóa danh sách role (Admin/User, không trùng); rỗng sau lọc → lỗi 400.
    private static List<string> NormalizeAdminRolesOrThrow(IReadOnlyList<string> roles)
    {
        if (roles is null || roles.Count == 0)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.UserInvalidRoles,
                ApiMessages.UserInvalidRoles);
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in roles)
        {
            var t = raw?.Trim() ?? "";
            if (t.Length == 0)
            {
                continue;
            }

            string canon;
            if (t.Equals("Admin", StringComparison.OrdinalIgnoreCase))
            {
                canon = "Admin";
            }
            else if (t.Equals("User", StringComparison.OrdinalIgnoreCase))
            {
                canon = "User";
            }
            else
            {
                throw new ApiException(
                    StatusCodes.Status400BadRequest,
                    ApiErrorCodes.UserInvalidRoles,
                    ApiMessages.UserInvalidRoles);
            }

            if (seen.Add(canon))
            {
                result.Add(canon);
            }
        }

        if (result.Count == 0)
        {
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.UserInvalidRoles,
                ApiMessages.UserInvalidRoles);
        }

        result.Sort(StringComparer.Ordinal);
        return result;
    }

    // IdentityResult không Success → 400 với mã UserUpdateFailed (chi tiết từ Identity nếu có).
    private static void ThrowUnlessSucceeded(IdentityResult result)
    {
        if (result.Succeeded)
        {
            return;
        }

        var detail = string.Join(" ", result.Errors.Select(e => e.Description));
        throw new ApiException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.UserUpdateFailed,
            string.IsNullOrWhiteSpace(detail) ? ApiMessages.UserUpdateFailed : detail);
    }

    private async Task<UserDto> MapToDtoAsync(User entity) // Map + đồng bộ roles từ UserManager.
    {
        var dto = _mapper.Map<UserDto>(entity); // Trường scalar.
        var roles = await _userManager.GetRolesAsync(entity); // Danh sách role hiện tại.
        dto.Roles = roles.OrderBy(r => r).ToList(); // Ổn định thứ tự trả API.
        return dto; // DTO đầy đủ.
    }

    // Ghép hàng projection UserPageRow với role đã batch-load.
    private static UserDto ToUserDto(UserPageRow row, IReadOnlyDictionary<Guid, List<string>> rolesByUser) // Không gọi UserManager từng dòng.
    {
        return new UserDto // Manual projection.
        {
            Id = row.Id, // PK.
            Name = row.Name, // Display name.
            UserName = row.UserName, // Login.
            Email = row.Email, // Email.
            CreatedAt = row.CreatedAt, // Audit.
            Roles = rolesByUser.TryGetValue(row.Id, out var r) ? r : new List<string>() // Roles hoặc rỗng.
        };
    }

    #endregion
}
