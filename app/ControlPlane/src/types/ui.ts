export interface Notification {
  id: string
  message: string
  type: 'info' | 'success' | 'warning' | 'error'
}
