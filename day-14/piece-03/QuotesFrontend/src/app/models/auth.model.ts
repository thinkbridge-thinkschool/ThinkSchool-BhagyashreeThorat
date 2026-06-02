// Mirrors the REAL Week-1 auth contract exactly — no invented fields.

// POST /api/auth/login  body
export interface LoginRequest {
  email: string;
  password: string;
}

// POST /api/auth/login  response
export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresIn: number;
}
