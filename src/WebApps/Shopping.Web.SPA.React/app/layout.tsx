import type { Metadata, Viewport } from 'next'
import { JetBrains_Mono, Inter } from 'next/font/google'
import { Providers } from '@/shared/layout/providers'
import './globals.css'

const _inter = Inter({ subsets: ["latin"], variable: "--font-inter" })
const _jetBrainsMono = JetBrains_Mono({ subsets: ["latin"], variable: "--font-jetbrains" })

export const metadata: Metadata = {
  title: 'CodeDuck Store - Your favorite debugging companion',
  description: 'Custom rubber ducks for devs and interactive code challenges. Rubber Duck Debugging with style.',
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
      <body className={`${_inter.variable} ${_jetBrainsMono.variable} font-sans antialiased`}>
        <Providers>
          {children}
        </Providers>
      </body>
    </html>
  )
}
