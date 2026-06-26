import { storage } from "@/shared/lib/storage"
import { STORAGE_KEYS } from "@/shared/constants/storage-keys"
import { createUserId } from "@/shared/lib/id"
import type { AuthResult, StoredUser, User } from "@/features/auth/types/auth.types"

const SIMULATED_LATENCY_MS = 800

function delay(ms: number) {
  return new Promise((resolve) => setTimeout(resolve, ms))
}

function toSessionUser({ id, name, email }: StoredUser): User {
  return { id, name, email }
}

function readUsers(): StoredUser[] {
  return storage.get<StoredUser[]>(STORAGE_KEYS.users, [])
}

function writeUsers(users: StoredUser[]) {
  storage.set(STORAGE_KEYS.users, users)
}

export const authService = {
  getSession(): User | null {
    return storage.get<User | null>(STORAGE_KEYS.session, null)
  },

  clearSession() {
    storage.remove(STORAGE_KEYS.session)
  },

  async login(email: string, password: string): Promise<AuthResult & { user?: User }> {
    await delay(SIMULATED_LATENCY_MS)

    const found = readUsers().find((u) => u.email === email && u.password === password)
    if (!found) {
      return { success: false, error: "Incorrect email or password." }
    }

    const sessionUser = toSessionUser(found)
    storage.set(STORAGE_KEYS.session, sessionUser)
    return { success: true, user: sessionUser }
  },

  async register(name: string, email: string, password: string): Promise<AuthResult & { user?: User }> {
    await delay(SIMULATED_LATENCY_MS)

    const users = readUsers()
    if (users.some((u) => u.email === email)) {
      return { success: false, error: "This email is already registered." }
    }

    const newUser: StoredUser = { id: createUserId(), name, email, password }
    writeUsers([...users, newUser])

    const sessionUser = toSessionUser(newUser)
    storage.set(STORAGE_KEYS.session, sessionUser)
    return { success: true, user: sessionUser }
  },
}
