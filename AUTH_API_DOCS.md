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
      "organizationId": "org-guid",
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
