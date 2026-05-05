using AutoMapper;
using CommentAPI;
using CommentAPI.Data;
using CommentAPI.DTOs;
using CommentAPI.Entities;
using CommentAPI.Interfaces;
using CommentAPI.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Services;

// Nghiệp vụ User: phân trang + cache, chi tiết user, tạo/sửa/xóa; admin đổi role với bảo vệ admin cuối.
public class UserService : ServiceBase, IUserService
{
    #region Trường & hàm tạo — UsersController

    private readonly IUserRepository _repository; // Paged projection + batch roles + GetById entity.
    private readonly UserManager<User> _userManager; // CreateAsync, roles, SetUserName, SetEmail, Delete.
    private readonly RoleManager<IdentityRole<Guid>> _roleManager; // RoleExistsAsync trước khi gán role admin.
    private readonly IMapper _mapper; // User → UserDto scalar.
    private readonly AppDbContext _dbContext; // Xóa comment “lạc” trước Delete user để không kẹt FK NoAction.
    private readonly ICacheListEpochStore _listEpoch; // usr:* list + InvalidateCommentsLists khi xóa comment gắn user.

    public UserService(
        IUserRepository repository,
        UserManager<User> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        IMapper mapper,
        IEntityResponseCache cache,
        AppDbContext dbContext,
        ICacheListEpochStore listEpoch)
        : base(cache)
    {
        _repository = repository;
        _userManager = userManager;
        _roleManager = roleManager;
        _mapper = mapper;
        _dbContext = dbContext;
        _listEpoch = listEpoch;
    }

    #endregion

    #region Route Functions

