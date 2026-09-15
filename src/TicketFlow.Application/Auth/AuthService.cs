using TicketFlow.Application.Auth.Dtos;
using TicketFlow.Application.Common.Interfaces;
using TicketFlow.Domain.Entities;
using TicketFlow.Domain.Exceptions;

namespace TicketFlow.Application.Auth;

public class AuthService
{
    private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(7);

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _tokenGenerator;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IUnitOfWork _unitOfWork;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator tokenGenerator,
        IDateTimeProvider dateTimeProvider,
        IUnitOfWork unitOfWork)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _dateTimeProvider = dateTimeProvider;
        _unitOfWork = unitOfWork;
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var existing = await _userRepository.GetByEmailAsync(request.Email, cancellationToken);
        if (existing is not null)
            throw new DomainException("A user with this email already exists.");

        var user = new User(request.Email, _passwordHasher.Hash(request.Password));
        var response = IssueTokens(user);

        await _userRepository.AddAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByEmailAsync(request.Email, cancellationToken)
            ?? throw new DomainException("Invalid email or password.");

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
            throw new DomainException("Invalid email or password.");

        var response = IssueTokens(user);
        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }

    public async Task<AuthResponse> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByRefreshTokenAsync(request.RefreshToken, cancellationToken)
            ?? throw new DomainException("Invalid refresh token.");

        var newRefreshToken = _tokenGenerator.GenerateRefreshToken();
        var utcNow = _dateTimeProvider.UtcNow;

        user.RotateRefreshToken(request.RefreshToken, newRefreshToken, utcNow.Add(RefreshTokenLifetime), utcNow);
        await _userRepository.UpdateAsync(user, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var accessToken = _tokenGenerator.GenerateAccessToken(user);
        return new AuthResponse(accessToken, newRefreshToken, utcNow.AddMinutes(15));
    }

    private AuthResponse IssueTokens(User user)
    {
        var utcNow = _dateTimeProvider.UtcNow;
        var accessToken = _tokenGenerator.GenerateAccessToken(user);
        var refreshToken = _tokenGenerator.GenerateRefreshToken();

        user.IssueRefreshToken(refreshToken, utcNow.Add(RefreshTokenLifetime));

        return new AuthResponse(accessToken, refreshToken, utcNow.AddMinutes(15));
    }
}
