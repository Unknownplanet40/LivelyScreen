# Knowledge Map Screensaver Recreation Specification

This specification provides the complete implementation details for the WinUI / Win2D screensaver project in `LivelyScreen.WinUI` to achieve a 1:1 visual, topological, and animated match with the Stash Knowledge Map (`KnowledgeBase-Quotes`).

---

## 1. Target Files & Architecture in `LivelyScreen.WinUI`

All changes apply to the following files in `src/LivelyScreen.WinUI`:
- **`Models/KnowledgeMapModels.cs`**: Node & Edge model definitions, shapes, hop distances, rendering flags.
- **`Services/KnowledgeMapService.cs`**: SQLite database querying, synthetic entity hub generation, and physical force relaxation layout.
- **`Controls/KnowledgeMapControl.cs`**: Win2D (`CanvasControl`) rendering pipeline, 3D celestial orbital camera, harmonic wave drift, cascading synaptic neural pulses, and collision-avoided typography.

---

## 2. Database & Entity Hub Generation (`KnowledgeMapService.cs`)

### 2.1 SQLite Source Database
- Default path: `C:\Users\ryanj\OneDrive\Desktop\Applications\KnowledgeBase-Quotes\data\brain.db`
- Query entries:
  ```sql
  SELECT id, title, type, author, tags, favorite FROM entries WHERE title IS NOT NULL AND length(trim(title)) > 0;
  ```
- Query links:
  ```sql
  SELECT source_id, target_id FROM links WHERE target_id IS NOT NULL;
  ```

### 2.2 Synthetic Entity Hub Generation
In addition to standard note entries, the server synthesizes 3 kinds of entity hubs:
1. **Author Hubs**:
   - `id`: `author:{authorName}`
   - `type`: `"author"`
   - `shape`: `NodeShape.Diamond`
   - `color`: `#A855F7`
   - Edge: Note $\to$ Author Hub
2. **Tag Hubs**:
   - Split `tags` column by comma `,`, trim and lowercase each tag.
   - `id`: `tag:{tagName}`
   - `type`: `"tag"`
   - `label`: `#{tagName}`
   - `shape`: `NodeShape.Hexagon`
   - `color`: `#10B981`
   - Edge: Note $\to$ Tag Hub
3. **Folder / Category Hubs**:
   - `id`: `folder:{type}`
   - `type`: `"folder"`
   - `label`: `{type.ToUpper()}`
   - `shape`: `NodeShape.Square`
   - `color`: `#F97316`
   - Edge: Note $\to$ Category Hub

### 2.3 Color Palette & Theme Tokens
```csharp
public static class KnowledgeMapPalette
{
    public static readonly Color BackgroundDark    = Color.FromArgb(255, 0x0B, 0x0D, 0x10); // #0B0D10
    public static readonly Color VignetteCenter     = Color.FromArgb(255, 0x14, 0x1A, 0x24); // #141A24
    public static readonly Color EdgeFilament       = Color.FromArgb(255, 150, 165, 190);    // RGB(150, 165, 190)
    public static readonly Color TextHalo           = Color.FromArgb(230, 0x0B, 0x0D, 0x10); // #0B0D10 (0.9 alpha)
    public static readonly Color TextMuted          = Color.FromArgb(255, 190, 198, 212);    // #BEC6D4
    public static readonly Color FavoriteRing       = Color.FromArgb(255, 0xF5, 0xB9, 0x42); // #F5B942

    public static readonly Color ColorNote          = Color.FromArgb(255, 0x5B, 0x8D, 0xEF); // #5B8DEF
    public static readonly Color ColorTrouble       = Color.FromArgb(255, 0xE2, 0x6D, 0x80); // #E26D80
    public static readonly Color ColorQuote         = Color.FromArgb(255, 0xE3, 0xA8, 0x57); // #E3A857
    public static readonly Color ColorSaying        = Color.FromArgb(255, 0x4F, 0xBF, 0x9F); // #4FBF9F
    public static readonly Color ColorJoke          = Color.FromArgb(255, 0x9C, 0x7B, 0xEA); // #9C7BEA
    public static readonly Color ColorDiary         = Color.FromArgb(255, 0xEC, 0x48, 0x99); // #EC4899
    public static readonly Color ColorBook          = Color.FromArgb(255, 0x06, 0xB6, 0xD4); // #06B6D4
    public static readonly Color ColorRecipe        = Color.FromArgb(255, 0x84, 0xCC, 0x16); // #84CC16
    public static readonly Color ColorAuthor        = Color.FromArgb(255, 0xA8, 0x55, 0xF7); // #A855F7 (Diamond)
    public static readonly Color ColorTag           = Color.FromArgb(255, 0x10, 0xB9, 0x81); // #10B981 (Hexagon)
    public static readonly Color ColorFolder        = Color.FromArgb(255, 0xF9, 0x73, 0x16); // #F97316 (Square)
    public static readonly Color ColorFallback      = Color.FromArgb(255, 0x8B, 0x93, 0xA1); // #8B93A1
}
```

