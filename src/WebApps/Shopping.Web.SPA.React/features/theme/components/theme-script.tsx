import { STORAGE_KEYS } from "@/shared/constants/storage-keys"

// Mirrors the class toggling in theme-context.tsx. Kept as a hand-written string because it has to
// run standalone in the document, before any bundle is parsed.
const applyPersistedTheme = `(function(){try{var t=localStorage.getItem(${JSON.stringify(
  STORAGE_KEYS.theme,
)});var r=document.documentElement;if(t==="light"){r.classList.remove("dark");r.classList.add("light")}else{r.classList.add("dark");r.classList.remove("light")}}catch(e){}})()`

/**
 * Applies the persisted theme to `<html>` before the first paint.
 *
 * Every route is prerendered and cached at the CloudFront edge with `class="dark"` baked into the
 * markup, so the document itself can never carry a per-visitor theme. Without this script, someone
 * who chose light mode gets the whole page rendered dark until ThemeProvider's effect runs after
 * hydration — measured at ~1.3s on a throttled mobile connection, ending in a hard flash.
 *
 * Must stay a blocking inline script rendered ahead of any visible markup: deferring it, or moving
 * it into a component that hydrates, paints the wrong theme first and brings the flash back.
 */
export function ThemeScript() {
  return <script dangerouslySetInnerHTML={{ __html: applyPersistedTheme }} />
}
