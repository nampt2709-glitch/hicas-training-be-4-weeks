using AutoMapper;
using CommentAPI.DTOs.Comments;
using CommentAPI.DTOs.Posts;
using CommentAPI.DTOs.Users;
using CommentAPI.Entities;

namespace CommentAPI.Mappings;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<User, UserDto>();
        CreateMap<CreateUserDto, User>();
        CreateMap<UpdateUserDto, User>();

        CreateMap<Post, PostDto>();
        CreateMap<CreatePostDto, Post>();
        CreateMap<UpdatePostDto, Post>();

        CreateMap<Comment, CommentDto>();
        CreateMap<CreateCommentDto, Comment>();
        CreateMap<UpdateCommentDto, Comment>();
    }
}
