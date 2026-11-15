"""Prompt Builder: turns design-DB entries into deterministic prompts (same input -> same prompt)."""
from __future__ import annotations

import json

from .paths import SEED

STYLE_HEADER = (
    "Premium HD 2D chibi fantasy monster illustration for a collectible card game. "
    "Original creature design. Big head, small body, large expressive glossy eyes with white highlights, "
    "clean crisp thin colored outlines, soft cel shading with gentle gradients and a soft rim light, "
    "harmonious bright pastel-leaning palette, clear readable silhouette, polished and consistent quality."
)

ELEMENT_MOTIF = {
    "fire": "fire element accents: warm embers and a gentle warm glow",
    "water": "water element accents: bubbles, small waves and pearls",
    "nature": "nature element accents: leaves, sprouts and small flowers",
    "earth": "earth element accents: smooth stones, crystals and sand",
    "lightning": "lightning element accents: zigzag sparks and crackling static",
    "wind": "wind element accents: soft swirls, feathers and wisps of cloud",
    "light": "light element accents: halos, floating light motes and soft rays",
    "dark": "dark element accents: crescent moons, sparkling dust and soft shadow wisps",
}

STAGE_NOTE = {
    1: "Baby form: very small, round and cute, the simplest version of the design.",
    2: "Evolved form: a natural growth of the baby form, slightly larger and more confident, more defined details.",
    3: "Final evolution: the grown, most majestic and ornate version, still chibi and cute, clearly the same lineage.",
}

COMPOSITION = (
    "Full body, three-quarter view, centered, the whole creature fits inside the frame with comfortable margin, "
    "isolated on a transparent background, no ground shadow."
)

NEGATIVE = (
    "Avoid: any text, letters, numbers, watermark, signature, logo, card frame, border, UI elements, humans, "
    "photorealism, 3D render look, horror, gore, extra limbs, cropped body, and any resemblance to creatures "
    "from existing games, anime or brands."
)


def load_pets() -> list[dict]:
    data = json.loads((SEED / "pets.json").read_text())
    out = []
    for fam in data["families"]:
        for form in fam["forms"]:
            out.append({**form, "family": fam})
    return out


def pet_prompt(form: dict) -> str:
    fam = form["family"]
    stage_note = STAGE_NOTE[form["stage"]] if fam["stages"] > 1 else "Standalone creature that does not evolve."
    return "\n".join([
        STYLE_HEADER,
        f"Creature: {form['art']}.",
        f"Signature family traits that must be visible: {fam['identity']}.",
        stage_note,
        ELEMENT_MOTIF[fam["element"]] + ".",
        COMPOSITION,
        NEGATIVE,
    ])
