import type { Metadata, Viewport } from 'next'
import { JetBrains_Mono, Inter } from 'next/font/google'
import { Providers } from '@/shared/layout/providers'
import { ThemeScript } from '@/features/theme/components/theme-script'
import { imageCdnUrl, isIndexableEnvironment, siteUrl } from '@/shared/lib/site'
import './globals.css'

const _inter = Inter({ subsets: ["latin"], variable: "--font-inter" })
const _jetBrainsMono = JetBrains_Mono({ subsets: ["latin"], variable: "--font-jetbrains" })

const title = 'CodeDuck Store - Your favorite debugging companion'
const description =
  'Custom rubber ducks for devs and interactive code challenges. Rubber Duck Debugging with style.'

export const metadata: Metadata = {
  // Required for the relative canonical/OG URLs below to resolve to absolute ones.
  metadataBase: new URL(siteUrl),
  title,
  description,
  alternates: { canonical: '/' },
  // Keeps non-production stages (which are served on public domains) out of search results.
  robots: isIndexableEnvironment
    ? undefined
    : { index: false, follow: false, nocache: true },
  openGraph: {
    type: 'website',
    siteName: 'CodeDuck Store',
    title,
    description,
    url: '/',
    images: [{ url: '/images/duck-hero.jpg', width: 1024, height: 1024, alt: 'CodeDuck Store' }],
  },
  twitter: {
    // `summary`, not `summary_large_image`: the only art available is the square 1024x1024 hero,
    // and the large card is 2:1 — it would centre-crop the duck. Worth revisiting with a dedicated
    // 1200x630 asset, which is what the large card is actually for.
    card: 'summary',
    title,
    description,
    images: ['/images/duck-hero.jpg'],
  },
  icons: {
    icon: '/icon.svg',
  },
}

export const viewport: Viewport = {
  themeColor: '#1a1a2e',
  userScalable: true,
}

export default function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  return (
    <html lang="en" className="dark" suppressHydrationWarning>
      {/* Product images come from a separate CDN origin (ADR-0034), so the handshake to it would
          otherwise only start when the first card image is discovered. */}
      {imageCdnUrl && <link rel="preconnect" href={imageCdnUrl} crossOrigin="" />}
      <body className={`${_inter.variable} ${_jetBrainsMono.variable} font-sans antialiased`}>
        <ThemeScript />
        {/* Visible only once focused, so keyboard users can jump the sticky header on every route. */}
        <a
          href="#main-content"
          className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-[100] focus:rounded-md focus:bg-primary focus:px-4 focus:py-2 focus:text-sm focus:font-medium focus:text-primary-foreground focus:outline-none focus:ring-2 focus:ring-ring"
        >
          Skip to main content
        </a>
        <Providers>
          {children}
        </Providers>
      </body>
    </html>
  )
}
