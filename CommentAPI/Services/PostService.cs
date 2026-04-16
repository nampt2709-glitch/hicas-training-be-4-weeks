using AutoMapper;
using CommentAPI.DTOs.Posts;
using CommentAPI.Entities;
using CommentAPI.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CommentAPI.Services;

public class PostService : IPostService
{
    private readonly IPostRepository _repository;
    private readonly IMapper _mapper;

    public PostService(IPostRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<PostDto>> GetAllAsync()
    {
        var entities = await _repository.GetAllAsync();
        return entities.Select(_mapper.Map<PostDto>).ToList();
    }

    public async Task<PostDto?> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        return entity is null ? null : _mapper.Map<PostDto>(entity);
    }

    public async Task<PostDto> CreateAsync(CreatePostDto dto)
    {
        var entity = _mapper.Map<Post>(dto);
        entity.Id = Guid.NewGuid();

        await _repository.AddAsync(entity);
        await _repository.SaveChangesAsync();

        return _mapper.Map<PostDto>(entity);
    }

    public async Task<bool> UpdateAsync(Guid id, UpdatePostDto dto)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            return false;
        }

        entity.Title = dto.Title;
        entity.Content = dto.Content;
        _repository.Update(entity);
        await _repository.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            return false;
        }

        _repository.Remove(entity);
        await _repository.SaveChangesAsync();
        return true;
    }
}
