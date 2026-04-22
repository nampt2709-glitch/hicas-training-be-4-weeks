using AutoMapper;
using CommentAPI;
using Microsoft.Extensions.Logging.Abstractions;

namespace CommentAPI.Tests;

// Factory AutoMapper dùng chung test: cùng MappingProfile với ứng dụng, không AssertConfigurationIsValid (AM16 + navigation).
internal static class TestMapperFactory
{
    public static IMapper CreateMapper()
    {
        var expr = new MapperConfigurationExpression();
        expr.AddProfile<MappingProfile>();
        var cfg = new MapperConfiguration(expr, NullLoggerFactory.Instance);
        return cfg.CreateMapper();
    }
}
