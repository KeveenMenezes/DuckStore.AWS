import { Code2 } from "lucide-react"

export function Footer() {
  return (
    <footer className="border-t border-border bg-card">
      <div className="mx-auto flex max-w-7xl flex-col items-center gap-4 px-4 py-8 text-center lg:px-8">
        <div className="flex items-center gap-2">
          <Code2 className="h-5 w-5 text-primary" />
          <span className="font-semibold text-foreground">
            Code<span className="text-primary">Duck</span> Store
          </span>
        </div>
        <p className="max-w-md text-sm text-muted-foreground">
          Your favorite debugging companion. Custom rubber ducks
          for devs and interactive code challenges.
        </p>
        <p className="text-xs text-muted-foreground">
          CodeDuck Store - Rubber Duck Debugging com estilo.
        </p>
      </div>
    </footer>
  )
}
