# ARSpace: Augmented Reality Workplace Layout Platform

ARSpace is an enterprise-grade Android AR application built with Unity 6, AR Foundation 6.5, and Universal Render Pipeline (URP). It enables commercial real estate (CRE) brokers, architects, and enterprise clients to visualize, plan, and analyze office layouts inside bare-shell commercial spaces in real time.

---

## Technical Stack & Architecture

- **Engine & Graphics:** Unity 6 (6000.5.6f1), Universal Render Pipeline (URP 17.5.0), OpenGLES3 graphics API (chosen for deterministic mid-range Android ARCore stability).
- **XR Framework:** AR Foundation 6.5.0, Google ARCore XR Plugin, XR Core Utils.
- **Input System:** Unity Input System (EnhancedTouch single-consumer gesture disambiguation).
- **Architecture Pattern:** Decoupled service layer (`ServiceLocator`), event-driven communication (`GameEvents`), state machine (`AppState`), and domain-specific services.

---

## Current Implemented Features

1. **AR Session & Quality Monitoring (Phase 1):**
   - Resilient AR session lifecycle management with camera permission handshakes.
   - Floor plane filtering (eliminating vertical walls, tables, and ceilings; requiring min 1.0 m² area).
   - Real-time 6DoF tracking quality monitor with user guidance and tracking loss dimming.

2. **Floor Grid Visualisation (Phase 2):**
   - High-performance custom URP unlit transparent grid shader (`PlaneGrid.shader`) with derivative-based anti-aliasing (`fwidth`).
   - Radial distance fade and boundary vertex feathering.
   - Dynamic scanning radar sweep pulse during room onboarding that stops cleanly upon first placement.

3. **Commercial Catalogue Data Layer (Phase 3):**
   - 8 workplace functional categories (`Workstations`, `ExecutiveCabins`, `ConferenceTables`, `Cafeteria`, `Reception`, `Partitions`, `Decor`, `Equipment`).
   - Measured physical bounds, seating capacity, clearance margins, and wall-alignment constraints.
   - Fast $O(1)$ database lookup with zero validation errors.

4. **Placement Pipeline & Anchor Clustering (Phase 4):**
   - Real-time screen-center and drag placement reticle with floor boundary and collision clearance checks.
   - Primary UI Place button and reticle tap placement.
   - **Anchor Clustering:** Groups furniture within 1.5 m under shared native AR anchors (`ARAnchorManager.AttachAnchor` / `TryAddAnchorAsync`) with a hard cap of 20 anchors to prevent ARCore tracking degradation.

5. **Selection & Object Manipulation (Phase 5):**
   - Touch gesture routing: tap selection, single-finger floor drag with plane constraints, 2-finger yaw rotation with 5° magnetic snapping, and pinch scale.
   - Clearance conflict visualizer: Orange warning footprint and live collision tinting.
   - Contextual toolbar: Duplicate, Delete, 90° Rotate, Reset Scale, Lock/Unlock, and CRE metadata info.

6. **Layout Persistence & Preset Templates (Phase 6):**
   - JSON serialization of complete workplace layouts with seating and area analytics.
   - Atomic disk writes (`.tmp` -> replace) to `Application.persistentDataPath/Layouts/`.
   - Built-in office presets: *Open Plan Team Pod (24 Seats)*, *Hybrid Team Zone (16 Seats)*, *Executive Floor & Boardroom (22 Seats)*.
   - Tolerant deserialization: cleanly ignores missing catalogue IDs without crashing.

7. **Space Utilization Analysis & CRE Dashboard (Phase 7):**
   - `FloorAreaCalculator`: Accurately computes non-overlapping usable room square meterage using 2D spatial occupancy grid sampling over detected floor planes.
   - `SpaceAnalyticsService`: Live calculation of footprint area vs circulation egress ratio, seating density per 100 m², category footprint breakdown, and clearance conflict counts.
   - Compliance scoring: Real-time badges for egress compliance (Compliant $\ge 45\%$, Constrained $30-45\%$, Non-Compliant $<30\%$) and density health (Sparse $<8$, Optimal $8-14$, High-Density $14-20$, Overcrowded $>20$ seats / 100 m²).
   - `AnalyticsPanel`: Executive dashboard UI with metric ($m^2$) and imperial ($sq\ ft$) toggling.

8. **Visual Polish, Lighting & Presentation Mode (Phase 8):**
   - `OnboardingCoach`: Step-by-step contextual guidance (Floor sweep -> Placement -> Manipulation & Inspection).
   - `LightEstimationBinder`: Smoothly binds AR camera light estimation (brightness, color temperature, spherical harmonics) to scene directional light and ambient probes without flickering.
   - `ShadowReceiver.shader`: Transparent ground shadow receiver rendering soft contact shadows under 3D furniture to visually anchor them on real office floors.
   - `PresentationModeController`: Executive one-tap presentation mode hiding UI chrome and plane grids with double-tap restore.

---

## Layout Persistence & Origin Architecture

> [!IMPORTANT]
> **Why Origin-Relative Persistence?**
> Native ARCore anchors exist purely in the transient session coordinate frame and **do not persist across app lifecycles** without cloud anchors (which are explicitly out of scope for offline/local privacy).
>
> To solve this robustly:
> 1. ARSpace serializes all furniture poses relative to an established **Layout Origin** pose ($P_{local} = R_{origin}^{-1} \cdot (P_{world} - P_{origin})$).
> 2. The layout origin is established at the first placed object or explicitly anchored by the user.
> 3. Upon loading a saved layout or built-in template, the user aims at any detected floor surface to confirm the origin anchor, and all furniture pieces are reconstructed at their respective relative offsets and clustered into native AR anchors.

---

## Editor Tools & Builders

All project assets and catalogues are built idempotently via Editor menus under `ARSpace/`:
- `ARSpace → Validate Project`: Runs a full automated audit across 30+ build, XR, shader, and script settings.
- `ARSpace → Apply Android AR Build Settings`: One-click enforcer for IL2CPP, ARM64, OpenGLES3, and ARCore configuration.
- `ARSpace → Setup Plane Grid Assets`: Generates plane grid materials, shaders, and visualizer prefabs.
- `ARSpace → Rebuild Furniture Catalogue`: Scans 3D models, normalizes pivots to $Y=0$, builds prefabs with box colliders, renders 256x256 thumbnails, and updates `FurnitureDatabase.asset`.