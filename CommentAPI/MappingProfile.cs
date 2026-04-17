using AutoMapper;
using CommentAPI.DTOs;
using CommentAPI.Entities;

namespace CommentAPI;

public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<User, UserDto>();
        CreateMap<UpdateUserDto, User>();

        CreateMap<Post, PostDto>();
        CreateMap<CreatePostDto, Post>();
        CreateMap<UpdatePostDto, Post>();

        CreateMap<Comment, CommentDto>();
        CreateMap<CreateCommentDto, Comment>();
        CreateMap<UpdateCommentDto, Comment>();
    }
}
