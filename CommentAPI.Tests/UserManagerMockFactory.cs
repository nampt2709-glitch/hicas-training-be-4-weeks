using CommentAPI.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace CommentAPI.Tests;

// Tạo Mock<UserManager<User>> với constructor Identity đầy đủ; CallBase = true để Setup các phương thức virtual.
internal static class UserManagerMockFactory
{
    public static Mock<UserManager<User>> Create()
    {
        var store = new Mock<IUserStore<User>>();
        // CallBase = false: chỉ hành vi đã Setup; tránh gọi IUserStore thật trong unit test.
        return new Mock<UserManager<User>>(
            store.Object,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<User>(),
            Array.Empty<IUserValidator<User>>(),
            Array.Empty<IPasswordValidator<User>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullServiceProvider.Instance,
            NullLogger<UserManager<User>>.Instance)
        {
            CallBase = false
        };
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        internal static readonly NullServiceProvider Instance = new();

        public object? GetService(Type serviceType) => null;
    }
}
