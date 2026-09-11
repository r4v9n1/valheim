# HERECOMESTHEVEIN PROJECT RULES

Authoritative workspace:

    G:\My Drive\build\Valheim\workspace\HereComesTheVein

Scope:
- This project is independent of LiquidCore and Terramizer.
- Do not read, edit, copy, move, build, clean, reset, or otherwise operate on the LiquidCore workspace for HereComesTheVein work.
- Do not introduce a LiquidCore dependency.

Game assembly authority (read-only):

    G:\My Drive\build\assembly_valheim.dll

Disposable output:
- Compiler output, obj/bin, package staging, diagnostics, and scratch work belong under:

    %LOCALAPPDATA%\R4V9N1\HereComesTheVein\

Release ZIPs belong under:

    G:\My Drive\build\Valheim\releases\HereComesTheVein\

Development principle:
- Preserve vanilla copper prefab/world-generation identity.
- Convert only a deterministic minority of copper vein instances at runtime.
- Preserve vanilla copper drop quantities/chances; substitute IronOre for CopperOre only.
- Keep the mod safe for existing worlds and removable without rewriting world data.
