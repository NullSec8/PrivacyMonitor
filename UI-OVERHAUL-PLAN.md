# UI Overhaul Plan

## Priority 0 — Critical Fixes
1. **Replace all Unicode icons** with WPF-UI `ui:SymbolIcon` (nav bar, sidebar, tabs, menus)
2. **Fix light theme colors** — high-risk warning colors are wrong (dark on light)
3. **Replace hardcoded `FontFamily="Segoe UI"`** with theme font system (100+ locations)

## Priority 1 — Control Modernization
4. **Replace custom `Card` style** with `ui:Card` (13+ locations)
5. **Replace `CNav` nav buttons** with `ui:Button Appearance="Transparent"` + icons
6. **Replace `PriBtn`/`SecBtn`** with `ui:Button Appearance="Primary"/"Secondary"`
7. **Use `ui:ToggleButton`** for Expert/Simple toggle

## Priority 2 — Spacing & Sizing
8. **Increase nav button size** from 32px to 40px (WCAG minimum)
9. **Standardize sidebar padding** (inconsistent across sections)
10. **Increase font sizes** — current 10-11px body text is too small

## Priority 3 — Typography
11. **Use theme styles** instead of inline `FontSize`/`FontWeight`
12. **Add `TextSectionHeader` style** for sidebar section headers
13. **Rationalize font sizes** to 6-tier system

## Priority 4 — Polish
14. **Add icons to analysis tabs** (Dashboard, Network, Storage, etc.)
15. **Use `ui:CardControl`** for per-site settings rows
16. **Improve bookmarks popup** button sizing
