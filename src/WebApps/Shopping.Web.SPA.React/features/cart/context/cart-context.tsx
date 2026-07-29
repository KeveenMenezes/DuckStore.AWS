"use client"

import {
  createContext,
  useContext,
  useState,
  useCallback,
  useMemo,
  useEffect,
  useRef,
  type ReactNode,
} from "react"
import type { Product } from "@/features/products/types/product.types"
import type { CartItem, CartProduct } from "@/features/cart/types/cart.types"
import { getBasket, syncCartToBasket } from "@/features/cart/services/basket.service"
import { mainImageId } from "@/shared/lib/image-url"

interface CartContextType {
  items: CartItem[]
  addItem: (product: Product) => boolean
  removeItem: (productId: string) => void
  updateQuantity: (productId: string, quantity: number) => boolean
  clearCart: () => void
  totalItems: number
  totalPrice: number
  isOpen: boolean
  setIsOpen: (open: boolean) => void
  isLoading: boolean
  flushCart: () => Promise<void>
}

const CartContext = createContext<CartContextType | null>(null)

export function CartProvider({ children }: { children: ReactNode }) {
  const [items, setItems] = useState<CartItem[]>([])
  const [isOpen, setIsOpen] = useState(false)
  const [isLoading, setIsLoading] = useState(true)
  // Only a real user mutation may trigger a write. Gating on this (instead of suppressing the
  // one sync that follows hydration) is what keeps a visitor who never touches the cart from
  // writing an empty basket to DynamoDB on every single page load.
  const dirtyRef = useRef(false)
  // Always-current snapshot of items — lets addItem read state without being in its dep array.
  const itemsRef = useRef(items)
  useEffect(() => { itemsRef.current = items }, [items])
  // Pending debounced sync timer — flushCart cancels it and syncs immediately instead.
  const pendingSyncRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    let cancelled = false
    async function hydrate() {
      try {
        const enriched = await getBasket()
        if (cancelled) return
        // A mutation that landed while this fetch was in flight is newer than what it returned,
        // so local intent wins — overwriting it here would silently drop the user's item.
        if (enriched.length > 0 && !dirtyRef.current) {
          setItems(enriched)
        }
      } catch (error) {
        // Cart stays at its initial [] on failure — but log it, since a swallowed
        // error here is indistinguishable in the UI from a genuinely empty cart.
        if (!cancelled) console.error('Failed to hydrate cart from basket', error)
      } finally {
        if (!cancelled) setIsLoading(false)
      }
    }
    hydrate()
    return () => {
      cancelled = true
    }
  }, [])

  useEffect(() => {
    if (isLoading) return
    if (!dirtyRef.current) return
    const timer = setTimeout(() => {
      // ownerId is injected by the BFF; guests and users both persist through the same path.
      // Tolerates failure (e.g. local-simulated users) same as the hydrate effect above, but
      // logs it — a swallowed failure here means the cart silently never saves.
      pendingSyncRef.current = null
      syncCartToBasket(items).catch((error) => console.error('Failed to sync cart to basket', error))
    }, 300)
    pendingSyncRef.current = timer
    return () => clearTimeout(timer)
  }, [items, isLoading])

  // Cancels any pending debounced sync and persists immediately — used before a hard
  // navigation (e.g. redirecting to login) that would otherwise abandon the debounce timer.
  const flushCart = useCallback(async (): Promise<void> => {
    if (pendingSyncRef.current) {
      clearTimeout(pendingSyncRef.current)
      pendingSyncRef.current = null
    }
    // Nothing was mutated, so there is no pending write to rescue before the navigation.
    if (!dirtyRef.current) return
    await syncCartToBasket(itemsRef.current).catch((error) => console.error('Failed to flush cart to basket', error))
  }, [])

  const addItem = useCallback((product: Product): boolean => {
    const existing = itemsRef.current.find((item) => item.product.id === product.id)

    if (existing) {
      if (existing.quantity >= product.stock) return false
      dirtyRef.current = true
      setItems((prev) =>
        prev.map((item) =>
          item.product.id === product.id
            ? { ...item, quantity: item.quantity + 1 }
            : item,
        ),
      )
      return true
    }

    if (product.stock <= 0) return false

    dirtyRef.current = true
    setItems((prev) => [
      ...prev,
      {
        product: { id: product.id, name: product.name, price: product.price, imageId: mainImageId(product.images), stock: product.stock },
        quantity: 1,
      },
    ])
    return true
  }, [])

  const removeItem = useCallback((productId: string) => {
    dirtyRef.current = true
    setItems((prev) => prev.filter((item) => item.product.id !== productId))
  }, [])

  const updateQuantity = useCallback(
    (productId: string, quantity: number): boolean => {
      if (quantity <= 0) {
        removeItem(productId)
        return true
      }
      let success = false
      setItems((prev) => {
        const item = prev.find((i) => i.product.id === productId)
        if (!item) return prev
        if (quantity > (item.product.stock ?? Infinity)) return prev
        success = true
        dirtyRef.current = true
        return prev.map((i) => (i.product.id === productId ? { ...i, quantity } : i))
      })
      return success
    },
    [removeItem],
  )

  const clearCart = useCallback(() => {
    // Emptying after checkout must reach DynamoDB, so this counts as a mutation.
    dirtyRef.current = true
    setItems([])
  }, [])

  const totalItems = useMemo(
    () => items.reduce((sum, item) => sum + item.quantity, 0),
    [items],
  )
  const totalPrice = useMemo(
    () => items.reduce((sum, item) => sum + item.product.price * item.quantity, 0),
    [items],
  )

  const value = useMemo(
    () => ({
      items,
      addItem,
      removeItem,
      updateQuantity,
      clearCart,
      totalItems,
      totalPrice,
      isOpen,
      setIsOpen,
      isLoading,
      flushCart,
    }),
    [items, addItem, removeItem, updateQuantity, clearCart, totalItems, totalPrice, isOpen, isLoading, flushCart],
  )

  return <CartContext.Provider value={value}>{children}</CartContext.Provider>
}

export function useCartContext() {
  const context = useContext(CartContext)
  if (!context) throw new Error("useCart must be used within CartProvider")
  return context
}
