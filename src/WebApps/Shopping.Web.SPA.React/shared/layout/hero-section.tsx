import Image from "next/image"
import Link from "next/link"
// Static import, not a "/images/..." string: it is what lets Next generate the blur placeholder
// below at build time. The URL it resolves to is the same either way.
import duckHero from "@/public/images/duck-hero.jpg"
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
            <Button asChild size="lg" className="gap-2">
              <Link href="#catalog">
                <ShoppingBag className="h-4 w-4" />
                View Catalog
              </Link>
            </Button>
            {/* Same destination as the header nav's Challenges link, already
                prefetched by it whenever both are on screen — prefetch={false}
                avoids firing a second, redundant prefetch on every home load. */}
            <Button asChild size="lg" variant="outline" className="gap-2">
              <Link href={ROUTES.challenges} prefetch={false}>
                <Code2 className="h-4 w-4" />
                Challenges
                <ArrowRight className="h-4 w-4" />
              </Link>
            </Button>
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
              src={duckHero}
              alt="Debug Duck - Rubber duck for debugging"
              fill
              sizes="(max-width: 1024px) 100vw, 448px"
              quality={75}
              className="object-cover"
              priority
              // `priority` alone only makes this eager + preloaded; it leaves the request at the
              // browser's default Low priority for images, which is what the LCP element was
              // measured at. fetchPriority is what actually promotes it on the wire.
              fetchPriority="high"
              // Unlike the catalog cards, this one is eager and sits on a solid `bg-card` square,
              // so without a placeholder you watch an empty dark box become a duck. The inline
              // base64 LQIP means the box is never empty. (The cards are lazy and transparent —
              // they land after the page settles, where the swap doesn't register.)
              placeholder="blur"
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
