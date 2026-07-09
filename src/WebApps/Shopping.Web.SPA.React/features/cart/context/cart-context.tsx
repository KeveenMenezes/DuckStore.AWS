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
import { gql } from "@/api"
import { GET_BASKET } from "@/api/queries/order"
import type { Product } from "@/features/products/types/product.types"
import type { CartItem, CartProduct } from "@/features/cart/types/cart.types"
import type { GqlShoppingCart } from "@/graphql/types"
import { syncCartToBasket } from "@/features/cart/services/basket.service"

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
  // Prevents syncing back to DB the items that were just loaded from DB
  const skipNextSyncRef = useRef(false)
  // Always-current snapshot of items — lets addItem read state without being in its dep array.
  const itemsRef = useRef(items)
  useEffect(() => { itemsRef.current = items }, [items])
  // Pending debounced sync timer — flushCart cancels it and syncs immediately instead.
  const pendingSyncRef = useRef<ReturnType<typeof setTimeout> | null>(null)

  useEffect(() => {
    let cancelled = false
    async function hydrate() {
      try {
        // No ownerId passed — the /api/graphql BFF injects it from the httpOnly identity cookies.
        const data = await gql<{ basket: GqlShoppingCart | null }>(GET_BASKET)
        if (cancelled) return
        const enriched: CartItem[] = (data.basket?.items ?? []).map((item) => ({
          product: {
            id: item.productId,
            name: item.productName,
            price: item.price,
            imageUrl: item.imageUrl ?? '',
          },
          quantity: item.quantity,
        }))
        if (enriched.length > 0) {
          skipNextSyncRef.current = true
          setItems(enriched)
        }
      } catch {
        // carrinho começa vazio em caso de erro
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
    if (skipNextSyncRef.current) {
      skipNextSyncRef.current = false
      return
    }
    const timer = setTimeout(() => {
      // ownerId is injected by the BFF; guests and users both persist through the same path.
      // Fails silently (e.g. local-simulated users) — same tolerance as the hydrate effect above.
      pendingSyncRef.current = null
      syncCartToBasket(items).catch(() => {})
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
    await syncCartToBasket(itemsRef.current).catch(() => {})
  }, [])

  const addItem = useCallback((product: Product): boolean => {
    const existing = itemsRef.current.find((item) => item.product.id === product.id)

    if (existing) {
      if (existing.quantity >= product.stock) return false
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

    setItems((prev) => [
      ...prev,
      {
        product: { id: product.id, name: product.name, price: product.price, imageUrl: product.imageUrl, stock: product.stock },
        quantity: 1,
      },
    ])
    return true
  }, [])

  const removeItem = useCallback((productId: string) => {
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
        return prev.map((i) => (i.product.id === productId ? { ...i, quantity } : i))
      })
      return success
    },
    [removeItem],
  )

  const clearCart = useCallback(() => {
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
