/**
 * Client-side validation for the auth forms. Returns an error message string,
 * or `null` when the input is valid.
 */

const MIN_PASSWORD_LENGTH = 4
const MIN_NAME_LENGTH = 2

function isValidEmail(email: string): boolean {
  return email.trim().length > 0 && email.includes("@")
}

export interface LoginFormValues {
  email: string
  password: string
}

export interface RegisterFormValues {
  name: string
  email: string
  password: string
  confirmPassword: string
}

export function validateLogin({ email, password }: LoginFormValues): string | null {
  if (!isValidEmail(email)) return "Enter a valid email."
  if (!password.trim() || password.length < MIN_PASSWORD_LENGTH) {
    return `Password must be at least ${MIN_PASSWORD_LENGTH} characters.`
  }
  return null
}

export function validateRegister({ name, email, password, confirmPassword }: RegisterFormValues): string | null {
  if (!name.trim() || name.trim().length < MIN_NAME_LENGTH) {
    return `Name must be at least ${MIN_NAME_LENGTH} characters.`
  }
  if (!isValidEmail(email)) return "Enter a valid email."
  if (!password.trim() || password.length < MIN_PASSWORD_LENGTH) {
    return `Password must be at least ${MIN_PASSWORD_LENGTH} characters.`
  }
  if (password !== confirmPassword) return "Passwords do not match."
  return null
}
