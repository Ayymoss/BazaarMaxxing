# Tech Stack: Bazaar Companion

**Core Language & Runtime:**
- **Language:** C# 14
- **Runtime:** .NET 10.0

**Web Application Framework:**
- **Frontend & Backend:** ASP.NET Core Blazor (Interactive Server Render Mode)
- **Real-time Communication:** Microsoft SignalR

**Data Persistence:**
- **Database:** PostgreSQL
- **ORM:** Entity Framework Core (using Npgsql.EntityFrameworkCore.PostgreSQL)

**External API Integration:**
- **Client Library:** Refit (Type-safe REST client)
- **Primary API:** Hypixel API

**User Interface & Styling:**
- **CSS Framework:** Tailwind CSS
- **Icons:** Phosphor Icons
- **Typography:** Google Fonts (e.g., Roboto Flex)
- **UI Components:** Microsoft.AspNetCore.Components.QuickGrid

**Logging & Observability:**
- **Provider:** Serilog (with Console and File sinks)

**Deployment:**
- **Containerization:** Docker (Linux target)
- **Orchestration:** Docker Compose
