namespace CommentAPI.DTOs.Users;

public class CreateUserDto
{
    public string Name { get; set; } = string.Empty;
}

public class UpdateUserDto
{
    public string Name { get; set; } = string.Empty;
}

public class UserDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
