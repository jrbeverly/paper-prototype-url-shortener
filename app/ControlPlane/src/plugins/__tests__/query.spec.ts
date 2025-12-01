import { describe, it, expect } from 'vitest'
import { queryClient } from '../query'

describe('queryClient', () => {
  const queries = queryClient.getDefaultOptions().queries

  it('uses a 5-minute stale time', () => {
    expect(queries?.staleTime).toBe(5 * 60 * 1000)
  })

  it('uses a 10-minute gc time', () => {
    expect(queries?.gcTime).toBe(10 * 60 * 1000)
  })

  it('disables automatic retry', () => {
    expect(queries?.retry).toBe(false)
  })

  it('refetches on window focus', () => {
    expect(queries?.refetchOnWindowFocus).toBe(true)
  })
})
