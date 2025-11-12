# Authentication API Documentation

## New Authentication Endpoints

### 1. Refresh Token
**Endpoint:** `POST /api/authentication/refresh-token`
**Description:** Get a new access token using a valid refresh token

**Request:**
```json
{
  "refreshToken": "your-refresh-token-here"
}
```

**Response (Success):**
```json
{
  "message": {
    "token": "new-jwt-token",
    "refreshToken": "new-refresh-token",
    "expiresAt": "2025-10-18T12:00:00Z",
    "user": {
      "id": "user-guid",
      "firstName": "John",
      "lastName": "Doe",
      "email": "john@example.com",
      "role": "Admin",
      "TenantId": "org-guid",
      "organizationName": "Acme Corp",
      "isActive": true,
      "createdAt": "2025-01-01T00:00:00Z",
      "lastLoginAt": "2025-10-18T11:00:00Z"
    },
    "organization": {
      "id": "org-guid",
      "name": "Acme Corp",
      "subscriptionPlan": "premium",
      "isActive": true,
      "createdAt": "2025-01-01T00:00:00Z"
    }
  }
}
```

**Curl Example:**
```bash
curl -X POST "https://api.runnatics.com/api/authentication/refresh-token" \
  -H "Content-Type: application/json" \
  -d '{
    "refreshToken": "your-refresh-token-here"
  }'
```

### 2. Logout
**Endpoint:** `POST /api/authentication/logout`
**Description:** Invalidate a refresh token and log out the user
**Authorization:** Bearer token required

**Request:**
```json
{
  "refreshToken": "your-refresh-token-here"
}
```

**Response (Success):**
```json
{
  "message": "Logout successful."
}
```

**Curl Example:**
```bash
curl -X POST "https://api.runnatics.com/api/authentication/logout" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer your-jwt-token" \
  -d '{
    "refreshToken": "your-refresh-token-here"
  }'
```

### 3. Validate Refresh Token
**Endpoint:** `POST /api/authentication/validate-token`
**Description:** Check if a refresh token is valid and not expired

**Request:**
```json
{
  "refreshToken": "your-refresh-token-here"
}
```

**Response:**
```json
{
  "isValid": true
}
```

**Curl Example:**
```bash
curl -X POST "https://api.runnatics.com/api/authentication/validate-token" \
  -H "Content-Type: application/json" \
  -d '{
    "refreshToken": "your-refresh-token-here"
  }'
```

### 4. Change Password
**Endpoint:** `POST /api/authentication/change-password`
**Description:** Change the current user's password
**Authorization:** Bearer token required

**Request:**
```json
{
  "currentPassword": "current-password",
  "newPassword": "new-secure-password",
  "confirmPassword": "new-secure-password"
}
```

**Response (Success):**
```json
{
  "message": "Password changed successfully."
}
```

**Response (Error Examples):**
```json
{
  "error": "Current password is incorrect."
}
```

```json
{
  "error": "New password must contain at least one uppercase letter, one lowercase letter, one number, and one special character (@$!%*?&)."
}
```

```json
{
  "error": "New password must be different from the current password."
}
```

**Curl Example:**
```bash
curl -X POST "https://api.runnatics.com/api/authentication/change-password" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer your-jwt-token" \
  -d '{
    "currentPassword": "current-password",
    "newPassword": "new-secure-password",
    "confirmPassword": "new-secure-password"
  }'
```

**Validation Rules:**
- Current password must be provided and correct
- New password must be at least 8 characters long
- New password must contain at least one uppercase letter (A-Z)
- New password must contain at least one lowercase letter (a-z)
- New password must contain at least one number (0-9)
- New password must contain at least one special character (@$!%*?&)
- New password must be different from current password
- New password and confirmation must match
- User must be authenticated and active

**Password Validation Examples:**

✅ **Valid Passwords:**
- `MyPassword123!` - Contains all required character types
- `SecurePass$456` - Meets complexity requirements
- `Admin@789Test` - Complex password with special characters