---

## 3. Physical Clustering vs Static Fermat Spiral

### 3.1 The Problem
The existing `lively-hertz` implementation lays out all nodes along a single sunflower/golden-angle spiral. As a result, related notes are scattered, and connecting edges cross the entire screen like messy wires.

### 3.2 Solution: 60-Step Force Relaxation Layout
When building the graph in `ComputeGraphLayout(KnowledgeMapData data)`:
1. **Category Radial Anchors**:
   Arrange distinct types around a circle of radius $R = 240\text{px}$:
   $$\text{Anchor}_i = \left(240 \cdot \cos\left(\frac{i}{N} \cdot 2\pi - \frac{\pi}{2}\right), 240 \cdot \sin\left(\frac{i}{N} \cdot 2\pi - \frac{\pi}{2}\right)\right)$$
2. **Initial Placement**:
   Place each node near its category anchor plus a small random jitter:
   $$x_0 = \text{Anchor}(type).x + (\text{rand} - 0.5) \cdot 40$$
   $$y_0 = \text{Anchor}(type).y + (\text{rand} - 0.5) \cdot 40$$
3. **Run 60 Relaxation Steps**:
   - **Hooke Link Attraction**:
     For each edge $(A, B)$:
     $$\text{dist}_{\text{target}} = \text{Clamp}(42 + \sqrt{\max(deg_A, deg_B)} \cdot 14, 50, 160)$$
     $$\vec{F}_{\text{link}} = \frac{\vec{B} - \vec{A}}{\text{dist}} \cdot (\text{dist} - \text{dist}_{\text{target}}) \cdot k_{\text{link}}$$
     where $k_{\text{link}} = 0.75$ (or $0.45$ if $\max(deg_A, deg_B) > 15$).
   - **Coulomb Many-Body Repulsion**:
     For each pair of nodes $(i, j)$:
     $$\text{Repulsion Strength } S = \begin{cases} -260 & deg > 15 \\ -150 & deg > 5 \\ -35 & deg = 0 \\ -85 & \text{default} \end{cases}$$
     $$\vec{F}_{\text{repulse}} = \frac{\vec{A} - \vec{B}}{\text{dist}^2} \cdot |S|$$
   - **Collision Buffer**:
     Ensure minimum distance $\ge r_A + r_B + 6\text{px}$.
   - **Velocity Damping**: Decay factor $0.62$ per step.
4. **Node Radius Formula**:
   $$r_n = 3.2 + \sqrt{\max(0, degree_n)} \cdot 2.1$$
5. **Deterministic Spherical Harmonic 3D Depth ($Z$)**:
   $$\text{dist} = \sqrt{x^2 + y^2}$$
   $$z_{\text{base}} = \left(\frac{\text{dist}}{320}\right)^{1.4} \cdot 45 - 20$$
   $$z_{\text{harmonic}} = \sin(\text{id} \cdot 1.618 + (\text{isHub} ? 0 : 2.5)) \cdot 95$$
   $$Z = (z_{\text{base}} + z_{\text{harmonic}}) \cdot (\text{isHub} ? 0.6 : 1.0)$$

---

## 4. 3D Celestial Camera & Organic Animations

