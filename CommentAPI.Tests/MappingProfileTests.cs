using CommentAPI.DTOs;
using CommentAPI.Entities;

namespace CommentAPI.Tests;

public class MappingProfileTests
{
    // F.I.R.S.T: smoke test map thực tế, không phụ thuộc AssertConfigurationIsValid (AM16 báo unmapped navigations).
    // 3A — Arrange: CreateCommentDto mẫu. Act: Map → Comment → CommentDto. Assert: scalar khớp; nếu gán Content sai kỳ vọng thì fail.
    [Fact]
    public void MP01_MappingProfile_ShouldMapCommentScalars_CreateRoundTrip()
    {
        var mapper = TestMapperFactory.CreateMapper();

        var postId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var create = new CreateCommentDto
        {
            Content = "nội dung",
            PostId = postId,
            UserId = userId,
            ParentId = null
        };

        var entity = mapper.Map<Comment>(create);
        entity.Id = Guid.NewGuid();
        entity.CreatedAt = DateTime.UtcNow;

        var dto = mapper.Map<CommentDto>(entity);

        Assert.Equal("nội dung", dto.Content);
        Assert.Equal(postId, dto.PostId);
        Assert.Equal(userId, dto.UserId);
        Assert.Null(dto.ParentId);
        Assert.NotEqual("sai cố ý", dto.Content);
    }
}
