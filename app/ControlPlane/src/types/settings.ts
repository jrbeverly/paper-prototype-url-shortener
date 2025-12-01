export interface NotificationPreferences {
  linkEvents: boolean
  domainEvents: boolean
  billingEvents: boolean
  weeklyDigest: boolean
}

export interface ActiveSession {
  id: string
  device: string
  browser: string
  location: string
  ipAddress: string
  lastActive: string
  isCurrent: boolean
}

export interface UpdateProfileParams {
  name: string
  email: string
}

export interface ChangePasswordParams {
  currentPassword: string
  newPassword: string
}
