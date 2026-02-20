# Product Guidelines: Bazaar Companion

**Core Design Philosophy:**
"Efficiency over embellishment."
The dashboard should present complex market data as clearly as possible, prioritizing information density and rapid decision-making.

**UX Principles:**
- **Instant Response:** Market data must update in real-time via SignalR without manual refreshes.
- **Progressive Disclosure:** Start with high-level metrics (e.g., Value Cards, Market Dashboard) and allow users to drill down into technical details (e.g., Order Book Depth, Correlation Matrices).
- **Consistent Visual Language:** Standardize color schemes for market direction (Green for Uptrend/Profit, Red for Downtrend/Loss) and interaction patterns (e.g., consistent modal behavior for product details).
- **Accessibility:** Use semantic HTML and ensure contrast ratios are sufficient for data visualization elements like charts and heatmaps.

**Prose & Tone:**
- **Professional & Precise:** Avoid flowery language. Use standard financial and market terminology (e.g., "OHLC", "Imbalance", "Spread", "Volume").
- **Clear Instructions:** Error messages should be helpful and direct, explaining why a data fetch or calculation failed.
- **Contextual Help:** Provide concise tooltips or "About" information for complex technical metrics.

**Interaction Patterns:**
- **Drill-down Modals:** Use consistent modal layouts for secondary analysis to keep the user in context.
- **Interactive Charts:** All price and volume charts should support standard zooming and panning.
- **Persistent State:** Preserve user preferences (e.g., chart intervals, active filters) across sessions using browser storage.

**Branding:**
- **Theme:** High-density, modern financial aesthetic.
- **Typography:** Roboto Flex for a professional, scalable look.
- **Icons:** Material Symbols for a clean, recognizable UI.
