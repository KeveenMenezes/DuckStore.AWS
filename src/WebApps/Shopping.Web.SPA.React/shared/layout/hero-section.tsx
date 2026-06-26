import Image from "next/image"
import Link from "next/link"
import { ArrowRight, Code2, ShoppingBag } from "lucide-react"
import { Button } from "@/components/ui/button"
import { ROUTES } from "@/shared/constants/routes"

export function HeroSection() {
  return (
    <section className="relative overflow-hidden border-b border-border">
      <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_top,_var(--tw-gradient-stops))] from-primary/10 via-transparent to-transparent" />
      <div className="relative mx-auto flex max-w-7xl flex-col items-center gap-8 px-4 py-16 lg:flex-row lg:gap-16 lg:px-8 lg:py-24">
        <div className="flex flex-1 flex-col items-center text-center lg:items-start lg:text-left">
          <div className="mb-4 inline-flex items-center gap-2 rounded-full border border-border bg-secondary px-4 py-1.5">
            <span className="h-2 w-2 rounded-full bg-accent" />
            <span className="text-xs font-medium text-muted-foreground">Rubber Duck Debugging</span>
          </div>
          <h1 className="text-balance text-4xl font-bold tracking-tight text-foreground md:text-5xl lg:text-6xl">
            Your favorite{" "}
            <span className="text-primary">debugging</span>{" "}
            partner
          </h1>
          <p className="mt-4 max-w-xl text-pretty text-lg text-muted-foreground">
            Custom rubber ducks for devs and interactive code
            challenges. Learn, practice and have fun with CodeDuck.
          </p>
          <div className="mt-8 flex flex-wrap items-center gap-3">
            <Link href="#catalog">
              <Button size="lg" className="gap-2">
                <ShoppingBag className="h-4 w-4" />
                View Catalog
              </Button>
            </Link>
            <Link href={ROUTES.challenges}>
              <Button size="lg" variant="outline" className="gap-2">
                <Code2 className="h-4 w-4" />
                Challenges
                <ArrowRight className="h-4 w-4" />
              </Button>
            </Link>
          </div>
          <div className="mt-8 flex items-center gap-6 text-sm text-muted-foreground">
            <div className="flex items-center gap-2">
              <span className="font-semibold text-foreground">8+</span>
              <span>Unique ducks</span>
            </div>
            <div className="h-4 w-px bg-border" />
            <div className="flex items-center gap-2">
              <span className="font-semibold text-foreground">9+</span>
              <span>Challenges</span>
            </div>
            <div className="h-4 w-px bg-border" />
            <div className="flex items-center gap-2">
              <span className="font-semibold text-foreground">7</span>
              <span>Languages</span>
            </div>
          </div>
        </div>
        <div className="relative flex-1">
          <div className="relative mx-auto aspect-square max-w-md overflow-hidden rounded-2xl border border-border bg-card">
            <Image
              src="/images/duck-hero.jpg"
              alt="Debug Duck - Rubber duck for debugging"
              fill
              className="object-cover"
              priority
              loading="eager"
            />
            <div className="absolute inset-0 bg-gradient-to-t from-background/60 to-transparent" />
            <div className="absolute bottom-4 left-4 right-4 rounded-lg border border-border bg-card/90 p-3 backdrop-blur-sm">
              <code className="text-xs text-muted-foreground">
                <span className="text-accent">const</span>{" "}
                <span className="text-primary">duck</span> = <span className="text-accent">new</span>{" "}
                <span className="text-foreground">DebugDuck</span>();
              </code>
              <br />
              <code className="text-xs text-muted-foreground">
                duck.<span className="text-primary">listen</span>(myCode);{" "}
                <span className="text-muted-foreground/60">{"// bug found!"}</span>
              </code>
            </div>
          </div>
        </div>
      </div>
    </section>
  )
}
