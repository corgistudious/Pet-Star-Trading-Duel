# Pet Star — Art Style Bible (v1)

## Core look
**Premium HD 2D Chibi Fantasy Monster.** Original creatures only — never imitate creatures, silhouettes,
iconography or art direction from any existing franchise.

| Aspect | Rule |
|---|---|
| Proportions | Head ≈ 40–50% of total height (stage 1), 35–40% (stage 2), 30–35% (stage 3). Small body, short limbs. |
| Eyes | Large, expressive, glossy with 1–2 white highlights. Eye style is a **family trait** and never changes across evolution. |
| Line | Clean, crisp, thin dark-tinted outline (not black), slightly thicker on the outer silhouette. |
| Shading | Soft cel shading + gentle gradient, one soft rim light, subtle ambient occlusion. No harsh noise or painterly texture. |
| Palette | Harmonious, 1 dominant + 1 secondary + 1 accent color. Dominant color is a family trait. Pastel-leaning, high clarity. |
| Silhouette | Must read clearly at 128 px. One signature shape per family (tail flame, coral crown, bolt stripes…). |
| Pose | Full body, 3/4 view, centered, whole creature inside frame with ~8% margin. |
| Background | Transparent (preferred). If unsupported: flat very light neutral background that is removed in post. |
| Text | **Never** any text, letters, numbers, logos, watermarks, card frames or UI in the artwork. |

## Evolution consistency
Each family has an `identity` string in `database/seed/pets.json`. It is injected in every form's prompt so
color, eye type, ears, horns, tail, pattern and element motif carry over. Later stages grow larger, more
confident and more ornate — a natural growth of the previous form, never a redesign.

## Element motifs
Fire: embers, warm glow · Water: bubbles, waves, pearls · Nature: leaves, sprouts, flowers · Earth: stone, crystal, sand ·
Lightning: zigzags, sparks, static · Wind: swirls, feathers, clouds · Light: halos, motes, rays · Dark: moons, dust, shadow wisps.

## Negative constraints (always appended)
no text, no letters, no numbers, no watermark, no signature, no logo, no card frame, no border, no UI,
no human characters, no realistic photo style, no 3D render look, no horror or gore, no extra limbs,
no cropped body, not a copy of any existing franchise creature.

## Prompt template
See `asset-pipeline/petstar_assets/prompts.py`. Order: style header → creature description → family identity →
element motif → composition → negative constraints.

## Card integration
Artwork is a standalone transparent PNG. The 9:16 card frame, name, stats, rarity color and finish effects are
rendered by Unity from `assets-source/card-template/` — AI never renders text or stats.
