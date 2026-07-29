"use client"

import Link from "next/link"
import { ShoppingBag, ArrowLeft } from "lucide-react"
import { Button } from "@/components/ui/button"
import { useCart } from "@/features/cart/hooks/use-cart"
import { CartItem } from "@/features/cart/components/cart-item"
import { CartSummary } from "@/features/cart/components/cart-summary"
import { useAuth } from "@/features/auth/hooks/use-auth"
import { ROUTES } from "@/shared/constants/routes"

export default function CartPage() {
  const { items, removeItem, updateQuantity, totalItems, totalPrice, flushCart } = useCart()
  const { user, isLoading, loginWithCognito } = useAuth()

  const handleCheckoutClick = async (e: React.MouseEvent<HTMLAnchorElement>) => {
    if (isLoading) {
      e.preventDefault()
      return
    }
    if (!user) {
      e.preventDefault()
      await flushCart()
      loginWithCognito(ROUTES.checkout)
    }
  }

  return (
    <div className="mx-auto max-w-7xl px-4 py-8 lg:px-8">
      <div className="mb-6 flex items-center gap-3">
        <Button asChild variant="ghost" size="icon" aria-label="Back to store">
          <Link href={ROUTES.home}>
            <ArrowLeft className="h-5 w-5" />
          </Link>
        </Button>
        <h1 className="text-2xl font-bold text-foreground">
          Your Cart{totalItems > 0 && <span className="ml-2 text-muted-foreground text-lg font-normal">({totalItems} items)</span>}
        </h1>
      </div>

      {items.length === 0 ? (
        <div className="flex flex-col items-center justify-center gap-4 py-24">
          <div className="flex h-20 w-20 items-center justify-center rounded-full bg-secondary">
            <ShoppingBag className="h-10 w-10 text-muted-foreground" />
          </div>
          <p className="text-center text-lg text-muted-foreground">
            Your cart is empty. How about adding a duck?
          </p>
          <Button asChild>
            <Link href={ROUTES.home}>Continue Shopping</Link>
          </Button>
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-8 lg:grid-cols-3">
          <div className="lg:col-span-2">
            <div className="flex flex-col gap-4">
              {items.map((item) => (
                <CartItem
                  key={item.product.id}
                  item={item}
                  onUpdateQuantity={updateQuantity}
                  onRemove={removeItem}
                />
              ))}
            </div>
            <div className="mt-6">
              <Button asChild variant="outline" className="gap-2">
                <Link href={ROUTES.home}>
                  <ArrowLeft className="h-4 w-4" />
                  Continue Shopping
                </Link>
              </Button>
            </div>
          </div>

          <div className="lg:col-span-1">
            <div className="rounded-lg border border-border bg-background">
              <CartSummary totalPrice={totalPrice} isAuthLoading={isLoading} onCheckout={handleCheckoutClick} />
            </div>
          </div>
        </div>
      )}
    </div>
  )
}