    // [1] GET /api/users — cache khi không filter; batch roles một query.
    public async Task<PagedResult<UserDto>> GetPagedAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default,
        DateTime? createdAtFrom = null,
        DateTime? createdAtTo = null,
        string? nameContains = null,
        string? userNameContains = null,
        string? emailContains = null,
        SortByColumn? sort = null)
    {
        var sortKey = sort ?? UserRepository.UserListSortDefault;
        // BƯỚC 1: Thử cache chỉ khi không có bất kỳ filter list nào.
        if (!HasUserListFilter(createdAtFrom, createdAtTo, nameContains, userNameContains, emailContains))
        {
            var usr = await _listEpoch.GetUsersListEpochAsync(cancellationToken);
            var cacheKey = EntityCacheKeys.UsersPaged(usr, page, pageSize, sortKey);
            var cached = await Cache.GetJsonAsync<PagedResult<UserDto>>(cacheKey, cancellationToken);
            if (cached is not null)
                return cached;
        }

        // BƯỚC 2: Lấy trang UserPageRow + total — repo đã Normalize page/size bên trong.
        var (items, total) = await _repository.GetPagedAsync(
            page,
            pageSize,
            cancellationToken,
            createdAtFrom,
            createdAtTo,
            nameContains,
            userNameContains,
            emailContains,
            sort);

        // BƯỚC 3: Gom Id trang hiện tại để một query batch roles.
        var ids = items.ConvertAll(x => x.Id);
        var rolesByUser = await _repository.GetRoleNamesByUserIdsAsync(ids, cancellationToken);

        // BƯỚC 4: Ghép từng dòng với dictionary roles — không gọi UserManager từng user.
        var list = new List<UserDto>(items.Count);
        foreach (var row in items)
        {
            list.Add(ToUserDto(row, rolesByUser));
        }

        // BƯỚC 5: Gói PagedResult.
        var result = new PagedResult<UserDto>
        {
            Items = list,
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        };

        // BƯỚC 6: Set cache nếu không filter.
        if (!HasUserListFilter(createdAtFrom, createdAtTo, nameContains, userNameContains, emailContains))
        {
            var usr = await _listEpoch.GetUsersListEpochAsync(cancellationToken);
            await Cache.SetJsonAsync(EntityCacheKeys.UsersPaged(usr, page, pageSize, sortKey), result, cancellationToken);
        }

        return result;
    }

    // [2] GET /api/users/{id} — cache-aside; miss thì MapToDtoAsync kèm roles live từ UserManager.
    public async Task<UserDto> GetByIdAsync(Guid id)
    {
        var cacheKey = EntityCacheKeys.User(id);
        var cached = await Cache.GetJsonAsync<UserDto>(cacheKey, CancellationToken.None);
        if (cached is not null)
            return cached;

        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.UserNotFound,
                ApiMessages.UserNotFound);
        }

        var dto = await MapToDtoAsync(entity);
        await Cache.SetJsonAsync(cacheKey, dto, default);
        return dto;
    }

    // [3] POST /api/users — tạo Identity user + role User; conflict username → 409.
    public async Task<UserDto> CreateAsync(CreateUserDto dto)
    {
        // TRƯỜNG HỢP A: UserName đã tồn tại.
        if (await _userManager.FindByNameAsync(dto.UserName) != null)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.UserNameConflict,
                ApiMessages.UserNameTaken);
        }

        // BƯỚC 1: Email rỗng → placeholder nội bộ để Identity không lỗi validation email.
        var email = string.IsNullOrWhiteSpace(dto.Email)
            ? $"{dto.UserName}@users.local"
            : dto.Email!.Trim();

        // BƯỚC 2: Entity User trước khi CreateAsync (Identity sẽ hash password).
        var entity = new User
        {
            Id = Guid.NewGuid(),
            UserName = dto.UserName,
            Name = dto.Name,
            Email = email,
            EmailConfirmed = true,
            CreatedAt = DateTime.UtcNow,
        };

        // BƯỚC 3: Create — Identity validate password policy.
        var result = await _userManager.CreateAsync(entity, dto.Password);
        if (!result.Succeeded)
        {
            var detail = string.Join(" ", result.Errors.Select(e => e.Description));
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.UserCreateFailed,
                string.IsNullOrWhiteSpace(detail) ? ApiMessages.UserCreateFailed : detail);
        }

        // BƯỚC 4: Gán role mặc định User.
        await _userManager.AddToRoleAsync(entity, "User");

        // User mới xuất hiện trong GET /api/users phân trang không filter — bơm epoch usr.
        await _listEpoch.InvalidateUsersListAsync(default);

        return await MapToDtoAsync(entity);
    }

    // [4] PUT /api/users/{id} — chỉ cho phép user sửa chính mình; chỉ đổi Name.
    public async Task UpdateAsSelfAsync(Guid id, UpdateUserDto dto, Guid currentUserId)
    {
        // TRƯỜNG HỢP: cố sửa profile người khác qua route self.
        if (id != currentUserId)
        {
            throw new ApiException(
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.NotResourceAuthor,
                ApiMessages.NotResourceAuthor);
        }

        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.UserNotFound,
                ApiMessages.UserNotFound);
        }

        entity.Name = dto.Name;
        _repository.Update(entity);
        await _repository.SaveChangesAsync();

        await Cache.RemoveAsync(EntityCacheKeys.User(id), default);
        await _listEpoch.InvalidateUsersListAsync(default);
    }

    // [5] PUT /api/admin/users/{id} — đổi Name, UserName, Email, Roles; kiểm tra role tồn tại; không gỡ Admin cuối cùng.
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

        // BƯỚC 1: Chuẩn hóa danh sách role — chỉ Admin/User, không trùng; rỗng → 400.
        var normalizedRoles = NormalizeAdminRolesOrThrow(dto.Roles);

        // BƯỚC 2: Mỗi role phải tồn tại trong AspNetRoles.
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

        // BƯỚC 3: UserName unique — cho phép trùng nếu chính user đó.
        var trimmedUserName = dto.UserName.Trim();
        var otherByName = await _userManager.FindByNameAsync(trimmedUserName);
        if (otherByName is not null && otherByName.Id != id)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                ApiErrorCodes.UserNameConflict,
                ApiMessages.UserNameTaken);
        }

        // BƯỚC 4: Email unique — placeholder nếu rỗng giống Create.
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

        // BƯỚC 5: Bảo vệ admin cuối — nếu đang có Admin và danh sách mới bỏ Admin, kiểm tra còn ai khác Admin.
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

        // BƯỚC 6: Áp Name + UserName + Email qua UserManager (validation + normalization).
        user.Name = dto.Name.Trim();
        ThrowUnlessSucceeded(await _userManager.SetUserNameAsync(user, trimmedUserName));
        user.EmailConfirmed = true;
        ThrowUnlessSucceeded(await _userManager.SetEmailAsync(user, emailToStore));

        // BƯỚC 7: Thay toàn bộ role — remove hết role cũ rồi add role mới.
        ThrowUnlessSucceeded(await _userManager.RemoveFromRolesAsync(user, currentRoles));
        ThrowUnlessSucceeded(await _userManager.AddToRolesAsync(user, normalizedRoles));

        await Cache.RemoveAsync(EntityCacheKeys.User(id), default);
        await _listEpoch.InvalidateUsersListAsync(default);
    }

    // [6] DELETE /api/users/{id} — xóa comment viết trên post người khác trước (FK NoAction); rồi UserManager.Delete.
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            throw new ApiException(
                StatusCodes.Status404NotFound,
                ApiErrorCodes.UserNotFound,
                ApiMessages.UserNotFound);
        }

        await Cache.RemoveAsync(EntityCacheKeys.User(id), default);

        // BƯỚC 1: Tìm comment của user này trên bài viết không thuộc chính user (Post.UserId != id).
        var authoredCommentsOutsideOwnPosts = await _dbContext.Comments
            .Where(c => c.UserId == id && c.Post != null && c.Post.UserId != id)
            .ToListAsync();

        // BƯỚC 2: Nếu có — RemoveRange (cascade subtree theo cấu hình) + SaveChanges trước Delete user.
        if (authoredCommentsOutsideOwnPosts.Count > 0)
        {
            _dbContext.Comments.RemoveRange(authoredCommentsOutsideOwnPosts);
            await _dbContext.SaveChangesAsync();
            await _listEpoch.InvalidateCommentsListsAsync(default);
        }

        // BƯỚC 3: Xóa user Identity — lỗi Identity → 400 với chi tiết.
        var result = await _userManager.DeleteAsync(entity);
        if (!result.Succeeded)
        {
            var detail = string.Join(" ", result.Errors.Select(e => e.Description));
            throw new ApiException(
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.UserDeleteFailed,
                string.IsNullOrWhiteSpace(detail) ? ApiMessages.UserDeleteFailed : detail);
        }

        await _listEpoch.InvalidateUsersListAsync(default);
    }

    #endregion

    #region Helpers

    // true nếu có filter list — tắt cache UsersPaged.
    private static bool HasUserListFilter(
        DateTime? createdAtFrom,
        DateTime? createdAtTo,
        string? nameContains,
        string? userNameContains,
        string? emailContains) =>
        HasCreatedAtFilter(createdAtFrom, createdAtTo)
        || !string.IsNullOrWhiteSpace(nameContains)
        || !string.IsNullOrWhiteSpace(userNameContains)
        || !string.IsNullOrWhiteSpace(emailContains);

    // Chuẩn hóa role admin: chỉ "Admin" và "User", không trùng (OrdinalIgnoreCase khi nhận, lưu canon), sorted.
    private static List<string> NormalizeAdminRolesOrThrow(IReadOnlyList<string> roles)
    {
        // TRƯỜNG HỢP A: null hoặc rỗng.
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
                continue;

            string canon;
            if (t.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                canon = "Admin";
            else if (t.Equals("User", StringComparison.OrdinalIgnoreCase))
                canon = "User";
            else
            {
                throw new ApiException(
                    StatusCodes.Status400BadRequest,
                    ApiErrorCodes.UserInvalidRoles,
                    ApiMessages.UserInvalidRoles);
            }

            if (seen.Add(canon))
                result.Add(canon);
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

    // IdentityResult failed → ApiException 400 UserUpdateFailed kèm chi tiết lỗi Identity.
    private static void ThrowUnlessSucceeded(IdentityResult result)
    {
        if (result.Succeeded)
            return;

        var detail = string.Join(" ", result.Errors.Select(e => e.Description));
        throw new ApiException(
            StatusCodes.Status400BadRequest,
            ApiErrorCodes.UserUpdateFailed,
            string.IsNullOrWhiteSpace(detail) ? ApiMessages.UserUpdateFailed : detail);
    }

    // Map entity + đọc roles hiện tại từ UserManager — thứ tự role sort ổn định.
    private async Task<UserDto> MapToDtoAsync(User entity)
    {
        var dto = _mapper.Map<UserDto>(entity);
        var roles = await _userManager.GetRolesAsync(entity);
        dto.Roles = roles.OrderBy(r => r).ToList();
        return dto;
    }

    // Ghép UserPageRow với dictionary batch roles — không có key thì Roles = list rỗng.
    private static UserDto ToUserDto(UserPageRow row, IReadOnlyDictionary<Guid, List<string>> rolesByUser)
    {
        return new UserDto
        {
            Id = row.Id,
            Name = row.Name,
            UserName = row.UserName,
            Email = row.Email,
            CreatedAt = row.CreatedAt,
            Roles = rolesByUser.TryGetValue(row.Id, out var r) ? r : new List<string>(),
        };
    }

    #endregion
}