### 4.1 Camera Angles & Harmonic Breathing
- **Yaw (Continuous Orbital Rotation)**:
  $$\Delta \text{yaw} = 0.035 \cdot \text{speedMultiplier} \cdot dt \quad (\sim 180\text{s per full revolution})$$
- **Pitch (Harmonic Breathing Oscillation)**:
  Base pitch is $27.5^\circ$ ($0.48\text{ rad}$).
  $$\text{pitch} = 0.48 + \sin(\text{elapsedSeconds} \cdot 0.39) \cdot 0.18\text{ rad} \quad (\sim 16.1\text{s period})$$

### 4.2 3D Perspective Projection
For each node $(X + \Delta x, Y + \Delta y, Z)$ centered at screen center $(cx, cy)$:
- **Rotation around Y (Yaw)**:
  $$x_1 = rx \cdot \cos(\text{yaw}) - rz \cdot \sin(\text{yaw})$$
  $$z_1 = rz \cdot \cos(\text{yaw}) + rx \cdot \sin(\text{yaw})$$
- **Rotation around X (Pitch)**:
  $$y_2 = ry \cdot \cos(\text{pitch}) - z_1 \cdot \sin(\text{pitch})$$
  $$z_2 = z_1 \cdot \cos(\text{pitch}) + ry \cdot \sin(\text{pitch})$$
- **Perspective Scale & Screen Coordinates**:
  $$\text{depth} = 850 + z_2$$
  $$\text{scale} = \frac{850}{\max(120, \text{depth})}$$
  $$P_x = cx + x_1 \cdot \text{scale}, \quad P_y = cy + y_2 \cdot \text{scale}$$
  $$\text{zAlpha} = \text{Clamp}\left(1.0 - \frac{z_2}{500} \cdot 0.65, 0.3, 1.0\right)$$

### 4.3 Individual Sinusoidal Floating Wave Drift
To avoid rigid body appearance, each node drifts with a zero-mean harmonic wave:
$$\Delta x = \cos(\text{elapsed} \cdot 1.2 + \text{id} \cdot 1.73) \cdot 4.0\text{px}$$
$$\Delta y = \sin(\text{elapsed} \cdot 0.9 + \text{id} \cdot 2.41) \cdot 4.0\text{px}$$

---

## 5. Cascading Synaptic Neural Pulses

Every 6.0 seconds, a neural firing wave travels hop-by-hop outward across the graph:
- **Cycle Period**: $T_{\text{cycle}} = 6000\text{ms}$.
- **Active Window**: First 65% ($0 \le t < 3900\text{ms}$). Remaining 35% is a quiet resting refractory period.
- **Hop Progression**:
  Run a BFS from the largest hub to tag each edge with its `hop` distance ($1, 2, \dots, H$).
  Normalized pulse progress $P = \frac{t_{\text{active}}}{3900\text{ms}}$.
  An edge at hop $h$ is active when:
  $$\frac{h - 1}{H} \le P \le \frac{h}{H}$$
  Normalized progress within the hop:
  $$p_{\text{hop}} = \frac{P - \frac{h-1}{H}}{\frac{1}{H}} \in [0, 1]$$
- **Visuals during Active Hop**:
  1. **Electric Edge Beam**:
     Draw the edge line with color `node.Color` and alpha $0.40 \cdot \text{hopDecay} \cdot \min(zAlpha_A, zAlpha_B)$.
  2. **Traveling White-Hot Synaptic Spark**:
     $$x_{\text{spark}} = x_A + (x_B - x_A) \cdot p_{\text{hop}}$$
     $$y_{\text{spark}} = y_A + (y_B - y_A) \cdot p_{\text{hop}}$$
     - Draw neon halo sprite around spark with color `dstNode.Color`.
     - Draw center white circle ($r = 2.8\text{px} \cdot scale$, `#FFFFFF`).
  3. **Arrival Shockwave Flash Ring**:
     When $p_{\text{hop}} > 0.70$:
     Draw expanding ring around destination node with radius $r_B \cdot scale + 4\text{px}$ and alpha $\sin\left(\frac{p_{\text{hop}} - 0.70}{0.30} \cdot \pi\right) \cdot 0.65$.

