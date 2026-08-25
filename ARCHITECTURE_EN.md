# SolitaireNET Architecture

## Overview

SolitaireNET is a cross-platform solution composed of three products that share
the same game domain:

```text
                         +----------------------+
                         | clients/web/         |
                         | Static web + games   |
                         +----------+-----------+
                                    |
                                    v HTTP/JSON
+-------------------+      +-------+----------------+
| .NET MAUI app     |----->| SolitaireNET.WebApi    |
| Android / Windows |      | sessions, ranking, auth|
+---------+---------+      +-------+----------------+
          |                         |
          v                         v
  clients/maui/Games/*         SQLite / PostgreSQL
  Engine + Pages
```

## Code organization

- `clients/maui/Games/<Game>`: MAUI presentation screens and engines; online
  game rules live exclusively in the backend.
- `server/api/SolitaireNET.WebApi`: API rules, state, and contracts for online
  games, ranking, presence, and usage metrics.
- `clients/maui/Pages`: application navigation and main screen composition.
- `clients/web`: static frontend built with HTML, CSS, and JavaScript.
- `scripts`: site and API publishing and operations scripts.

## Online game flow

1. `SolitairePage` loads or creates the game identifier stored in `Preferences`.
2. `SolitaireApiClient` or the JavaScript client translates the intent into HTTP/JSON.
3. The API keeps the authoritative state in `GameStore` and validates the action.
4. The returned state is applied to the remote engine and the screen is updated.
5. On network failure, the screen attempts to synchronize again before the next action.

## Architectural decisions

- UI code should not contain business rules.
- The API is authoritative for online games and ranking.
- The MAUI app and website send actions to and render state from the same API.
- Client-side checks are limited to interaction feedback and never replace
  backend validation.
- SQLite is the local fallback; PostgreSQL is the production store.
- The website and MAUI app have independent release cycles.
- New games should follow `Domain -> Engine -> Pages`.

## Next steps

1. Create `SolitaireNET.Domain`, `SolitaireNET.Application`, and
   `SolitaireNET.Infrastructure` projects.
2. Extract interfaces for repositories, ranking, and API clients.
3. Add unit tests for rules, win conditions, and serialization.
4. Validate every pull request with formatting, builds, and tests.
