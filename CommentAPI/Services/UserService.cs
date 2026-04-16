using AutoMapper;
using CommentAPI.DTOs.Users;
using CommentAPI.Entities;
using CommentAPI.Repositories;

namespace CommentAPI.Services;

public class UserService : IUserService
{
    private readonly IUserRepository _repository;
    private readonly IMapper _mapper;

    public UserService(IUserRepository repository, IMapper mapper)
    {
        _repository = repository;
        _mapper = mapper;
    }

    public async Task<List<UserDto>> GetAllAsync()
    {
        var entities = await _repository.GetAllAsync();
        return entities.Select(_mapper.Map<UserDto>).ToList();
    }

    public async Task<UserDto?> GetByIdAsync(Guid id)
    {
        var entity = await _repository.GetByIdAsync(id);
        return entity is null ? null : _mapper.Map<UserDto>(entity);
    }

    public async Task<UserDto> CreateAsync(CreateUserDto dto)
    {
        var entity = _mapper.Map<User>(dto);
        entity.Id = Guid.NewGuid();

        await _repository.AddAsync(entity);
        await _repository.SaveChangesAsync();

        return _mapper.Map<UserDto>(entity);
    }

    public async Task<bool> UpdateAsync(Guid id, UpdateUserDto dto)
    {
        var entity = await _repository.GetByIdAsync(id);
        if (entity is null)
        {
            return false;
        }

        entity.Name = dto.Name;
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
