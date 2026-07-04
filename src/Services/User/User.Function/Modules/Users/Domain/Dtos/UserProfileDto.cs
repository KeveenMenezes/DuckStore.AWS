namespace User.Function.Modules.Users.Dtos;

public record UserProfileDto(
    string UserId,
    string Email,
    string Name,
    string? Phone,
    string? AddressLine,
    string? City,
    string? State,
    string? ZipCode,
    string? Country);
