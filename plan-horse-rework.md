# Implementation Plan: Horse Rework

All changes are data-only — `creatures.json` + `genes.json`. No code changes.

## 1. Remove legendary mane gene
- **Files:** `assets/genes.json` (modify)
- **What:** Delete the `spec.horse.stormmane` entry (lines 38-42)
- **Why:** User wants legendary horse mane removed. Common `horse.mat.mane` stays as craft material.
- **Depends on:** none

## 2. Remove mane visual from horse body plan
- **Files:** `assets/creatures.json` (modify)
- **What:** Delete the Appendage part (mane visual) from the horse body plan
- **Why:** No more mane on the horse. Freed Appendage slot can be repurposed later.
- **Depends on:** none

## 3. Change horse body from Box to Cylinder
- **Files:** `assets/creatures.json` (modify)
- **What:** Change Core part Shape from `"Box"` to `"Cylinder"`, adjust Size from `[0.50, 0.60, 1.20]` to `[0.65, 0.60, 0.65]` (radius=0.325 for similar volume)
- **Why:** User wants cylindrical horse body
- **Depends on:** none

## 4. Reshape horse head to include neck + snout
- **Files:** `assets/creatures.json` (modify)
- **What:** Change Head part: Shape `"Capsule"` → `"Box"`, Size `[0.26, 0.40, 0.26]` → `[0.22, 0.55, 0.60]`, Socket `[0.00, 0.95, 0.58]` → `[0.00, 1.10, 0.48]`. Tint stays `"#6E4522"`, Role stays `"Head"`.
- **Why:** Box with tall Y + deep Z reads as neck + snout in one part. The wider Z (0.60) gives a pronounced horse snout; the taller Y (0.55) covers neck length from body top to head top.
- **Depends on:** 3 (body shape change may shift where head sits)

## 5. Reposition eyes to match new head shape
- **Files:** `assets/creatures.json` (modify)
- **What:** Eye sockets from `[-0.11, 1.02, 0.72]` / `[0.11, 1.02, 0.72]` → `[-0.11, 1.22, 0.70]` / `[0.11, 1.22, 0.70]`
- **Why:** Head shifted up (socket y 0.95→1.10) and deepened (z half 0.26→0.30). Eyes go to upper-front of new head box. Exact values may need tuning after visual check.
- **Depends on:** 4

## 6. Lengthen horse legs
- **Files:** `assets/creatures.json` (modify)
- **What:** All 4 Locomotion parts: Size Y from `0.50` to `0.70` (legs 40% longer). Sockets unchanged.
- **Why:** User wants longer horse legs
- **Depends on:** 3 (sockets may need minor adjustment if cylinder body changes visual footprint)

## 7. Make horse legs gene Rare
- **Files:** `assets/genes.json` (modify)
- **What:** Change `horse.mat.legs` Rarity from `"Common"` to `"Rare"`
- **Why:** User wants horse legs gene to be rare
- **Depends on:** none
