using Dapper;
using FlexiFit.Api.DTOs;
using FlexiFit.Api.Entities;
using Npgsql;

namespace FlexiFit.Api.Services;

public class UserService : IUserService
{
    private readonly string _connectionString;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IConfiguration configuration,
        ILogger<UserService> logger)
    {
        _connectionString = configuration.GetConnectionString("FlexifitDb")
        ?? throw new InvalidOperationException("Connection string 'FlexifitDb' not found.");
        _logger = logger;

    }

    // =======================================================
    // IMPLEMENTATION OF IUserService (Required by Interface)
    // =======================================================
    public async Task<UsrUser?> GetByFirebaseUidAsync(string firebaseUid)
    {
        var sql = @"SELECT user_id, firebase_uid, email, name, username, is_verified, role, status, auth_provider, created_at, updated_at 
                    FROM usr_users 
                    WHERE firebase_uid = @firebaseUid";
        
        using var connection = new NpgsqlConnection(_connectionString);
        return await connection.QueryFirstOrDefaultAsync<UsrUser>(sql, new { firebaseUid });
    }

    // =======================================================
    // CRUD Operations for User Management 
    // =======================================================

    // CREATE
    // =======================================================
    public async Task CreateUserAsync(UserCreateDto dto)
    {
        var sql = @"INSERT INTO usr_users 
                    (firebase_uid, email, name, username, is_verified, role, status, auth_provider, created_at, updated_at) 
                    VALUES 
                    (@firebase_uid, @email, @name, @username, true, @role, 'ACTIVE', @auth_provider, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)";

        using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.ExecuteAsync(sql, dto);
        
        if (rows == 0)
        {
            // Throw exception kung hindi naka-insert (hal. unique constraint violation)
            throw new InvalidOperationException("Failed to create user. Email or username might already exist.");
        }

    }

    // READ
    // =======================================================
    public async Task<IEnumerable<UserDto>> GetAllUsersAsync()
    {
        var sql = "SELECT * FROM usr_users ORDER BY created_at DESC";
        using var connection = new NpgsqlConnection(_connectionString);

        return await connection.QueryAsync<UserDto>(sql);
    }

    public async Task<UserDto?> GetUserByIdAsync(int id)
    {
        var sql = @"SELECT user_id, firebase_uid, name, username, email, 
                           role, status, is_verified, auth_provider, 
                           created_at, updated_at 
                    FROM usr_users 
                    WHERE user_id = @id";
        
        using var connection = new NpgsqlConnection(_connectionString);

        return await connection.QueryFirstOrDefaultAsync<UserDto>(sql, new { id });
    }

    // UPDATE
    // =======================================================

    public async Task UpdateUserAsync(int id, UserUpdateRequest request)
    {
        var sql = @"UPDATE usr_users 
                    SET name = @name,
                        username = @username,
                        email = @email,
                        role = @role,
                        updated_at = CURRENT_TIMESTAMP
                    WHERE user_id = @user_id";
        
        using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.ExecuteAsync(sql, request);
        
        if (rows == 0)
        {
            // Throw KeyNotFoundException kung wala ang user (para maging 404 sa Middleware)
            throw new KeyNotFoundException($"User with ID {id} not found.");
        }

    }

    // DELETE
    // =======================================================
    public async Task DeleteUserAsync(int id)
    {
        using var connection = new NpgsqlConnection(_connectionString);

        // 1. I-check muna kung existing ang user (Lightweight query)
        var exists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM usr_users WHERE user_id = @id", new { id });
        
        if (exists == 0)
        {
            // Throw KeyNotFoundException para automatic na i-handle ng Global Middleware as 404
            throw new KeyNotFoundException($"User with ID {id} not found.");
        }


        var sql = "DELETE FROM usr_users WHERE user_id = @id";
        await connection.ExecuteAsync(sql, new { id });

    }
}