using CommentAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Tests;

internal static class TestDbContextFactory
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"comment-api-tests-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }
}
