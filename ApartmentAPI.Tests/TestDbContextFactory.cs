using ApartmentAPI.Data;
using Microsoft.EntityFrameworkCore;

namespace ApartmentAPI.Tests;

// Factory tạo AppDbContext InMemory tách biệt cho mỗi test.
internal static class TestDbContextFactory
{
    // Tạo DbContext với tên database ngẫu nhiên để tránh chia sẻ trạng thái giữa các test.
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"apartment-api-tests-{Guid.NewGuid()}")
            .Options;

        return new AppDbContext(options);
    }
}