❌ **Invalid Passwords:**
- `password123` - Missing uppercase letter and special character
- `PASSWORD123!` - Missing lowercase letter
- `MyPassword!` - Missing number
- `MyPassword123` - Missing special character
- `Short1!` - Too short (less than 8 characters)
- `ValidPass123#` - Contains invalid special character (#)

### 5. Forgot Password
**Endpoint:** `POST /api/authentication/forgot-password`
**Description:** Request a password reset link for a user account
**Authorization:** None required (public endpoint)

**Request:**
```json
{
  "email": "user@example.com"
}
```

**Response (Always Success for Security):**
```json
{
  "message": "If the email address exists in our system, you will receive a password reset link shortly."
}
```

**Curl Example:**
```bash
curl -X POST "https://api.runnatics.com/api/authentication/forgot-password" \
  -H "Content-Type: application/json" \
  -d '{
    "email": "user@example.com"
  }'
```

### 6. Reset Password
**Endpoint:** `POST /api/authentication/reset-password`
**Description:** Reset password using a valid reset token
**Authorization:** None required (public endpoint)

**Request:**
```json
{
  "resetToken": "your-reset-token-here",
  "newPassword": "NewSecurePassword123!",
  "confirmNewPassword": "NewSecurePassword123!"
}
```

**Response (Success):**
```json
{
  "message": "Password has been reset successfully. Please log in with your new password."
}
```

**Response (Error):**
```json
{
  "error": "Invalid or expired reset token."
}
```

**Curl Example:**
```bash
curl -X POST "https://api.runnatics.com/api/authentication/reset-password" \
  -H "Content-Type: application/json" \
  -d '{
    "resetToken": "your-reset-token-here",
    "newPassword": "NewSecurePassword123!",
    "confirmNewPassword": "NewSecurePassword123!"
  }'
```

**Password Reset Security Features:**
- Reset tokens are cryptographically secure and hashed before storage
- Tokens expire after 1 hour for security
- Tokens can only be used once
- All user sessions are invalidated after successful password reset
- Email validation is done without revealing if account exists
- Strong password requirements enforced

## Token Management Best Practices

### Client-Side Implementation
1. **Store tokens securely**: Use secure storage (Keychain, encrypted storage)
2. **Automatic token refresh**: Implement interceptors to refresh tokens before they expire
3. **Handle refresh token expiration**: Redirect to login when refresh token is invalid
4. **Logout properly**: Always call the logout endpoint to invalidate tokens

### Example JavaScript Implementation
```javascript
class AuthService {
  async refreshToken() {
    try {
      const refreshToken = localStorage.getItem('refreshToken');
      const response = await fetch('/api/authentication/refresh-token', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken })
      });
      
      if (response.ok) {
        const data = await response.json();
        localStorage.setItem('accessToken', data.message.token);
        localStorage.setItem('refreshToken', data.message.refreshToken);
        return data.message.token;
      } else {
        // Refresh token is invalid, redirect to login
        this.logout();
        window.location.href = '/login';
      }
    } catch (error) {
      console.error('Token refresh failed:', error);
      this.logout();
    }
  }

  async logout() {
    try {
      const refreshToken = localStorage.getItem('refreshToken');
      if (refreshToken) {
        await fetch('/api/authentication/logout', {
          method: 'POST',
          headers: { 
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${localStorage.getItem('accessToken')}`
          },
          body: JSON.stringify({ refreshToken })
        });
      }
    } finally {
      localStorage.removeItem('accessToken');
      localStorage.removeItem('refreshToken');
    }
  }
}
```

## Security Features

1. **Refresh Token Hashing**: Refresh tokens are hashed using BCrypt before storage
2. **Token Expiration**: JWT tokens expire based on configuration (default: 1 hour)
3. **Session Management**: User sessions are tracked and can be invalidated
4. **Automatic Cleanup**: Expired tokens can be cleaned up automatically
5. **Single Session Validation**: Each refresh token is unique per session

## Error Responses

### Invalid/Expired Refresh Token
```json
{
  "error": "Invalid or expired refresh token."
}
```

### Missing Authorization
```json
{
  "error": "Unauthorized"
}
```

### Validation Errors
```json
{
  "error": "Refresh token is required"
}
```