---

## 6. Win2D Rendering Pipeline (`KnowledgeMapControl.cs`)

Implement the rendering session inside `OnCanvasDraw` with the following distinct passes:

### Pass 1: Background Vignette
Draw a radial gradient or gradient mesh centered at $(0.5W, 0.45H)$ from `#141A24` fading to `#0B0D10`.

### Pass 2: Base Filament Edges
Draw lines between connected nodes:
- Color: `Color.FromArgb((byte)(avgAlpha * 48), 150, 165, 190)`
- Width: $\max(0.7\text{px}, 0.8\text{px} \cdot scale)$

### Pass 3: Active Synaptic Beams & Sparks
Draw the active hop edges, traveling sparks, and arrival flashes as described in Section 5.

### Pass 4: Additive Glow Pass (`CanvasComposite.Add`)
Change composite mode to additive:
```csharp
using (ds.CreateLayer(1.0f, CanvasComposite.Add))
{
    foreach (var node in nodes)
    {
        float glowRadius = node.Radius * node.Scale * 3.6f;
        float pulse = MathF.Sin(elapsed * 2.0f + node.Id * 0.73f) * 0.35f;
        byte glowAlpha = (byte)Math.Clamp((int)(node.ZAlpha * (45 + pulse * 15)), 0, 255);
        // Draw soft radial glow circle
        ds.FillCircle(node.Px, node.Py, glowRadius * (1.0f + pulse * 0.15f), 
                      Color.FromArgb(glowAlpha, node.Color.R, node.Color.G, node.Color.B));
    }
}
```

### Pass 5: Depth-Sorted Node Bodies & Shapes (`CanvasComposite.SourceOver`)
Sort nodes back-to-front by $Z_{\text{depth}}$ (Painter's algorithm):
- **Diamond (Author)**:
  Draw polygon with 4 vertices:
  $(P_x, P_y - 1.25r), (P_x + 1.25r, P_y), (P_x, P_y + 1.25r), (P_x - 1.25r, P_y)$.
- **Hexagon (Tag)**:
  Draw 6-sided regular polygon with radius $1.15r$.
- **Square (Folder / Category)**:
  Draw rectangle $(P_x - r, P_y - r, 2r, 2r)$.
- **Circle (Standard Notes)**:
  Draw circle with radius $r$.
- **Favorite Ring**:
  If `node.Favorite`, stroke an outer ring at radius $r + 2.5\text{px}$ with `#F5B942`.
- **White Rim Stroke**:
  Stroke node outline with `rgba(255, 255, 255, 0.22)` (or `0.45` for hubs).
- **Celestial Hub Ripple Waves**:
  For hubs ($deg \ge 3$ or favorite):
  $$\text{rippleT} = (\text{elapsed} \cdot 0.45 + \text{id} \cdot 0.23) \pmod{1.0}$$
  Stroke circle with radius $r + \text{rippleT} \cdot 14\text{px} \cdot scale$ and color `node.Color` at alpha $(1.0 - \text{rippleT}) \cdot 0.28$.

### Pass 6: Typography with Collision Culling
1. **Anchor Position**:
   Centered directly **below** the node:
   $$X_{\text{label}} = P_x, \quad Y_{\text{label}} = P_y + r \cdot scale + 4\text{px}$$
   `CanvasTextFormat`: `HorizontalAlignment = Center`, `VerticalAlignment = Top`.
2. **Culling Rules**:
   - Only consider nodes where $scale \ge 0.78$ or $deg \ge 2$ or $isHub$.
   - Compute candidate bounding box:
     $$[X - \frac{W}{2} - 4, Y, X + \frac{W}{2} + 4, Y + 15]$$
   - Priority sort: Hubs $\to$ High degree $\to$ Foreground.
   - If bounding box intersects any already placed label, suppress it.
3. **Double-Pass Legibility Halo**:
   - Pass 1 (Halo): Draw text offset by $\pm 1\text{px}$ with `#0B0D10` (alpha $0.9$).
   - Pass 2 (Text): Draw text with `#BEC6D4` (or `#FFFFFF` for hubs) with alpha $node.ZAlpha$.
