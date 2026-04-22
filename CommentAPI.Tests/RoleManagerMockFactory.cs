using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CommentAPI.Tests;

// Mock RoleManager<IdentityRole<Guid>> cho unit test UserService (RoleExistsAsync, v.v.).
internal static class RoleManagerMockFactory
{
    public static Mock<RoleManager<IdentityRole<Guid>>> Create()
    {
        var store = new Mock<IRoleStore<IdentityRole<Guid>>>();
        var mock = new Mock<RoleManager<IdentityRole<Guid>>>(
            store.Object,
            Array.Empty<IRoleValidator<IdentityRole<Guid>>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<IdentityRole<Guid>>>.Instance)
        {
            CallBase = false
        };
        mock.Setup(m => m.RoleExistsAsync(It.IsAny<string>())).ReturnsAsync(true);
        return mock;
    }
}
