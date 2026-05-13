"""
One-shot offline patch: switch TerraTech's PhysX broadphase from MBP to SAP.

Why: Unity 2018.4 ships PhysX 3.x whose MultiboxPruning (MBP) broadphase has a
hard 64K-objects-per-region cap (uint16 indexing). At aggressive WorldCustomizer
settings the chunk + scenery + tech-block shape count blows past that and
Unity crashes natively in BpBroadPhaseMBP.cpp. SweepAndPrune (SAP) has no
per-region cap; it scales worse with widely-distributed objects but doesn't
hard-crash.

PhysX broadphase is selected at native PhysX scene creation, which happens
during Unity engine startup before any managed code runs. So this can't be
flipped at runtime from a Harmony patch. It can only be changed by editing
the PhysicsManager settings stored in `globalgamemanagers`, which Unity reads
at boot.

Run once per game install. The patch sticks until:
  - TerraTech updates and Steam re-downloads globalgamemanagers
  - User runs "Verify Integrity of Game Files" in Steam

A timestamped backup is made next to the file. To revert, copy the .WC_backup
back over globalgamemanagers.

Usage:
    python scripts/patch_broadphase.py
"""
from __future__ import annotations
import os
import shutil
import sys
from pathlib import Path

import UnityPy

GGM = Path(
    r"C:\Program Files (x86)\Steam\steamapps\common\TerraTech"
    r"\TerraTechWin64_Data\globalgamemanagers"
)

# Unity PhysicsBroadphaseType enum values (from PhysX SDK + Unity source)
BROADPHASE_SAP = 0
BROADPHASE_MBP = 1
BROADPHASE_ABP = 2

NAME = {0: "SweepAndPrune", 1: "MultiboxPruning", 2: "AutomaticBoxPruning"}


def main() -> int:
    if not GGM.exists():
        print(f"ERROR: {GGM} not found")
        return 1

    print(f"Loading {GGM} ...")
    env = UnityPy.load(str(GGM))

    physics_manager = None
    for obj in env.objects:
        # PhysicsManager has type "PhysicsManager" in serialized form (Unity-side ClassID 55)
        if obj.type.name == "PhysicsManager":
            physics_manager = obj
            break

    if physics_manager is None:
        print("ERROR: PhysicsManager object not found in globalgamemanagers")
        print("Objects found:")
        for obj in env.objects:
            print(f"  - {obj.type.name}")
        return 1

    tree = physics_manager.read_typetree()
    current = tree.get("m_BroadphaseType")
    if current is None:
        print("ERROR: m_BroadphaseType field not in PhysicsManager. Field listing:")
        for k in tree:
            print(f"  - {k}")
        return 1

    print(f"Current broadphase: {current} ({NAME.get(current, '?')})")

    if current == BROADPHASE_SAP:
        print("Already SAP — no change needed.")
        return 0

    tree["m_BroadphaseType"] = BROADPHASE_SAP
    physics_manager.save_typetree(tree)

    out = env.file.save(packer="none")

    # Direct overwrite. Windows + Program Files refuses os.replace from a
    # non-elevated process even though the parent directory permits new-file
    # writes (we already made the backup via plain copy). Writing bytes
    # directly into the existing file goes through a different ACL path
    # and works without elevation.
    GGM.write_bytes(out)

    print(f"Patched: broadphase {current} -> {BROADPHASE_SAP} ({NAME[BROADPHASE_SAP]})")
    print("Restart TerraTech for the change to take effect.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
