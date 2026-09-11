# Changelog

## 0.1.8

- Added loaded-scene detection for an Elder trophy displayed near an OfferingBowl altar.

## 0.1.7

- Added an activated Elder OfferingBowl trophy fallback for restored worlds missing the global Elder defeat key.

## 0.1.6

- IronOre drops from copper veins now begin only after the Elder has been defeated.

## 0.1.0

- Replaced the package icon with a custom-made, non-AI-generated icon.

- Initial HereComesTheVein build.
- Moved the CopperOre/IronOre substitution to Valheim's final `DropTable.GetDropList` boundary so mining results reliably average 60% CopperOre and 40% IronOre.
- Matched the standard Thunderstore package layout: `plugins/HereComesTheVein/HereComesTheVein.dll`.
- Replaced the experimental Iron Vein conversion with loot-only copper vein augmentation: weighted drops average 60% CopperOre and 40% IronOre.
- Applies on dedicated servers and clients through BepInEx/Harmony; Jotunn is not required.
- Replaced the package icon with a custom-made, non-AI-generated icon.
- Converts a deterministic 20% subset of copper vein sites into Iron Veins.
- Adds black/silver runtime material treatment.
- Preserves copper vein drop quantities/chances while replacing CopperOre with IronOre.
- Supports existing worlds without rewriting world-generation data.
- Adds Windows client build and Thunderstore package automation.
